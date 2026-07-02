namespace TheUniversalEntertainmentSystem.API;

/// <summary>
/// Determines how a voxel type generates its visual geometry during chunk meshing.
/// </summary>
public enum VoxelMeshMode : byte
{
	/// <summary>
	/// No geometry is produced. Used for Air and invisible logic blocks.
	/// </summary>
	None = 0,

	/// <summary>
	/// Standard six-faced cube. The vast majority of terrain voxels use this mode.
	/// Each face's UVs are mapped to a region of the shared texture atlas.
	/// </summary>
	Cube = 1,

	/// <summary>
	/// A pre-built mesh is stamped at the voxel's grid position.
	/// The VoxelType must provide a valid CustomMeshPath when using this mode.
	/// Custom meshes do not participate in face culling — they always render fully.
	/// </summary>
	Custom = 2,
}
