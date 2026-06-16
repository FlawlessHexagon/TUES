namespace TheUniversalEntertainmentSystem;

/// <summary>
/// Defines the contract for dimension-specific terrain generation engines.
/// Any class implementing this interface can be hot-swapped to fundamentally
/// change how chunks are populated with voxels (e.g., Default World, Superflat, Void).
/// </summary>
public interface IDimensionGenerator
{
	/// <summary>
	/// Called once when the world is loaded or the dimension is initialized.
	/// Used to cache voxel IDs, initialize noise, and set up thread-safe read-only data.
	/// </summary>
	/// <param name="seed">The global world seed.</param>
	void Initialize(int seed, IRegistryAccess registry);

	/// <summary>
	/// Populates the given chunk's voxel data array.
	/// MUST be thread-safe as it will be executed simultaneously across multiple 
	/// background workers.
	/// </summary>
	/// <param name="chunk">The chunk object whose voxel array needs populating.</param>
	void GenerateChunk(Chunk chunk);
}
