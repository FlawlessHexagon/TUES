namespace TheUniversalEntertainmentSystem;

/// <summary>
/// A lightweight wrapper around the static VoxelRegistry that implements IRegistryAccess.
/// This allows the global registry to remain static for performance while satisfying
/// the dependency injection contract for sandboxed dimension engines.
/// </summary>
public class VoxelRegistryWrapper : IRegistryAccess
{
	public ushort GetRuntimeId(string namespacedId)
	{
		return VoxelRegistry.GetRuntimeId(namespacedId);
	}
}
