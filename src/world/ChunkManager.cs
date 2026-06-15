using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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

	public Vector3 ReferencePosition { get; set; } = Vector3.Zero;

	private static readonly int WorkerThreadsCount = Math.Max(2, System.Environment.ProcessorCount - 2);
	private const int MaxMeshesAttachedPerFrame = 50;

	// ── State ───────────────────────────────────────────────────────────────

	public event Action<Vector3I, ushort>? OnVoxelChanged;

	private readonly ConcurrentDictionary<Vector3I, Chunk> _activeChunks = new();
	private readonly Dictionary<Vector3I, MeshInstance3D> _activeMeshesMap = new();
	private readonly Dictionary<Vector3I, StaticBody3D> _activeCollisions = new();

	private readonly ConcurrentQueue<(Chunk chunk, MeshResult result)> _meshAttachmentQueue = new();

	// ── Worker Threads ──────────────────────────────────────────────────────
	
	private readonly CancellationTokenSource _cts = new();
	private readonly List<Task> _workers = new();
	private readonly object _queueLock = new();

	// ── Queue Caching ───────────────────────────────────────────────────────

	private Vector3I _lastCenterChunk = new(int.MaxValue, int.MaxValue, int.MaxValue);
	private readonly List<(Vector3I pos, int distSq)> _missingChunksQueue = new();

	public bool IsWorldLoaded 
	{
		get 
		{
			if (_activeChunks.IsEmpty) return false; // The world hasn't even started loading yet
			
			lock (_queueLock)
			{
				if (_missingChunksQueue.Count > 0) return false;
			}
			return _activeChunks.Values.All(c => c.State >= ChunkState.Meshed) && _meshAttachmentQueue.IsEmpty;
		}
	}

	public bool IsChunkRadiusLoaded(Vector3 worldPos, int chunkRadius)
	{
		Vector3I centerChunk = GetChunkCoordinate(worldPos);
		int radiusSq = chunkRadius * chunkRadius;

		for (int x = -chunkRadius; x <= chunkRadius; x++)
		{
			for (int z = -chunkRadius; z <= chunkRadius; z++)
			{
				if (x * x + z * z <= radiusSq)
				{
					// Verify all 8 vertical chunks in this column
					for (int y = 0; y < 8; y++)
					{
						Vector3I pos = new Vector3I(centerChunk.X + x, y, centerChunk.Z + z);
						if (!_activeChunks.TryGetValue(pos, out var chunk) || chunk.State < ChunkState.Meshed)
						{
							return false;
						}
					}
				}
			}
		}

		return true;
	}

	public int ActiveChunkCount => _activeChunks.Count;

	// ── Godot Lifecycle ─────────────────────────────────────────────────────

	public override void _Ready()
	{
		Image atlasImage = ChunkMesher.CreateAtlasImage();
		ImageTexture atlasTexture = ImageTexture.CreateFromImage(atlasImage);
		ChunkMesher.Initialize(atlasTexture);
		WorldGenerator.Initialize(GameSettings.WorldSeed, GameSettings.GeneratorType);

		// Spawn background workers
		for (int i = 0; i < WorkerThreadsCount; i++)
		{
			_workers.Add(Task.Run(() => GenerationWorkerLoop(_cts.Token)));
			_workers.Add(Task.Run(() => MeshingWorkerLoop(_cts.Token)));
		}
	}

	public override void _ExitTree()
	{
		_cts.Cancel();
		try { Task.WaitAll(_workers.ToArray()); } catch (AggregateException) {}
		_cts.Dispose();
	}

	public override void _Process(double delta)
	{
		Vector3I centerChunk = GetChunkCoordinate(ReferencePosition);

		ProcessUnloading(centerChunk);
		UpdateGenerationQueue(centerChunk);
		ProcessMeshAttachments();
		ProcessCollisionStreaming(centerChunk);
	}

	// ── Pipeline Stages ─────────────────────────────────────────────────────

	private void ProcessUnloading(Vector3I centerChunk)
	{
		List<Vector3I>? toRemove = null;

		foreach (var kvp in _activeChunks)
		{
			Vector3I pos = kvp.Key;
			Chunk chunk = kvp.Value;

			int dx = Math.Abs(pos.X - centerChunk.X);
			int dz = Math.Abs(pos.Z - centerChunk.Z);
			int dist = Math.Max(dx, dz); // Cylindrical unload distance

			if (dist > GameSettings.RenderDistance + 2) // Unload meshes that are outside the render radius buffer.
			{
				if (chunk.State == ChunkState.Generating || chunk.State == ChunkState.Meshing)
					continue;

				toRemove ??= new List<Vector3I>();
				toRemove.Add(pos);
			}
		}

		if (toRemove is null) return;

		foreach (Vector3I pos in toRemove)
		{
			if (!_activeChunks.TryGetValue(pos, out var chunkToUnload))
				continue;
				
			if (!chunkToUnload.TryClaimDispose())
				continue; // Background thread is currently working on it. Skip unloading for now.

			if (_activeMeshesMap.Remove(pos, out var meshInstance))
			{
				RemoveChild(meshInstance);
				meshInstance.QueueFree();
			}

			if (_activeCollisions.Remove(pos, out var staticBody))
			{
				RemoveChild(staticBody);
				staticBody.QueueFree();
			}

			if (_activeChunks.TryRemove(pos, out var removedChunk))
			{
				removedChunk.Dispose();
			}
		}
	}

	private void UpdateGenerationQueue(Vector3I centerChunk)
	{
		if (centerChunk == _lastCenterChunk)
			return;

		_lastCenterChunk = centerChunk;

		lock (_queueLock)
		{
			_missingChunksQueue.Clear();

			for (int x = -GameSettings.RenderDistance; x <= GameSettings.RenderDistance; x++)
			{
				for (int y = 0; y < 8; y++) // Absolute vertical column (Y=0 to Y=7, 128m high) - Standard Minecraft style
				{
					for (int z = -GameSettings.RenderDistance; z <= GameSettings.RenderDistance; z++)
					{
						Vector3I pos = new Vector3I(centerChunk.X + x, y, centerChunk.Z + z);
						int distSq = x * x + z * z; // Only use 2D distance for cylindrical rendering
						
						if (distSq <= GameSettings.RenderDistance * GameSettings.RenderDistance && !_activeChunks.ContainsKey(pos))
						{
							_missingChunksQueue.Add((pos, distSq));
						}
					}
				}
			}

			_missingChunksQueue.Sort((a, b) => b.distSq.CompareTo(a.distSq));
		}
	}

	// ── Worker Loops ────────────────────────────────────────────────────────

	private void GenerationWorkerLoop(CancellationToken token)
	{
		while (!token.IsCancellationRequested)
		{
			Vector3I? targetPos = null;

			lock (_queueLock)
			{
				if (_missingChunksQueue.Count > 0)
				{
					int lastIndex = _missingChunksQueue.Count - 1;
					targetPos = _missingChunksQueue[lastIndex].pos;
					_missingChunksQueue.RemoveAt(lastIndex);
				}
			}

			if (targetPos.HasValue)
			{
				Vector3I pos = targetPos.Value;

				if (_activeChunks.ContainsKey(pos))
					continue;

				var chunk = new Chunk(pos) { State = ChunkState.Generating };
				if (_activeChunks.TryAdd(pos, chunk))
				{
					try
					{
						WorldGenerator.GenerateChunk(chunk);
					}
					catch (Exception e)
					{
						GD.PushError($"Chunk generation failed at {pos}: {e}");
						_activeChunks.TryRemove(pos, out _);
					}
				}
			}
			else
			{
				// Prevent CPU spin locking when queue is empty
				Thread.Sleep(5);
			}
		}
	}

	private void MeshingWorkerLoop(CancellationToken token)
	{
		Func<int, int, int, ushort> neighbourLookup = (globalX, globalY, globalZ) =>
		{
			Vector3I neighbourPos = new(globalX >> 4, globalY >> 4, globalZ >> 4);
			if (_activeChunks.TryGetValue(neighbourPos, out var nChunk))
				return nChunk.GetVoxel(globalX & 15, globalY & 15, globalZ & 15);
			return VoxelRegistry.AirId;
		};

		Vector3I[] neighbours = {
			Vector3I.Right, Vector3I.Left, 
			Vector3I.Up, Vector3I.Down, 
			Vector3I.Forward, Vector3I.Back
		};

		while (!token.IsCancellationRequested)
		{
			bool worked = false;

			// Snapshot the center chunk for thread-safe distance sorting
			Vector3I currentCenter = _lastCenterChunk;
			
			var candidates = new List<(Chunk chunk, int distSq)>();

			foreach (var kvp in _activeChunks)
			{
				if (kvp.Value.State == ChunkState.Generated)
				{
					int dx = kvp.Key.X - currentCenter.X;
					int dz = kvp.Key.Z - currentCenter.Z;
					candidates.Add((kvp.Value, dx * dx + dz * dz));
				}
			}

			if (candidates.Count > 0)
			{
				// Sort closest to player first
				candidates.Sort((a, b) => a.distSq.CompareTo(b.distSq));

				foreach (var candidate in candidates)
				{
					if (token.IsCancellationRequested) break;

					Chunk chunk = candidate.chunk;
					if (chunk.State != ChunkState.Generated)
						continue;

					bool neighboursReady = true;
					foreach (Vector3I offset in neighbours)
					{
						Vector3I nPos = chunk.Position + offset;
						
						// Skip checks for out-of-bounds absolute vertical chunks
						if (nPos.Y < 0 || nPos.Y >= 8)
							continue;

						if (!_activeChunks.TryGetValue(nPos, out var nChunk) || nChunk.State < ChunkState.Generated)
						{
							neighboursReady = false;
							break;
						}
					}

					if (!neighboursReady)
						continue;

				if (chunk.TryClaimMeshing())
				{
					worked = true;
					try
					{
						MeshResult? result = ChunkMesher.BuildMesh(chunk, neighbourLookup);
						if (result.HasValue)
						{
							_meshAttachmentQueue.Enqueue((chunk, result.Value));
						}
						else
						{
							chunk.State = ChunkState.Meshed;
						}
					}
					catch (Exception e)
					{
						GD.PushError($"Chunk meshing failed at {chunk.Position}: {e}");
						chunk.State = ChunkState.Generated; // Allow retry
					}
				}
			}
			}

			if (!worked)
			{
				Thread.Sleep(10);
			}
		}
	}

	// ── Main Thread Attachment ──────────────────────────────────────────────

	private void ProcessMeshAttachments()
	{
		int attachedThisFrame = 0;
		while (attachedThisFrame < MaxMeshesAttachedPerFrame && _meshAttachmentQueue.TryDequeue(out var item))
		{
			Chunk chunk = item.chunk;
			MeshResult result = item.result;

			if (!_activeChunks.ContainsKey(chunk.Position))
				continue;

			// Safely remove OLD meshes now that the NEW one is ready, eliminating flicker
			if (_activeMeshesMap.TryGetValue(chunk.Position, out var oldMesh))
				oldMesh.QueueFree();
			// Remove old collision to force a recreation in the streaming pass
			if (_activeCollisions.Remove(chunk.Position, out var oldCol))
				oldCol.QueueFree();

			var meshInstance = new MeshInstance3D
			{
				Mesh = result.Mesh,
				Position = chunk.WorldPosition
			};
			AddChild(meshInstance);
			_activeMeshesMap[chunk.Position] = meshInstance;

			chunk.CollisionFaces = result.CollisionFaces;

			chunk.State = ChunkState.Meshed;
			attachedThisFrame++;
		}
	}

	private void ProcessCollisionStreaming(Vector3I centerChunk)
	{
		// 1. Unload distant collisions
		List<Vector3I>? toRemoveCol = null;
		foreach (var kvp in _activeCollisions)
		{
			Vector3I pos = kvp.Key;
			int dx = Math.Abs(pos.X - centerChunk.X);
			int dz = Math.Abs(pos.Z - centerChunk.Z);
			int dist = Math.Max(dx, dz);
			if (dist > GameSettings.SimulationDistance)
			{
				toRemoveCol ??= new List<Vector3I>();
				toRemoveCol.Add(pos);
			}
		}

		if (toRemoveCol != null)
		{
			foreach (var pos in toRemoveCol)
			{
				var body = _activeCollisions[pos];
				RemoveChild(body);
				body.QueueFree();
				_activeCollisions.Remove(pos);
			}
		}

		// 2. Load close collisions
		for (int x = -GameSettings.SimulationDistance; x <= GameSettings.SimulationDistance; x++)
		{
			for (int y = 0; y < 8; y++) // All vertical layers
			{
				for (int z = -GameSettings.SimulationDistance; z <= GameSettings.SimulationDistance; z++)
				{
					Vector3I pos = new Vector3I(centerChunk.X + x, y, centerChunk.Z + z);
					int distSq = x * x + z * z;
					if (distSq <= GameSettings.SimulationDistance * GameSettings.SimulationDistance)
					{
						if (!_activeCollisions.ContainsKey(pos) && _activeChunks.TryGetValue(pos, out var chunk))
						{
							if (chunk.State >= ChunkState.Meshed && chunk.CollisionFaces != null && chunk.CollisionFaces.Length > 0)
							{
								var concaveShape = new ConcavePolygonShape3D();
								concaveShape.SetFaces(chunk.CollisionFaces);
								concaveShape.BackfaceCollision = true;

								var staticBody = new StaticBody3D { Position = chunk.WorldPosition };
								var collisionShape = new CollisionShape3D { Shape = concaveShape };
								staticBody.AddChild(collisionShape);
								AddChild(staticBody);
								_activeCollisions[chunk.Position] = staticBody;
							}
						}
					}
				}
			}
		}
	}

	private static Vector3I GetChunkCoordinate(Vector3 worldPosition)
	{
		return new Vector3I(
			Mathf.FloorToInt(worldPosition.X / Chunk.SizeX),
			Mathf.FloorToInt(worldPosition.Y / Chunk.SizeY),
			Mathf.FloorToInt(worldPosition.Z / Chunk.SizeZ)
		);
	}

	// ── API ─────────────────────────────────────────────────────────────────

	public ushort GetVoxelAtGlobalPos(Vector3I globalPos)
	{
		Vector3I chunkPos = new Vector3I(
			Mathf.FloorToInt((float)globalPos.X / Chunk.SizeX),
			Mathf.FloorToInt((float)globalPos.Y / Chunk.SizeY),
			Mathf.FloorToInt((float)globalPos.Z / Chunk.SizeZ)
		);

		if (_activeChunks.TryGetValue(chunkPos, out var chunk))
		{
			int lx = globalPos.X - (chunkPos.X * Chunk.SizeX);
			int ly = globalPos.Y - (chunkPos.Y * Chunk.SizeY);
			int lz = globalPos.Z - (chunkPos.Z * Chunk.SizeZ);
			return chunk.GetVoxel(lx, ly, lz);
		}
		return 0; // Air
	}

	public void ApplyVoxelDelta(Vector3I globalPos, ushort newVoxelId)
	{
		Vector3I chunkPos = new Vector3I(
			Mathf.FloorToInt((float)globalPos.X / Chunk.SizeX),
			Mathf.FloorToInt((float)globalPos.Y / Chunk.SizeY),
			Mathf.FloorToInt((float)globalPos.Z / Chunk.SizeZ)
		);

		if (!_activeChunks.TryGetValue(chunkPos, out var chunk))
			return;

		int lx = globalPos.X - (chunkPos.X * Chunk.SizeX);
		int ly = globalPos.Y - (chunkPos.Y * Chunk.SizeY);
		int lz = globalPos.Z - (chunkPos.Z * Chunk.SizeZ);

		chunk.SetVoxel(lx, ly, lz, newVoxelId);
		
		FlagChunkForRemeshing(chunkPos);

		if (lx == 0) FlagChunkForRemeshing(chunkPos + Vector3I.Left);
		if (lx == Chunk.SizeX - 1) FlagChunkForRemeshing(chunkPos + Vector3I.Right);
		if (ly == 0) FlagChunkForRemeshing(chunkPos + Vector3I.Down);
		if (ly == Chunk.SizeY - 1) FlagChunkForRemeshing(chunkPos + Vector3I.Up);
		if (lz == 0) FlagChunkForRemeshing(chunkPos + Vector3I.Back);
		if (lz == Chunk.SizeZ - 1) FlagChunkForRemeshing(chunkPos + Vector3I.Forward);

		// Safely emit to main thread
		Callable.From(() => OnVoxelChanged?.Invoke(globalPos, newVoxelId)).CallDeferred();
	}

	private void FlagChunkForRemeshing(Vector3I chunkPos)
	{
		if (_activeChunks.TryGetValue(chunkPos, out var chunk))
		{
			// Do NOT delete the old mesh here! Let it render until the background thread finishes
			// building the new mesh to prevent visual flashing.
			chunk.State = ChunkState.Generated; // Pushes it back into meshing queue
		}
	}
}
