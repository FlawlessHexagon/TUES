using System;

namespace TheUniversalEntertainmentSystem.API;

/// <summary>
/// The immutable data definition of a voxel type. Describes a category of voxel — not
/// a voxel instance. All voxels of a given type share a single VoxelType definition,
/// referenced by runtime ID from chunk data arrays.
///
/// A voxel in TUES is not just a cube. It is a 3D model occupying one grid cell.
/// The MeshMode field determines whether the type renders as a standard cube, a
/// custom mesh, or produces no geometry at all.
/// </summary>
public sealed class VoxelType
{
	/// <summary>
	/// The canonical, collision-proof identity of this voxel type.
	/// Format: "namespace:name" (e.g. "tues:stone", "forestcraft:maple_log").
	/// Both parts must be non-empty and contain only lowercase alphanumeric characters
	/// and underscores.
	/// </summary>
	public string NamespacedId { get; }

	/// <summary>
	/// Human-readable display name (e.g. "Stone", "Maple Log").
	/// </summary>
	public string DisplayName { get; }

	/// <summary>
	/// Whether this voxel blocks physical movement and provides collision geometry.
	/// </summary>
	public bool IsSolid { get; }

	/// <summary>
	/// Whether this voxel physically occludes the faces of adjacent voxels
	/// during chunk meshing. True ONLY for full 1x1x1 opaque cubes (Stone, Dirt). 
	/// False for Glass, Air, AND Custom non-cubic meshes (Slopes, Stairs).
	/// </summary>
	public bool OccludesNeighbours { get; }

	/// <summary>
	/// Whether this voxel allows light and visibility to pass through it.
	/// Transparent voxels (glass, water, leaves) are collected into a separate mesh
	/// surface from opaque voxels for correct render ordering.
	/// </summary>
	public bool IsTransparent { get; }

	/// <summary>
	/// How this voxel type generates its visual geometry during chunk meshing.
	/// </summary>
	public VoxelMeshMode MeshMode { get; }

	/// <summary>
	/// For Custom mesh mode: the resource path to the pre-built mesh
	/// (e.g. "res://assets/meshes/slope.tres"). Null for Cube and None modes.
	/// </summary>
	public string? CustomMeshPath { get; }

	// ──────────────────────────────────────────────────────────────────────────
	// Texture Atlas Indices
	//
	// Design decision: texture atlas, not individual materials.
	//
	// A texture atlas assigns a single material to ALL cube-mode voxels in a chunk,
	// meaning the entire opaque chunk mesh renders in one draw call. Individual
	// materials per voxel type would produce one draw call per type per chunk —
	// at 5 types and 500 active chunks, that's 2,500 draw calls for terrain alone.
	// The atlas collapses this to 500.
	//
	// Three indices cover the common case: top face (e.g. grass green), bottom face
	// (e.g. dirt), and all four side faces (e.g. grass-over-dirt). Per-face overrides
	// for all six faces can be added later if specific types require it.
	//
	// These indices are provisional until the texture atlas is built in Step 0.2.
	// For None-mode voxels (Air), these values are unused.
	// ──────────────────────────────────────────────────────────────────────────

	/// <summary>
	/// Index into the texture atlas for the top (+Y) face.
	/// </summary>
	public int TextureTopIndex { get; }

	/// <summary>
	/// Index into the texture atlas for the bottom (-Y) face.
	/// </summary>
	public int TextureBottomIndex { get; }

	/// <summary>
	/// Index into the texture atlas for all four side faces (±X, ±Z).
	/// </summary>
	public int TextureSideIndex { get; }

