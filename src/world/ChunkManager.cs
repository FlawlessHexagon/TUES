using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace TheUniversalEntertainmentSystem;

/// <summary>
/// Orchestrates the lifecycle of all chunks in the active world.
/// Handles the asynchronous treadmill that loads chunks ahead of a reference point
/// and unloads them behind it, strictly enforcing main-thread dictionary access
/// and background thread chunk generation.
/// </summary>
public partial class ChunkManager : Node3D
{
	// ── Configuration ───────────────────────────────────────────────────────

	/// <summary>
	/// The world-space focal point. Chunks are generated around this point.
	/// </summary>
	public Vector3 ReferencePosition { get; set; } = Vector3.Zero;

	/// <summary>
	/// Radius (in chunk units) within which chunks are loaded.
	/// </summary>
	public int LoadDistance { get; set; } = 8;

	/// <summary>
	/// Radius (in chunk units) beyond which chunks are unloaded.
	/// Must be greater than LoadDistance to prevent hysteresis thrashing.
	/// </summary>
	public int UnloadDistance { get; set; } = 10;

	/// <summary>
	/// The global seed for the world terrain generation.
	/// </summary>
	[Export] public int WorldSeed { get; set; } = 1337;

	// Dynamic ThreadPool limits based on active CPU core count to prevent starvation
	private static readonly int MaxConcurrentGenerations = Math.Max(2, System.Environment.ProcessorCount - 2);
	private static readonly int MaxConcurrentMeshes = Math.Max(2, System.Environment.ProcessorCount / 2);
	
	// Throttles main-thread SceneTree modifications (MeshInstance3D creation)
	private const int MaxMeshesAttachedPerFrame = 10;

	// ── State ───────────────────────────────────────────────────────────────

	// Uses ConcurrentDictionary to allow safe background neighbour queries during meshing.
	private readonly ConcurrentDictionary<Vector3I, Chunk> _activeChunks = new();
	// MUST be accessed from the main thread ONLY.
	private readonly Dictionary<Vector3I, MeshInstance3D> _activeMeshesMap = new();
	private readonly Dictionary<Vector3I, StaticBody3D> _activeCollisions = new();

	// Thread-safe queue for worker threads to hand meshes back to the main thread.
	private readonly ConcurrentQueue<(Chunk chunk, MeshResult result)> _meshAttachmentQueue = new();

	// ── Concurrency Counters ────────────────────────────────────────────────
	
	private int _activeGenerations = 0;
	private int _activeMeshes = 0;

	// ── Queue Caching ───────────────────────────────────────────────────────

	private Vector3I _lastCenterChunk = new(int.MaxValue, int.MaxValue, int.MaxValue);
	private readonly List<(Vector3I pos, int distSq)> _missingChunksQueue = new();

	// ── Godot Lifecycle ─────────────────────────────────────────────────────

	public override void _Ready()
	{
		// Ensure textures are prepared
		Image atlasImage = ChunkMesher.CreateAtlasImage();
		ImageTexture atlasTexture = ImageTexture.CreateFromImage(atlasImage);
		ChunkMesher.Initialize(atlasTexture);

		// Initialize the world generator's cache and noise module
		WorldGenerator.Initialize(WorldSeed);
	}

	public override void _Process(double delta)
	{
		Vector3I centerChunk = GetChunkCoordinate(ReferencePosition);

		ProcessUnloading(centerChunk);
		ProcessGeneration(centerChunk);
		ProcessMeshing();
		ProcessMeshAttachments();
	}

	// ── Pipeline Stages ─────────────────────────────────────────────────────

