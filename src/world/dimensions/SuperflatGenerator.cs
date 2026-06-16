using System;
using Godot;

namespace TheUniversalEntertainmentSystem.Dimensions;

/// <summary>
/// A highly optimized dimension generator that produces a flat world.
/// Used to isolate chunk meshing and threading performance without the overhead 
/// of noise evaluation.
/// 
/// Layers:
/// Y = 0: Bedrock
/// Y = 1 to 26: Stone
/// Y = 27 to 29: Dirt
/// Y = 30: Grass
/// </summary>
public sealed class SuperflatGenerator : IDimensionGenerator
{
	private ushort _airId;
	private ushort _bedrockId;
	private ushort _stoneId;
	private ushort _dirtId;
	private ushort _grassId;

	public void Initialize(int seed, IRegistryAccess registry)
	{
		_airId = VoxelRegistry.AirId;
		_bedrockId = VoxelRegistry.GetRuntimeId("tues:bedrock");
		_stoneId = VoxelRegistry.GetRuntimeId("tues:stone");
		_dirtId = VoxelRegistry.GetRuntimeId("tues:dirt");
		_grassId = VoxelRegistry.GetRuntimeId("tues:grass");
	}

	public void GenerateChunk(Chunk chunk)
	{
		ushort[] voxels = chunk.Voxels;
		int chunkWorldY = chunk.WorldPosition.Y;

		// Early Out: Entire chunk is above the terrain limit (Sky)
		if (chunkWorldY > 30)
		{
			// Voxels are pre-zeroed to Air by the Chunk constructor.
			chunk.State = ChunkState.Generated;
			return;
		}

		// Early Out: Entire chunk is deep underground
		if (chunkWorldY + Chunk.SizeY - 1 < 0)
		{
			// Fill entirely with Stone (bounds restricted due to ArrayPool buckets)
			Array.Fill(voxels, _stoneId, 0, Chunk.Volume);
			chunk.State = ChunkState.Generated;
			return;
		}

		// Chunk intersects the terrain boundaries
		for (int y = 0; y < Chunk.SizeY; y++)
		{
			int globalY = chunkWorldY + y;

			// Skip air layers
			if (globalY > 30)
				continue;

			ushort layerId = _airId;

			if (globalY == 0)
			{
				layerId = _bedrockId;
			}
			else if (globalY <= 26)
			{
				layerId = _stoneId;
			}
			else if (globalY <= 29)
			{
				layerId = _dirtId;
			}
			else if (globalY == 30)
			{
				layerId = _grassId;
			}

			// We skip writing Air entirely
			if (layerId != _airId)
			{
				// Hardware-accelerated memory block fill for the entire Y-slice
				Array.Fill(voxels, layerId, y * Chunk.LayerVolume, Chunk.LayerVolume);
			}
		}

		chunk.State = ChunkState.Generated;
	}
}
