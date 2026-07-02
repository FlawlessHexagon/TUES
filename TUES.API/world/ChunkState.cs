namespace TheUniversalEntertainmentSystem.API;

/// <summary>
/// Tracks the lifecycle of a <see cref="Chunk"/>. Systems check this state before
/// operating on a chunk — the mesher refuses to mesh a chunk still in
/// <see cref="Generating"/> state, and the manager will not re-generate a
/// <see cref="Meshed"/> chunk.
/// </summary>
public enum ChunkState : byte
{
	/// <summary>
	/// Default / initial state. No voxel data has been generated.
	/// </summary>
	Unloaded = 0,

	/// <summary>
	/// Voxel data is being filled on a worker thread. Do not read or mesh.
	/// </summary>
	Generating = 1,

	/// <summary>
	/// Voxel data is complete. Ready for meshing.
	/// </summary>
	Generated = 2,

	/// <summary>
	/// The mesh is actively being generated on a worker thread.
	/// </summary>
	Meshing = 3,

	/// <summary>
	/// Mesh has been built and attached to the scene tree.
	/// </summary>
	Meshed = 4,

	/// <summary>
	/// Chunk is marked for unload and cannot be claimed by worker threads.
	/// </summary>
	Disposed = 5,
}