	private void ProcessUnloading(Vector3I centerChunk)
	{
		// Use a list to defer removals and avoid modifying the dictionary during iteration.
		List<Vector3I>? toRemove = null;

		foreach (var kvp in _activeChunks)
		{
			Vector3I pos = kvp.Key;
			Chunk chunk = kvp.Value;

			// Calculate Chebyshev distance (max axis distance)
			int dx = Math.Abs(pos.X - centerChunk.X);
			int dy = Math.Abs(pos.Y - centerChunk.Y);
			int dz = Math.Abs(pos.Z - centerChunk.Z);
			int dist = Math.Max(dx, Math.Max(dy, dz));

			if (dist > UnloadDistance)
			{
				// Concurrency Guard: Do not unload if still generating or meshing on a worker thread.
				if (chunk.State == ChunkState.Generating || chunk.State == ChunkState.Meshing)
					continue;

				toRemove ??= new List<Vector3I>();
				toRemove.Add(pos);
			}
		}

		if (toRemove is null) return;

		foreach (Vector3I pos in toRemove)
		{
			// Remove Visuals
			if (_activeMeshesMap.Remove(pos, out var meshInstance))
			{
				RemoveChild(meshInstance);
				meshInstance.QueueFree();
			}

			// Remove Collisions
			if (_activeCollisions.Remove(pos, out var staticBody))
			{
				RemoveChild(staticBody);
				staticBody.QueueFree();
			}

			// Return memory to pool
			if (_activeChunks.TryRemove(pos, out var removedChunk))
			{
				removedChunk.Dispose();
			}
		}
	}

	private void ProcessGeneration(Vector3I centerChunk)
	{
		// 1. Rebuild and cache the generation queue only when the center chunk changes
		if (centerChunk != _lastCenterChunk)
		{
			_lastCenterChunk = centerChunk;
			_missingChunksQueue.Clear();

			for (int x = -LoadDistance; x <= LoadDistance; x++)
			{
				for (int y = -LoadDistance; y <= LoadDistance; y++)
				{
					for (int z = -LoadDistance; z <= LoadDistance; z++)
					{
						Vector3I pos = centerChunk + new Vector3I(x, y, z);
						
						// Squared Euclidean distance for spherical culling
						int distSq = x * x + y * y + z * z;
						
						if (distSq <= LoadDistance * LoadDistance && !_activeChunks.ContainsKey(pos))
						{
							_missingChunksQueue.Add((pos, distSq));
						}
					}
				}
			}

			// Sort by distance descending (farthest first) for O(1) popping from the end
			_missingChunksQueue.Sort((a, b) => b.distSq.CompareTo(a.distSq));
		}

		// 2. Dispatch tasks while respecting ThreadPool limits
		while (_missingChunksQueue.Count > 0 && Volatile.Read(ref _activeGenerations) < MaxConcurrentGenerations)
		{
			// O(1) pop closest chunk
			int lastIndex = _missingChunksQueue.Count - 1;
			var item = _missingChunksQueue[lastIndex];
			_missingChunksQueue.RemoveAt(lastIndex);

			Vector3I pos = item.pos;

			// Re-verify it hasn't been generated since being queued
			if (_activeChunks.ContainsKey(pos))
				continue;

			var chunk = new Chunk(pos) { State = ChunkState.Generating };
			_activeChunks.TryAdd(pos, chunk);

			Interlocked.Increment(ref _activeGenerations);

			// Dispatch to .NET ThreadPool
			Task.Run(() => 
			{
				try 
				{
					WorldGenerator.GenerateChunk(chunk);
				}
				finally
				{
					Interlocked.Decrement(ref _activeGenerations);
				}
			});
		}
	}

