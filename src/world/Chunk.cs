using Godot;
using System;
using System.Buffers;
using System.Threading;

namespace TheUniversalEntertainmentSystem;

/// <summary>
/// A fixed-size 3D container of voxel data. Stores <see cref="Volume"/> runtime IDs
/// (ushort) in a flat array for cache-friendly access. This is pure data — no Godot
/// nodes, no rendering, no scene tree integration.
///
/// The chunk does not know about its neighbours. Out-of-bounds reads return Air (0),
/// which produces correct behaviour during meshing (exposed faces are rendered).
/// Neighbour resolution is the ChunkManager's responsibility (Step 0.3).
/// </summary>
public sealed class Chunk : IDisposable
{
	// ── Dimensions ──────────────────────────────────────────────────────────
	// Defined as const so other systems (mesher, manager, generator) can
	// reference them without a Chunk instance.

	public const int SizeX = 16;
	public const int SizeY = 16;
	public const int SizeZ = 16;

	/// <summary>
	/// Total number of voxels per chunk: SizeX × SizeY × SizeZ = 4,096.
	/// Memory footprint of the voxel array: Volume × sizeof(ushort) = 8,192 bytes.
	/// </summary>
	public const int Volume = SizeX * SizeY * SizeZ;

	/// <summary>
	/// Precomputed chunk size as a Vector3I for world-position conversion.
	/// </summary>
	public static readonly Vector3I Size = new(SizeX, SizeY, SizeZ);

	// ── Voxel data ──────────────────────────────────────────────────────────
	//
	// Index formula: index = x + SizeX * (z + SizeZ * y)
	// Expanded:      index = x + 16z + 256y
	//
	// Memory layout (Y-major ordering):
	//   X is contiguous (stride 1)  — optimal for the innermost meshing loop
	//   Z is the middle axis (stride 16)
	//   Y is the outermost axis (stride 256) — each Y layer is a 256-entry block
	//
	// This matches the standard meshing iteration pattern:
	//   for y in 0..15:     (sweep horizontal layers bottom-to-top)
	//     for z in 0..15:   (within each layer, sweep rows)
	//       for x in 0..15: (within each row, contiguous memory access)
	//
	// Sequential access during meshing maximises CPU cache hit rates.
	// Flat array is non-negotiable — 3D/jagged arrays scatter memory and destroy
	// cache locality at the scale of hundreds of chunks per frame.

	private ushort[]? _voxels;

	/// <summary>
	/// Direct access to the underlying voxel data array. Intended for bulk
	/// operations by the world generator (which writes the entire array) and
	/// the mesher (which reads it sequentially).
	///
	/// Callers using this property bypass bounds checking and dirty-flag tracking.
	/// Use <see cref="GetVoxel"/> / <see cref="SetVoxel"/> for safe, single-voxel
	/// access that respects chunk boundaries and modification tracking.
	/// </summary>
	public ushort[] Voxels => _voxels ?? throw new ObjectDisposedException(nameof(Chunk));

	// ── Metadata ────────────────────────────────────────────────────────────

	/// <summary>
	/// This chunk's location in chunk-grid coordinates (not world coordinates).
	/// World position is derived: <see cref="WorldPosition"/>.
	/// </summary>
	public Vector3I Position { get; }

	/// <summary>
	/// The chunk's origin in world coordinates.
	/// Computed as <c>Position * Size</c> (component-wise multiplication).
	/// </summary>
	public Vector3I WorldPosition => Position * Size;

	/// <summary>
	/// Whether any voxel in this chunk has been modified via <see cref="SetVoxel"/>
	/// since creation. Only dirty chunks need to be saved to disk (Phase 2).
	/// Defaults to <c>false</c>. Direct writes through <see cref="Voxels"/> do
	/// not set this flag — this is intentional, as the world generator fills
	/// chunks via direct array access during generation.
	/// </summary>
	public bool IsDirty { get; private set; }

	private byte _state;

	/// <summary>
	/// The chunk's current lifecycle state. External systems (manager, mesher,
	/// generator) read and write this to coordinate operations.
	/// Uses volatile read/write to ensure thread-safe visibility across 
	/// background generation and main thread polling.
	/// </summary>
	public ChunkState State
	{
		get => (ChunkState)Volatile.Read(ref _state);
		set => Volatile.Write(ref _state, (byte)value);
	}

	// ── Constructor ─────────────────────────────────────────────────────────

	/// <summary>
	/// Creates a new chunk at the given chunk-grid position. The voxel array is
	/// allocated immediately and default-initialised to all zeros (Air).
	/// </summary>
	/// <param name="position">Chunk-grid coordinates (not world coordinates).</param>
	public Chunk(Vector3I position)
	{
		Position = position;
		_voxels = ArrayPool<ushort>.Shared.Rent(Volume);
		// ArrayPool does not guarantee a zeroed array; clear it manually.
		Array.Clear(_voxels, 0, Volume);
		State = ChunkState.Unloaded;
		IsDirty = false;
	}

	/// <summary>
	/// Returns the chunk's underlying array to the shared memory pool.
	/// Must be called when the chunk is permanently unloaded.
	/// </summary>
	public void Dispose()
	{
		if (_voxels is not null)
		{
			ArrayPool<ushort>.Shared.Return(_voxels);
			_voxels = null;
		}
	}

	// ── Voxel access ────────────────────────────────────────────────────────

	/// <summary>
	/// Returns the runtime voxel ID at local coordinates (x, y, z).
	/// Out-of-bounds coordinates return 0 (Air) without throwing — this is
	/// essential during meshing, where neighbour checks at chunk edges produce
	/// coordinates of -1 or <see cref="SizeX"/>.
	/// </summary>
	public ushort GetVoxel(int x, int y, int z)
	{
		if (!IsInBounds(x, y, z) || _voxels is null)
			return 0;

		return _voxels[FlatIndex(x, y, z)];
	}

	/// <summary>
	/// Sets the voxel at local coordinates (x, y, z) to the given runtime ID
	/// and marks the chunk as dirty. Out-of-bounds coordinates are silently
	/// ignored — no exception, no side effect.
	/// </summary>
	public void SetVoxel(int x, int y, int z, ushort id)
	{
		if (!IsInBounds(x, y, z) || _voxels is null)
			return;

		_voxels[FlatIndex(x, y, z)] = id;
		IsDirty = true;
	}

	// ── Utilities ───────────────────────────────────────────────────────────

	/// <summary>
	/// Returns <c>true</c> if the given local coordinates are within chunk bounds.
	/// Uses unsigned comparison to collapse both the negative and overflow checks
	/// into a single comparison per axis.
	/// </summary>
	public static bool IsInBounds(int x, int y, int z)
	{
		return (uint)x < SizeX && (uint)y < SizeY && (uint)z < SizeZ;
	}

	/// <summary>
	/// Computes the flat array index for the given local coordinates.
	/// No bounds checking — caller must ensure coordinates are valid.
	/// Formula: <c>x + SizeX * (z + SizeZ * y)</c>
	/// </summary>
	public static int FlatIndex(int x, int y, int z)
	{
		return x + SizeX * (z + SizeZ * y);
	}
}
