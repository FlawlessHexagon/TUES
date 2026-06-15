using System;
using Godot;

namespace TheUniversalEntertainmentSystem.Dimensions;

/// <summary>
/// A dimension generator that uses standard Simplex Noise for jagged, natural terrain.
/// </summary>
public sealed class SimplexGenerator : IDimensionGenerator
{
	private ushort _airId;
	private ushort _bedrockId;
	private ushort _stoneId;
	private ushort _dirtId;
	private ushort _grassId;

	private SimplexNoise _noise = null!;

	public void Initialize(int seed)
	{
		_airId = VoxelRegistry.AirId;
		_bedrockId = VoxelRegistry.GetRuntimeId("tues:bedrock");
		_stoneId = VoxelRegistry.GetRuntimeId("tues:stone");
		_dirtId = VoxelRegistry.GetRuntimeId("tues:dirt");
		_grassId = VoxelRegistry.GetRuntimeId("tues:grass");

		_noise = new SimplexNoise(seed);
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

				// Generate FBm-like terrain manually using Simplex
				// Lower frequencies = wider, smoother hills
				float n1 = _noise.GetNoise2D(globalX * 0.004f, globalZ * 0.004f);
				float n2 = _noise.GetNoise2D(globalX * 0.008f, globalZ * 0.008f) * 0.5f;
				float n3 = _noise.GetNoise2D(globalX * 0.016f, globalZ * 0.016f) * 0.25f;

				float finalNoise = (n1 + n2 + n3) / 1.75f; // Normalize to [-1, 1]

				// Base height 40, hills go up to +20 and down to -20
				int h = 40 + (int)(finalNoise * 20f);
				
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
/// A pure C# implementation of 2D Simplex Noise.
/// Embedded directly to ensure the dimension engine is self-contained.
/// </summary>
public sealed class SimplexNoise
{
	private readonly byte[] _p = new byte[512];
	private static readonly float F2 = 0.5f * (MathF.Sqrt(3.0f) - 1.0f);
	private static readonly float G2 = (3.0f - MathF.Sqrt(3.0f)) / 6.0f;

	public SimplexNoise(int seed)
	{
		var random = new Random(seed);
		for (int i = 0; i < 256; i++) _p[i] = (byte)i;
		for (int i = 0; i < 256; i++)
		{
			int j = random.Next(256);
			(_p[i], _p[j]) = (_p[j], _p[i]);
		}
		for (int i = 0; i < 256; i++) _p[256 + i] = _p[i];
	}

	public float GetNoise2D(float x, float y)
	{
		float s = (x + y) * F2;
		int i = FastFloor(x + s);
		int j = FastFloor(y + s);
		
		float t = (i + j) * G2;
		float X0 = i - t;
		float Y0 = j - t;
		float x0 = x - X0;
		float y0 = y - Y0;

		int i1, j1;
		if (x0 > y0) { i1 = 1; j1 = 0; }
		else { i1 = 0; j1 = 1; }

		float x1 = x0 - i1 + G2;
		float y1 = y0 - j1 + G2;
		float x2 = x0 - 1.0f + 2.0f * G2;
		float y2 = y0 - 1.0f + 2.0f * G2;

		int ii = i & 255;
		int jj = j & 255;
		int gi0 = _p[ii + _p[jj]] % 12;
		int gi1 = _p[ii + i1 + _p[jj + j1]] % 12;
		int gi2 = _p[ii + 1 + _p[jj + 1]] % 12;

		float n0, n1, n2;

		float t0 = 0.5f - x0 * x0 - y0 * y0;
		if (t0 < 0) n0 = 0.0f;
		else
		{
			t0 *= t0;
			n0 = t0 * t0 * Grad(gi0, x0, y0);
		}

		float t1 = 0.5f - x1 * x1 - y1 * y1;
		if (t1 < 0) n1 = 0.0f;
		else
		{
			t1 *= t1;
			n1 = t1 * t1 * Grad(gi1, x1, y1);
		}

		float t2 = 0.5f - x2 * x2 - y2 * y2;
		if (t2 < 0) n2 = 0.0f;
		else
		{
			t2 *= t2;
			n2 = t2 * t2 * Grad(gi2, x2, y2);
		}

		return 70.0f * (n0 + n1 + n2);
	}

	private static int FastFloor(float x) => x >= 0 ? (int)x : (int)x - 1;

	private static float Grad(int hash, float x, float y)
	{
		int h = hash & 7;
		float u = h < 4 ? x : y;
		float v = h < 4 ? y : x;
		return ((h & 1) != 0 ? -u : u) + ((h & 2) != 0 ? -2.0f * v : 2.0f * v);
	}
}
