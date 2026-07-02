using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace TheUniversalEntertainmentSystem;
using TheUniversalEntertainmentSystem.API;

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
	private readonly object _centerLock = new();
	private readonly List<(Vector3I pos, int distSq)> _missingChunksQueue = new();

	// ── Meshing Queue ───────────────────────────────────────────────────────

	private readonly ConcurrentQueue<Chunk> _meshReadyQueue = new();

	// ── Background Rebuild Signal ───────────────────────────────────────────

	private readonly ManualResetEventSlim _rebuildSignal = new(false);

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
					for (int y = 0; y < 8; y++) // TODO: Phase 2 — replace with engine-provided MinY/MaxY bounds
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
		WorldGenerator.Initialize(GameSettings.WorldSeed, GameSettings.GeneratorType);

		// Spawn background workers
		for (int i = 0; i < WorkerThreadsCount; i++)
		{
			_workers.Add(Task.Factory.StartNew(() => GenerationWorkerLoop(_cts.Token), _cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default));
			_workers.Add(Task.Factory.StartNew(() => MeshingWorkerLoop(_cts.Token), _cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default));
		}
		_workers.Add(Task.Factory.StartNew(() => QueueRebuildLoop(_cts.Token), _cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default));
	}

	public override void _ExitTree()
	{
		_cts.Cancel();
		_rebuildSignal.Set(); // Unblock the rebuild loop so it can observe cancellation
		try { Task.WaitAll(_workers.ToArray()); } catch (AggregateException) {}
		_cts.Dispose();
		_rebuildSignal.Dispose();
	}

	public override void _Process(double delta)
	{
		Vector3I centerChunk = GetChunkCoordinate(ReferencePosition);

		ProcessUnloading(centerChunk);
		UpdateGenerationQueue(centerChunk);
		ProcessMeshAttachments();
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

			// Stop-gap: retain dirty (player-modified) chunks in memory until
			// the Phase 2 region-file persistence system is implemented.
			if (chunkToUnload.IsDirty)
				continue;

			if (!chunkToUnload.TryClaimDispose())
				continue; // Background thread is currently working on it. Skip unloading for now.

			if (_activeMeshesMap.Remove(pos, out var meshInstance))
			{
				RemoveChild(meshInstance);
				meshInstance.Mesh?.Dispose();
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
		lock (_centerLock)
		{
			// The current generation logic is cylindrical (ignores Y).
			// Do not trigger costly queue rebuilds for purely vertical movement.
			if (centerChunk.X == _lastCenterChunk.X && centerChunk.Z == _lastCenterChunk.Z) return;
			_lastCenterChunk = centerChunk;
		}
		_rebuildSignal.Set();
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
						_meshReadyQueue.Enqueue(chunk);
					}
					catch (Exception e)
					{
						Logger.Error($"Chunk generation failed at {pos}: {e}");
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
			if (!_meshReadyQueue.TryDequeue(out var chunk))
			{
				Thread.Sleep(10);
				continue;
			}

			// Chunk may have been disposed or claimed by another worker since enqueue
			if (chunk.State != ChunkState.Generated)
				continue;

			// Check that all face-adjacent neighbours are generated
			bool neighboursReady = true;
			foreach (Vector3I offset in neighbours)
			{
				Vector3I nPos = chunk.Position + offset;

				// Skip checks for out-of-bounds absolute vertical chunks
				if (nPos.Y < 0 || nPos.Y >= 8) // TODO: Phase 2 — replace with engine-provided MinY/MaxY bounds
					continue;

				if (!_activeChunks.TryGetValue(nPos, out var nChunk) || nChunk.State < ChunkState.Generated)
				{
					neighboursReady = false;
					break;
				}
			}

			if (!neighboursReady)
			{
				// Re-enqueue for later attempt; throttle to avoid hot-spinning on edge chunks
				_meshReadyQueue.Enqueue(chunk);
				Thread.Sleep(1);
				continue;
			}

			if (chunk.TryClaimMeshing())
			{
				try
				{
					MeshResult? result = ChunkMesher.BuildMesh(chunk, neighbourLookup);
					if (result != null)
					{
						_meshAttachmentQueue.Enqueue((chunk, result));
					}
					else
					{
						chunk.State = ChunkState.Meshed;
					}
				}
				catch (Exception e)
				{
					Logger.Error($"Chunk meshing failed at {chunk.Position}: {e}");
					chunk.State = ChunkState.Generated; // Allow retry
				}
			}
		}
	}

	private void QueueRebuildLoop(CancellationToken token)
	{
		try
		{
			while (!token.IsCancellationRequested)
			{
				_rebuildSignal.Wait(token);
				_rebuildSignal.Reset();

				Vector3I center;
				lock (_centerLock) { center = _lastCenterChunk; }

				var localQueue = new List<(Vector3I pos, int distSq)>();

				for (int x = -GameSettings.RenderDistance; x <= GameSettings.RenderDistance; x++)
				{
					for (int y = 0; y < 8; y++) // TODO: Phase 2 — replace with engine-provided MinY/MaxY bounds
					{
						for (int z = -GameSettings.RenderDistance; z <= GameSettings.RenderDistance; z++)
						{
							Vector3I pos = new Vector3I(center.X + x, y, center.Z + z);
							int distSq = x * x + z * z;

							if (distSq <= GameSettings.RenderDistance * GameSettings.RenderDistance && !_activeChunks.ContainsKey(pos))
							{
								localQueue.Add((pos, distSq));
							}
						}
					}
				}

				localQueue.Sort((a, b) => b.distSq.CompareTo(a.distSq));

				lock (_queueLock)
				{
					_missingChunksQueue.Clear();
					_missingChunksQueue.AddRange(localQueue);
				}
			}
		}
		catch (OperationCanceledException) { }
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
			{
				oldMesh.Mesh?.Dispose();
				oldMesh.QueueFree();
			}
			// Remove old collision to force a recreation in the streaming pass
			if (_activeCollisions.Remove(chunk.Position, out var oldCol))
			{
				oldCol.QueueFree();
			}

			var arrayMesh = new ArrayMesh();

			if (result.OpaqueVerts != null)
			{
				var arrays = new Godot.Collections.Array();
				arrays.Resize((int)Mesh.ArrayType.Max);
				arrays[(int)Mesh.ArrayType.Vertex] = result.OpaqueVerts!;
				arrays[(int)Mesh.ArrayType.Normal] = result.OpaqueNormals!;
				arrays[(int)Mesh.ArrayType.TexUV] = result.OpaqueUVs!;
				arrays[(int)Mesh.ArrayType.TexUV2] = result.OpaqueUV2s!;
				arrays[(int)Mesh.ArrayType.Index] = result.OpaqueIndices!;
				arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
				arrayMesh.SurfaceSetMaterial(0, ChunkMesher.OpaqueMaterial);
				arrays.Dispose();
			}

			if (result.TransVerts != null)
			{
				var arrays = new Godot.Collections.Array();
				arrays.Resize((int)Mesh.ArrayType.Max);
				arrays[(int)Mesh.ArrayType.Vertex] = result.TransVerts!;
				arrays[(int)Mesh.ArrayType.Normal] = result.TransNormals!;
				arrays[(int)Mesh.ArrayType.TexUV] = result.TransUVs!;
				arrays[(int)Mesh.ArrayType.TexUV2] = result.TransUV2s!;
				arrays[(int)Mesh.ArrayType.Index] = result.TransIndices!;
				arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
				arrayMesh.SurfaceSetMaterial(result.OpaqueVerts != null ? 1 : 0, ChunkMesher.TransparentMaterial);
				arrays.Dispose();
			}

			var meshInstance = new MeshInstance3D
			{
				Mesh = arrayMesh,
				Position = chunk.WorldPosition
			};
			AddChild(meshInstance);
			_activeMeshesMap[chunk.Position] = meshInstance;

			chunk.CollisionShape?.Dispose();
			ConcavePolygonShape3D? collisionShape = null;
			if (result.CollisionFaces != null)
			{
				collisionShape = new ConcavePolygonShape3D();
				collisionShape.SetFaces(result.CollisionFaces);
				collisionShape.BackfaceCollision = true;
				
				var staticBody = new StaticBody3D { Position = chunk.WorldPosition };
				var colShape3D = new CollisionShape3D { Shape = collisionShape };
				staticBody.AddChild(colShape3D);
				AddChild(staticBody);
				_activeCollisions[chunk.Position] = staticBody;
			}
			chunk.CollisionShape = collisionShape;

			// If another block was broken while we were meshing, FlagChunkForRemeshing changed
			// the state to Generated. We must NOT overwrite it to Meshed, otherwise the block
			// break will never be visually updated.
			if (chunk.State != ChunkState.Generated)
			{
				chunk.State = ChunkState.Meshed;
			}
			attachedThisFrame++;
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
			chunk.State = ChunkState.Generated;
			_meshReadyQueue.Enqueue(chunk);
		}
	}
}
