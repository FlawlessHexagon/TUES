namespace TheUniversalEntertainmentSystem;

/// <summary>
/// Provides read-only access to the voxel registry for dimension generators.
/// </summary>
public interface IRegistryAccess
{
	/// <summary>
	/// Returns the runtime ID for the given namespaced ID.
	/// </summary>
	ushort GetRuntimeId(string namespacedId);
}
