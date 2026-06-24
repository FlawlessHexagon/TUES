using System;
using Godot;

namespace TheUniversalEntertainmentSystem.Dimensions;

/// <summary>
/// A unified base class for all dimension generators that utilize FastNoiseLite 
/// to generate terrain. This prevents duplicate logic across Simplex, Perlin, 
/// and Extreme generators.
/// </summary>
public abstract class BaseNoiseGenerator : IDimensionGenerator
{
	protected ushort _airId;
	protected ushort _bedrockId;
	protected ushort _stoneId;
	protected ushort _dirtId;
	protected ushort _grassId;

	protected Godot.FastNoiseLite _noise = new();

	public virtual void Initialize(int seed, IRegistryAccess registry)
	{
		_airId = VoxelRegistry.AirId;
		_bedrockId = VoxelRegistry.GetRuntimeId("tues:bedrock");
		_stoneId = VoxelRegistry.GetRuntimeId("tues:stone");
		_dirtId = VoxelRegistry.GetRuntimeId("tues:dirt");
		_grassId = VoxelRegistry.GetRuntimeId("tues:grass");

		_noise.Seed = seed;
		ConfigureNoise(_noise);
	}

	protected abstract void ConfigureNoise(FastNoiseLite noise);

	protected abstract int CalculateHeight(float finalNoise);

	protected virtual int GetDirtDepth() => 3;

	public void GenerateChunk(Chunk chunk)
	{
		ushort[] voxels = chunk.Voxels;
		int chunkWorldX = chunk.WorldPosition.X;
		int chunkWorldY = chunk.WorldPosition.Y;
		int chunkWorldZ = chunk.WorldPosition.Z;

		Span<int> heightMap = stackalloc int[Chunk.SizeX * Chunk.SizeZ];
		int minHeight = int.MaxValue;
		int maxHeight = int.MinValue;

		for (int z = 0; z < Chunk.SizeZ; z++)
		{
			for (int x = 0; x < Chunk.SizeX; x++)
			{
				float globalX = chunkWorldX + x;
				float globalZ = chunkWorldZ + z;

				float finalNoise = _noise.GetNoise2D(globalX, globalZ);
				int h = CalculateHeight(finalNoise);
				
				heightMap[x + z * Chunk.SizeX] = h;
				
				if (h < minHeight) minHeight = h;
				if (h > maxHeight) maxHeight = h;
			}
		}

		// Early Out: Entire chunk is above the highest terrain peak
		if (chunkWorldY > maxHeight)
		{
			chunk.State = ChunkState.Generated;
			return;
		}

		int dirtDepth = GetDirtDepth();

		// Early Out: Entire chunk is below the lowest terrain valley
		if (chunkWorldY + Chunk.SizeY - 1 < minHeight - dirtDepth)
		{
			// Fix: We must not overwrite Bedrock if chunkWorldY == 0
			if (chunkWorldY > 0)
			{
				Array.Fill(voxels, _stoneId, 0, Chunk.Volume);
				chunk.State = ChunkState.Generated;
				return;
			}
		}

		// Chunk intersects the terrain boundaries
		for (int y = 0; y < Chunk.SizeY; y++)
		{
			int globalY = chunkWorldY + y;

			for (int z = 0; z < Chunk.SizeZ; z++)
			{
				for (int x = 0; x < Chunk.SizeX; x++)
				{
					int h = heightMap[x + z * Chunk.SizeX];

					ushort layerId = _airId;

					if (globalY == 0)
					{
						layerId = _bedrockId;
					}
					else if (globalY < h - dirtDepth)
					{
						layerId = _stoneId;
					}
					else if (globalY < h)
					{
						layerId = _dirtId;
					}
					else if (globalY == h)
					{
						layerId = _grassId;
					}

					if (layerId != _airId)
					{
						voxels[Chunk.FlatIndex(x, y, z)] = layerId;
					}
				}
			}
		}

		chunk.State = ChunkState.Generated;
	}
}
