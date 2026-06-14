using System;
using System.Collections.Generic;

namespace TheUniversalEntertainmentSystem;

/// <summary>
/// The central registry of all voxel type definitions. Manages the bidirectional
/// mapping between namespaced string IDs (stable, canonical identity) and compact
/// runtime ushort IDs (used in chunk data arrays for memory efficiency).
///
/// The registry is populated during startup and becomes read-only after
/// <see cref="FreezeRegistry"/> is called. All post-finalization access is safe for
/// concurrent reads from worker threads without synchronisation, because the
/// underlying data is immutable once finalized.
///
/// Runtime ID 0 is permanently reserved for Air ("tues:air"). This invariant is
/// guaranteed by registering Air first during core type registration.
/// </summary>
public static class VoxelRegistry
{
	/// <summary>
	/// The runtime ID permanently reserved for Air. Always 0.
	/// </summary>
	public const ushort AirId = 0;

	// Namespaced string → runtime ushort. Used for name-based lookups.
	private static readonly Dictionary<string, ushort> _nameToId = new();

	// Runtime ushort → VoxelType. List indexing is O(1), equivalent to array
	// access, and avoids the hashing overhead of a Dictionary<ushort, VoxelType>.
	// Trimmed to exact capacity on finalization.
	private static readonly List<VoxelType> _types = new();

	private static bool _finalized;

	/// <summary>
	/// Whether the registry has been finalized. After finalization, no new types
	/// can be registered, but all lookup methods remain fully functional.
	/// </summary>
	public static bool IsFinalized => _finalized;

	/// <summary>
	/// The number of registered voxel types.
	/// </summary>
	public static int Count => _types.Count;

	// ── High-Performance Flat Lookup Tables ────────────────────────────────
	// Constructed during FreezeRegistry. These arrays provide direct memory access
	// to voxel properties, bypassing the object pointer dereferencing of GetType().
	// This prevents massive L1/L2 cache misses during chunk meshing.
	
	public static bool[] OccludesTable { get; private set; } = Array.Empty<bool>();
	public static bool[] TransparentTable { get; private set; } = Array.Empty<bool>();
	public static VoxelMeshMode[] MeshModeTable { get; private set; } = Array.Empty<VoxelMeshMode>();
	public static int[] TextureTopTable { get; private set; } = Array.Empty<int>();
	public static int[] TextureBottomTable { get; private set; } = Array.Empty<int>();
	public static int[] TextureSideTable { get; private set; } = Array.Empty<int>();

	/// <summary>
	/// Registers a voxel type and assigns it the next available runtime ID.
	/// Runtime IDs are assigned sequentially starting from 0.
	/// </summary>
	/// <param name="type">The voxel type definition to register. Must not be null.</param>
	/// <returns>The assigned runtime ID (ushort).</returns>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the registry has been finalized, or if the maximum number of
	/// types (65,536) has been reached.
	/// </exception>
	/// <exception cref="ArgumentNullException">Thrown if type is null.</exception>
	/// <exception cref="ArgumentException">
	/// Thrown if a type with the same NamespacedId is already registered.
	/// </exception>
	public static ushort Register(VoxelType type)
	{
		if (_finalized)
			throw new InvalidOperationException(
				$"Cannot register voxel type '{type?.NamespacedId ?? "null"}': " +
				"the registry has been finalized. " +
				"All registrations must occur before FreezeRegistry() is called.");

		ArgumentNullException.ThrowIfNull(type);

		if (_types.Count > ushort.MaxValue)
			throw new InvalidOperationException(
				$"Cannot register voxel type '{type.NamespacedId}': the maximum number " +
				$"of types ({ushort.MaxValue + 1}) has been reached.");

		if (_nameToId.ContainsKey(type.NamespacedId))
			throw new ArgumentException(
				$"A voxel type with namespaced ID '{type.NamespacedId}' is already registered. " +
				"Duplicate registrations are not allowed — each namespaced ID must be unique.",
				nameof(type));

		ushort runtimeId = (ushort)_types.Count;
		_types.Add(type);
		_nameToId[type.NamespacedId] = runtimeId;

		return runtimeId;
	}

	/// <summary>
	/// Locks the registry, preventing any further registrations. Call this once
	/// during startup after all types (core and module) have been registered,
	/// and before any worker threads begin accessing the registry.
	///
	/// This method is idempotent — calling it multiple times is safe.
	/// </summary>
	public static void FreezeRegistry()
	{
		if (_finalized)
			return;

		_finalized = true;
		_types.TrimExcess();

		// Construct flat arrays
		int count = _types.Count;
		OccludesTable = new bool[count];
		TransparentTable = new bool[count];
		MeshModeTable = new VoxelMeshMode[count];
		TextureTopTable = new int[count];
		TextureBottomTable = new int[count];
		TextureSideTable = new int[count];

		for (int i = 0; i < count; i++)
		{
			VoxelType t = _types[i];
			OccludesTable[i] = t.OccludesNeighbours;
			TransparentTable[i] = t.IsTransparent;
			MeshModeTable[i] = t.MeshMode;
			TextureTopTable[i] = t.TextureTopIndex;
			TextureBottomTable[i] = t.TextureBottomIndex;
			TextureSideTable[i] = t.TextureSideIndex;
		}
	}

	/// <summary>
	/// Returns the <see cref="VoxelType"/> for the given runtime ID,
	/// or null if the ID is not assigned.
	/// </summary>
	public static VoxelType? GetType(ushort runtimeId)
	{
		return runtimeId < _types.Count ? _types[runtimeId] : null;
	}

	/// <summary>
	/// Returns the <see cref="VoxelType"/> for the given namespaced ID,
	/// or null if the ID is not registered.
	/// </summary>
	public static VoxelType? GetType(string namespacedId)
	{
		if (_nameToId.TryGetValue(namespacedId, out ushort id))
			return _types[id];
		return null;
	}

	/// <summary>
	/// Returns the runtime ID for the given namespaced ID.
	/// </summary>
	/// <exception cref="KeyNotFoundException">
	/// Thrown if the namespaced ID is not registered.
	/// </exception>
	public static ushort GetRuntimeId(string namespacedId)
	{
		if (_nameToId.TryGetValue(namespacedId, out ushort id))
			return id;

		throw new KeyNotFoundException(
			$"No voxel type registered with namespaced ID '{namespacedId}'. " +
			"Ensure the type is registered before attempting to look it up.");
	}

	/// <summary>
	/// Clears all registered types and resets the registry to its initial state.
	/// Intended for testing only — in production, the registry is built once
	/// during startup and never reset.
	/// </summary>
	public static void Reset()
	{
		_nameToId.Clear();
		_types.Clear();
		_finalized = false;
	}
}
