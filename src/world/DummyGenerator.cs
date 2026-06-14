using System;

namespace TheUniversalEntertainmentSystem;

/// <summary>
/// Temporary procedural generation logic for Step 0.3.
/// Fills chunks with a flat plane of stone, dirt, and grass at specific Y levels.
/// Executes entirely decoupled from Godot's node hierarchy to run safely on
/// background worker threads.
/// </summary>
public static class DummyGenerator
{
	/// <summary>
	/// Fills the given chunk with placeholder layered terrain and transitions
	/// its state to <see cref="ChunkState.Generated"/>.
	/// </summary>
	public static void FillChunk(Chunk chunk)
	{
		ushort stoneId = VoxelRegistry.GetRuntimeId("tues:stone");
		ushort dirtId = VoxelRegistry.GetRuntimeId("tues:dirt");
		ushort grassId = VoxelRegistry.GetRuntimeId("tues:grass");

		// Bypass chunk bounds-checking for optimal bulk access
		ushort[] voxels = chunk.Voxels;

		// Convert global chunk Y position to determine layer logic.
		// For the dummy generator, we only generate flat terrain in chunks where Y=0.
		// Chunks above Y=0 are purely Air. Chunks below Y=0 are solid stone.
		int chunkY = chunk.Position.Y;

		if (chunkY < 0)
		{
			// Deep underground
			Array.Fill(voxels, stoneId);
		}
		else if (chunkY == 0)
		{
			// Surface level
			for (int y = 0; y < Chunk.SizeY; y++)
			{
				ushort currentId = VoxelRegistry.AirId;
				if (y <= 5) currentId = stoneId;
				else if (y <= 7) currentId = dirtId;
				else if (y == 8) currentId = grassId;

				if (currentId == VoxelRegistry.AirId)
					continue; // Keep Array.Clear'd zeros

				for (int z = 0; z < Chunk.SizeZ; z++)
				{
					for (int x = 0; x < Chunk.SizeX; x++)
					{
						voxels[Chunk.FlatIndex(x, y, z)] = currentId;
					}
				}
			}
		}

		// Flag the chunk as ready for meshing.
		// Thread-safe via volatile backing field.
		chunk.State = ChunkState.Generated;
	}
}