	private void ProcessMeshing()
	{
		// The neighbour lookup delegate passed to the mesher.
		// Uses ultra-fast bitwise math to resolve global to local coords.
		Func<int, int, int, ushort> neighbourLookup = (globalX, globalY, globalZ) =>
		{
			// Signed shift >> 4 acts as floor division by 16.
			Vector3I neighbourPos = new(globalX >> 4, globalY >> 4, globalZ >> 4);
			
			if (_activeChunks.TryGetValue(neighbourPos, out var nChunk))
			{
				// Bitwise AND 15 acts as modulo 16.
				return nChunk.GetVoxel(globalX & 15, globalY & 15, globalZ & 15);
			}
			return VoxelRegistry.AirId;
		};

		// Define cardinal directions for the Culling Synchronization Rule
		Vector3I[] neighbours = {
			Vector3I.Right, Vector3I.Left, 
			Vector3I.Up, Vector3I.Down, 
			Vector3I.Forward, Vector3I.Back
		};

		foreach (var kvp in _activeChunks)
		{
			if (Volatile.Read(ref _activeMeshes) >= MaxConcurrentMeshes)
				break;

			Chunk chunk = kvp.Value;
			if (chunk.State != ChunkState.Generated)
				continue;

			// ── Culling Synchronization Rule ──
			// Wait until all 6 cardinal neighbours exist and are fully generated.
			bool neighboursReady = true;
			foreach (Vector3I offset in neighbours)
			{
				Vector3I nPos = chunk.Position + offset;
				if (!_activeChunks.TryGetValue(nPos, out var nChunk) || nChunk.State < ChunkState.Generated)
				{
					neighboursReady = false;
					break;
				}
			}

			if (!neighboursReady)
				continue;

			// Mark as Meshing so it isn't dispatched twice or unloaded prematurely
			chunk.State = ChunkState.Meshing;
			Interlocked.Increment(ref _activeMeshes);

			// Dispatch to .NET ThreadPool
			Task.Run(() =>
			{
				try 
				{
					MeshResult? result = ChunkMesher.BuildMesh(chunk, neighbourLookup);
					
					if (result.HasValue)
					{
						_meshAttachmentQueue.Enqueue((chunk, result.Value));
					}
					else
					{
						// If the chunk is completely empty, skip attachment entirely.
						chunk.State = ChunkState.Meshed;
					}
				}
				finally 
				{
					Interlocked.Decrement(ref _activeMeshes);
				}
			});
		}
	}

	private void ProcessMeshAttachments()
	{
		// Process up to MaxMeshesBuiltPerFrame attachments per frame to prevent GC/GPU stutter
		int attachedThisFrame = 0;
		while (attachedThisFrame < MaxMeshesAttachedPerFrame && _meshAttachmentQueue.TryDequeue(out var item))
		{
			Chunk chunk = item.chunk;
			MeshResult result = item.result;

			// Edge case: Chunk was scheduled for destruction while mesh was in queue.
			// The unload process waits for State != Meshing, but let's be safe.
			if (!_activeChunks.ContainsKey(chunk.Position))
			{
				continue;
			}

			// Attach Visuals
			var meshInstance = new MeshInstance3D
			{
				Mesh = result.Mesh,
				Position = chunk.WorldPosition
			};
			AddChild(meshInstance);
			_activeMeshesMap[chunk.Position] = meshInstance;

			// Attach Collisions
			if (result.CollisionShape is not null)
			{
				var staticBody = new StaticBody3D { Position = chunk.WorldPosition };
				var collisionShape = new CollisionShape3D
				{
					Shape = result.CollisionShape
				};
				staticBody.AddChild(collisionShape);
				AddChild(staticBody);
				_activeCollisions[chunk.Position] = staticBody;
			}

			chunk.State = ChunkState.Meshed;
			attachedThisFrame++;
		}
	}

	// ── Helpers ─────────────────────────────────────────────────────────────

	/// <summary>
	/// Converts a world coordinate to a chunk grid coordinate using optimal
	/// floor math (capable of handling negative coordinates cleanly).
	/// </summary>
	private static Vector3I GetChunkCoordinate(Vector3 worldPosition)
	{
		return new Vector3I(
			Mathf.FloorToInt(worldPosition.X / Chunk.SizeX),
			Mathf.FloorToInt(worldPosition.Y / Chunk.SizeY),
			Mathf.FloorToInt(worldPosition.Z / Chunk.SizeZ)
		);
	}
}
