using System;
using System.Collections.Generic;
using System.Linq;
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

	// Limit operations per frame to maintain 60+ FPS
	// Pushing higher dispatch limits heavily utilizes the .NET ThreadPool
	private const int MaxGenerationsDispatchedPerFrame = 64;
	
	// Throttles main-thread SceneTree modifications (MeshInstance3D creation)
	private const int MaxMeshesBuiltPerFrame = 10;

	// ── State ───────────────────────────────────────────────────────────────

	// MUST be accessed from the main thread ONLY.
	private readonly Dictionary<Vector3I, Chunk> _activeChunks = new();
	private readonly Dictionary<Vector3I, MeshInstance3D> _activeMeshes = new();
	private readonly Dictionary<Vector3I, StaticBody3D> _activeCollisions = new();

	// ── Godot Lifecycle ─────────────────────────────────────────────────────

	public override void _Ready()
	{
		// Ensure textures are prepared
		Image atlasImage = ChunkMesher.CreateAtlasImage();
		ImageTexture atlasTexture = ImageTexture.CreateFromImage(atlasImage);
		ChunkMesher.Initialize(atlasTexture);
	}

	public override void _Process(double delta)
	{
		Vector3I centerChunk = GetChunkCoordinate(ReferencePosition);

		ProcessUnloading(centerChunk);
		ProcessGeneration(centerChunk);
		ProcessMeshing();
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
				// Concurrency Guard: Do not unload if still generating on a worker thread.
				if (chunk.State == ChunkState.Generating)
					continue;

				toRemove ??= new List<Vector3I>();
				toRemove.Add(pos);
			}
		}

		if (toRemove is null) return;

		foreach (Vector3I pos in toRemove)
		{
			// Remove Visuals
			if (_activeMeshes.Remove(pos, out var meshInstance))
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
			_activeChunks[pos].Dispose();
			_activeChunks.Remove(pos);
		}
	}

	private void ProcessGeneration(Vector3I centerChunk)
	{
		int generatedThisFrame = 0;

		// Prioritised list of missing chunks
		var missingChunks = new List<(Vector3I pos, int dist)>();

		// We use a spherical or cubic radius. Here we use cubic (Chebyshev)
		// but sort by Euclidean distance for organic rounded generation.
		for (int x = -LoadDistance; x <= LoadDistance; x++)
		{
			for (int y = -LoadDistance; y <= LoadDistance; y++)
			{
				for (int z = -LoadDistance; z <= LoadDistance; z++)
				{
					Vector3I pos = centerChunk + new Vector3I(x, y, z);

					if (!_activeChunks.ContainsKey(pos))
					{
						// Squared Euclidean distance for sorting
						int distSq = x * x + y * y + z * z;
						// Optional: Only queue if within a true sphere
						if (distSq <= LoadDistance * LoadDistance)
						{
							missingChunks.Add((pos, distSq));
						}
					}
				}
			}
		}

		if (missingChunks.Count == 0)
			return;

		// Sort by distance (closest first)
		missingChunks.Sort((a, b) => a.dist.CompareTo(b.dist));

		foreach (var item in missingChunks)
		{
			if (generatedThisFrame >= MaxGenerationsDispatchedPerFrame)
				break;

			Vector3I pos = item.pos;
			var chunk = new Chunk(pos) { State = ChunkState.Generating };
			
			// Immediately register in dictionary so it isn't queued again next frame
			_activeChunks.Add(pos, chunk);

			// Dispatch to .NET ThreadPool
			Task.Run(() => DummyGenerator.FillChunk(chunk));

			generatedThisFrame++;
		}
	}

	private void ProcessMeshing()
	{
		int meshedThisFrame = 0;

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
			if (meshedThisFrame >= MaxMeshesBuiltPerFrame)
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

			// Mesh it
			MeshResult? result = ChunkMesher.BuildMesh(chunk, neighbourLookup);
			
			if (result.HasValue)
			{
				// Attach Visuals
				var meshInstance = new MeshInstance3D
				{
					Mesh = result.Value.Mesh,
					Position = chunk.WorldPosition
				};
				AddChild(meshInstance);
				_activeMeshes[chunk.Position] = meshInstance;

				// Attach Collisions
				if (result.Value.CollisionShape is not null)
				{
					var staticBody = new StaticBody3D { Position = chunk.WorldPosition };
					var collisionShape = new CollisionShape3D
					{
						Shape = result.Value.CollisionShape
					};
					staticBody.AddChild(collisionShape);
					AddChild(staticBody);
					_activeCollisions[chunk.Position] = staticBody;
				}

				// Only increment the quota if we actually built a mesh.
				// This allows the engine to burn through hundreds of empty Air chunks
				// in a single frame without artificially bottlenecking the main thread.
				meshedThisFrame++;
			}

			// Advance state whether meshed or empty
			chunk.State = ChunkState.Meshed;
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