	/// <summary>
	/// Creates a new voxel type definition with full validation.
	/// </summary>
	/// <exception cref="ArgumentException">
	/// Thrown if NamespacedId or DisplayName are invalid, or if MeshMode is Custom
	/// but no CustomMeshPath is provided.
	/// </exception>
	public VoxelType(
		string namespacedId,
		string displayName,
		bool isSolid,
		bool occludesNeighbours,
		bool isTransparent,
		VoxelMeshMode meshMode,
		string? customMeshPath = null,
		int textureTopIndex = 0,
		int textureBottomIndex = 0,
		int textureSideIndex = 0)
	{
		ValidateNamespacedId(namespacedId);

		if (string.IsNullOrWhiteSpace(displayName))
			throw new ArgumentException("DisplayName must not be null or empty.", nameof(displayName));

		if (meshMode == VoxelMeshMode.Custom && string.IsNullOrWhiteSpace(customMeshPath))
			throw new ArgumentException(
				$"CustomMeshPath is required when MeshMode is Custom (type: '{namespacedId}').",
				nameof(customMeshPath));

		if (textureTopIndex < 0)
			throw new ArgumentOutOfRangeException(nameof(textureTopIndex), "Texture index must be non-negative.");
		if (textureBottomIndex < 0)
			throw new ArgumentOutOfRangeException(nameof(textureBottomIndex), "Texture index must be non-negative.");
		if (textureSideIndex < 0)
			throw new ArgumentOutOfRangeException(nameof(textureSideIndex), "Texture index must be non-negative.");

		NamespacedId = namespacedId;
		DisplayName = displayName;
		IsSolid = isSolid;
		OccludesNeighbours = occludesNeighbours;
		IsTransparent = isTransparent;
		MeshMode = meshMode;
		CustomMeshPath = customMeshPath;
		TextureTopIndex = textureTopIndex;
		TextureBottomIndex = textureBottomIndex;
		TextureSideIndex = textureSideIndex;
	}

	/// <summary>
	/// Validates that a namespaced ID conforms to the "namespace:name" format.
	/// Both parts must be non-empty and contain only lowercase letters, digits,
	/// and underscores.
	/// </summary>
	private static void ValidateNamespacedId(string namespacedId)
	{
		if (string.IsNullOrEmpty(namespacedId))
			throw new ArgumentException(
				"NamespacedId must not be null or empty.", nameof(namespacedId));

		int colonIndex = namespacedId.IndexOf(':');

		if (colonIndex < 0)
			throw new ArgumentException(
				$"NamespacedId '{namespacedId}' must contain a ':' separator " +
				"(expected format: 'namespace:name').",
				nameof(namespacedId));

		if (colonIndex == 0)
			throw new ArgumentException(
				$"NamespacedId '{namespacedId}' has an empty namespace " +
				"(expected format: 'namespace:name').",
				nameof(namespacedId));

		if (colonIndex == namespacedId.Length - 1)
			throw new ArgumentException(
				$"NamespacedId '{namespacedId}' has an empty name " +
				"(expected format: 'namespace:name').",
				nameof(namespacedId));

		// Reject multiple colons.
		if (namespacedId.IndexOf(':', colonIndex + 1) >= 0)
			throw new ArgumentException(
				$"NamespacedId '{namespacedId}' contains multiple ':' separators " +
				"(expected format: 'namespace:name').",
				nameof(namespacedId));

		ReadOnlySpan<char> namespacePart = namespacedId.AsSpan(0, colonIndex);
		ReadOnlySpan<char> namePart = namespacedId.AsSpan(colonIndex + 1);

		ValidateIdSegment(namespacePart, "namespace", namespacedId);
		ValidateIdSegment(namePart, "name", namespacedId);
	}

	/// <summary>
	/// Validates that a single segment (namespace or name) of a namespaced ID contains
	/// only lowercase letters (a-z), digits (0-9), and underscores.
	/// </summary>
	private static void ValidateIdSegment(
		ReadOnlySpan<char> segment, string segmentLabel, string fullId)
	{
		foreach (char c in segment)
		{
			if (c is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '_'))
			{
				throw new ArgumentException(
					$"NamespacedId '{fullId}' contains invalid character '{c}' in {segmentLabel}. " +
					"Only lowercase letters (a-z), digits (0-9), and underscores (_) are allowed.",
					nameof(fullId));
			}
		}
	}

	public override string ToString() => NamespacedId;
}
