namespace TheUniversalEntertainmentSystem;

/// <summary>
/// Registers the core voxel types that ship with the base game. Separated from
/// <see cref="VoxelRegistry"/> to keep the registry generic — it has no knowledge
/// of specific voxel types. Future modules will follow this same pattern: each
/// module calls its own registration method during startup.
/// </summary>
public static class VoxelRegistration
{
	/// <summary>
	/// Registers the five core voxel types required for basic terrain generation.
	/// Must be called before <see cref="VoxelRegistry.FreezeRegistry"/>.
	///
	/// Registration order matters: tues:air is registered first, guaranteeing it
	/// receives runtime ID 0 (the permanent Air ID reserved by the registry).
	/// </summary>
	public static void RegisterCoreTypes()
	{
		// ── Air ────────────────────────────────────────────────────────────
		// Registered first to guarantee runtime ID 0. Air produces no geometry,
		// is non-solid, and is transparent to light.
		VoxelRegistry.Register(new VoxelType(
			namespacedId:    "tues:air",
			displayName:     "Air",
			isSolid:         false,
			occludesNeighbours: false,
			isTransparent:   true,
			meshMode:        VoxelMeshMode.None));

		// ── Grass ──────────────────────────────────────────────────────────
		// Top face: grass texture (index 0). Sides: grass-over-dirt (index 1).
		// Bottom: dirt (index 2). Distinct indices for the three visible regions.
		VoxelRegistry.Register(new VoxelType(
			namespacedId:    "tues:grass",
			displayName:     "Grass",
			isSolid:         true,
			occludesNeighbours: true,
			isTransparent:   false,
			meshMode:        VoxelMeshMode.Cube,
			textureTopIndex:    0,
			textureBottomIndex: 2,
			textureSideIndex:   1));

		// ── Dirt ───────────────────────────────────────────────────────────
		// Uniform dirt texture on all faces.
		VoxelRegistry.Register(new VoxelType(
			namespacedId:    "tues:dirt",
			displayName:     "Dirt",
			isSolid:         true,
			occludesNeighbours: true,
			isTransparent:   false,
			meshMode:        VoxelMeshMode.Cube,
			textureTopIndex:    2,
			textureBottomIndex: 2,
			textureSideIndex:   2));

		// ── Stone ──────────────────────────────────────────────────────────
		// Uniform stone texture on all faces.
		VoxelRegistry.Register(new VoxelType(
			namespacedId:    "tues:stone",
			displayName:     "Stone",
			isSolid:         true,
			occludesNeighbours: true,
			isTransparent:   false,
			meshMode:        VoxelMeshMode.Cube,
			textureTopIndex:    3,
			textureBottomIndex: 3,
			textureSideIndex:   3));

		// ── Bedrock ────────────────────────────────────────────────────────
		// Uniform bedrock texture on all faces.
		VoxelRegistry.Register(new VoxelType(
			namespacedId:    "tues:bedrock",
			displayName:     "Bedrock",
			isSolid:         true,
			occludesNeighbours: true,
			isTransparent:   false,
			meshMode:        VoxelMeshMode.Cube,
			textureTopIndex:    4,
			textureBottomIndex: 4,
			textureSideIndex:   4));
	}
}
