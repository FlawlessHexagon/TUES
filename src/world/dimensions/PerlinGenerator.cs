using System;
using Godot;

namespace TheUniversalEntertainmentSystem.Dimensions;

/// <summary>
/// A dimension generator that uses standard Perlin Noise for rolling hills.
/// </summary>
public sealed class PerlinGenerator : IDimensionGenerator
{
	private ushort _airId;
	private ushort _bedrockId;
	private ushort _stoneId;
	private ushort _dirtId;
	private ushort _grassId;

	private PerlinNoise _noise = null!;

	public void Initialize(int seed)
	{
		_airId = VoxelRegistry.AirId;
		_bedrockId = VoxelRegistry.GetRuntimeId("tues:bedrock");
		_stoneId = VoxelRegistry.GetRuntimeId("tues:stone");
		_dirtId = VoxelRegistry.GetRuntimeId("tues:dirt");
		_grassId = VoxelRegistry.GetRuntimeId("tues:grass");

		_noise = new PerlinNoise(seed);
	}

	public void GenerateChunk(Chunk chunk)
	{
		ushort[] voxels = chunk.Voxels;
		int chunkWorldY = chunk.WorldPosition.Y;
		int chunkWorldX = chunk.WorldPosition.X;
		int chunkWorldZ = chunk.WorldPosition.Z;

		// We precalculate the heightmap for the 16x16 chunk slice
		Span<int> heightMap = stackalloc int[Chunk.SizeX * Chunk.SizeZ];

		int minHeight = int.MaxValue;
		int maxHeight = int.MinValue;

		for (int z = 0; z < Chunk.SizeZ; z++)
		{
			for (int x = 0; x < Chunk.SizeX; x++)
			{
				float globalX = chunkWorldX + x;
				float globalZ = chunkWorldZ + z;

				// Generate FBm-like terrain manually using Perlin
				// Lower frequencies = wider, smoother hills
				float n1 = _noise.GetNoise2D(globalX * 0.003f, globalZ * 0.003f);
				float n2 = _noise.GetNoise2D(globalX * 0.006f, globalZ * 0.006f) * 0.5f;
				float n3 = _noise.GetNoise2D(globalX * 0.012f, globalZ * 0.012f) * 0.25f;

				float finalNoise = (n1 + n2 + n3) / 1.75f; // Normalize to [-1, 1]

				// Base height 30, hills go up to +15 and down to -15
				int h = 30 + (int)(finalNoise * 15f);
				
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

		// Early Out: Entire chunk is below the lowest terrain valley
		if (chunkWorldY + Chunk.SizeY - 1 < minHeight)
		{
			// Fill entirely with Stone
			Array.Fill(voxels, _stoneId, 0, Chunk.Volume);
			chunk.State = ChunkState.Generated;
			return;
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
					else if (globalY < h - 3)
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

// ── Embedded Noise Engine ───────────────────────────────────────────────────

/// <summary>
/// A 100% pure C#, allocation-free implementation of standard 2D Perlin Noise.
/// Embedded directly to ensure the dimension engine is self-contained.
/// </summary>
public sealed class PerlinNoise
{
	// 512-byte permutation table to avoid modulo operations
	private readonly byte[] _p = new byte[512];

	public PerlinNoise(int seed)
	{
		// Initialize standard permutation array
		var random = new Random(seed);
		for (int i = 0; i < 256; i++)
		{
			_p[i] = (byte)i;
		}

		// Shuffle
		for (int i = 0; i < 256; i++)
		{
			int j = random.Next(256);
			(_p[i], _p[j]) = (_p[j], _p[i]);
		}

		// Duplicate to avoid index wrapping calculations in the inner loop
		for (int i = 0; i < 256; i++)
		{
			_p[256 + i] = _p[i];
		}
	}

	/// <summary>
	/// Pure stateless function evaluating 2D Perlin noise.
	/// Output is roughly in the range [-1.0, 1.0].
	/// </summary>
	public float GetNoise2D(float x, float y)
	{
		// Fast floor
		int X = x >= 0 ? (int)x : (int)x - 1;
		int Y = y >= 0 ? (int)y : (int)y - 1;

		// Relative bounds
		x -= X;
		y -= Y;

		// Wrap around 256
		X &= 255;
		Y &= 255;

		// Compute fade curves
		float u = Fade(x);
		float v = Fade(y);

		// Hash coordinates
		int A = _p[X] + Y;
		int B = _p[X + 1] + Y;

		// Blend results
		return Lerp(v,
			Lerp(u, Grad(_p[A], x, y), Grad(_p[B], x - 1, y)),
			Lerp(u, Grad(_p[A + 1], x, y - 1), Grad(_p[B + 1], x - 1, y - 1))
		);
	}

	// ── Math Helpers ────────────────────────────────────────────────────────
	
	// Fade curve: 6t^5 - 15t^4 + 10t^3
	private static float Fade(float t) => t * t * t * (t * (t * 6 - 15) + 10);

	private static float Lerp(float t, float a, float b) => a + t * (b - a);

	private static float Grad(int hash, float x, float y)
	{
		int h = hash & 15;
		float u = h < 8 ? x : y;
		float v = h < 4 ? y : h == 12 || h == 14 ? x : 0f;
		return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
	}
}
