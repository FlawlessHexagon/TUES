using System;
using Godot;
using TheUniversalEntertainmentSystem.Noise;

namespace TheUniversalEntertainmentSystem;

/// <summary>
/// A pure, stateless utility that populates a chunk's voxel data array with
/// deterministic, natural-looking terrain using pure C# FBm Perlin noise.
/// Designed for zero-allocation performance on worker threads.
/// </summary>
public static class WorldGenerator
{
	private static FractalNoise? _noise;
	private static int _currentSeed = -1;

	private static ushort _airId = 0;
	private static ushort _bedrockId;
	private static ushort _stoneId;
	private static ushort _dirtId;
	private static ushort _grassId;

	public const int MinHeight = 30;
	public const int MaxHeight = 100;

	/// <summary>
	/// Must be called once on the main thread during world load to initialize
	/// the shared thread-safe noise generator and cache registry IDs.
	/// </summary>
	public static void Initialize(int seed)
	{
		if (_noise == null || _currentSeed != seed)
		{
			_noise = new FractalNoise(seed);
			_currentSeed = seed;
			
			_airId = VoxelRegistry.AirId;
			_bedrockId = VoxelRegistry.GetRuntimeId("tues:bedrock");
			_stoneId = VoxelRegistry.GetRuntimeId("tues:stone");
			_dirtId = VoxelRegistry.GetRuntimeId("tues:dirt");
			_grassId = VoxelRegistry.GetRuntimeId("tues:grass");
		}
	}

	/// <summary>
	/// Generates the terrain for the chunk and promotes its state to Generated.
	/// Executes safely on background worker threads.
	/// </summary>
	public static void GenerateChunk(Chunk chunk)
	{
		// 1. Validate Initialisation
		if (_noise == null)
			throw new InvalidOperationException("WorldGenerator must be Initialized before generating chunks.");

		// Bypasses properties for direct flat array mutation
		ushort[] voxels = chunk.Voxels;
		int chunkWorldY = chunk.WorldPosition.Y;
		
		// 2. Early Out Optimization (Sky)
		if (chunkWorldY > MaxHeight)
		{
			// The chunk array is already cleared from the ArrayPool.
			chunk.State = ChunkState.Generated;
			return;
		}

		// 3. Early Out Optimization (Underground)
		if (chunkWorldY + Chunk.SizeY - 1 < MinHeight - 3)
		{
			if (chunk.Position.Y == 0)
			{
				// Edge case: Fill bedrock at global y=0, stone above
				for (int y = 0; y < Chunk.SizeY; y++)
				{
					ushort id = (chunkWorldY + y == 0) ? _bedrockId : _stoneId;
					for (int z = 0; z < Chunk.SizeZ; z++)
					{
						for (int x = 0; x < Chunk.SizeX; x++)
						{
							voxels[Chunk.FlatIndex(x, y, z)] = id;
						}
					}
				}
			}
			else
			{
				// Deep underground chunk. Zero noise evaluations required.
				Array.Fill(voxels, _stoneId);
			}

			chunk.State = ChunkState.Generated;
			return;
		}

		// 4. Heightmap Evaluation
		int chunkWorldX = chunk.WorldPosition.X;
		int chunkWorldZ = chunk.WorldPosition.Z;

		// We compute the heightmap for the 16x16 column footprint once.
		// `stackalloc` avoids heap allocations entirely.
		Span<int> heightMap = stackalloc int[Chunk.SizeX * Chunk.SizeZ];

		for (int z = 0; z < Chunk.SizeZ; z++)
		{
			for (int x = 0; x < Chunk.SizeX; x++)
			{
				// Evaluate purely in C#. Zero interop marshaling.
				float noiseVal = _noise.GetNoise2D(chunkWorldX + x, chunkWorldZ + z);
				
				// Scale [-1, 1] up to our terrain bounds
				float normalized = (noiseVal + 1f) * 0.5f;
				int terrainY = (int)(MinHeight + normalized * (MaxHeight - MinHeight));
				
				heightMap[x + z * Chunk.SizeX] = terrainY;
			}
		}

		// 5. Vertical Population
		// Y-major loop for optimal cache access
		for (int y = 0; y < Chunk.SizeY; y++)
		{
			int globalY = chunkWorldY + y;
			
			// Skip processing layers strictly above the maximum possible terrain.
			if (globalY > MaxHeight)
				continue;

			for (int z = 0; z < Chunk.SizeZ; z++)
			{
				for (int x = 0; x < Chunk.SizeX; x++)
				{
					int terrainY = heightMap[x + z * Chunk.SizeX];

					ushort id = _airId;

					if (globalY == 0)
					{
						id = _bedrockId;
					}
					else if (globalY < terrainY - 3)
					{
						id = _stoneId;
					}
					else if (globalY < terrainY)
					{
						id = _dirtId;
					}
					else if (globalY == terrainY)
					{
						id = _grassId;
					}

					// We skip writing Air entirely, because the ArrayPool buffer
					// was manually zeroed in the Chunk constructor.
					if (id != _airId)
					{
						voxels[Chunk.FlatIndex(x, y, z)] = id;
					}
				}
			}
		}

		// Thread-safe state advancement
		chunk.State = ChunkState.Generated;
	}
}
