using System;

namespace TheUniversalEntertainmentSystem.Noise;

/// <summary>
/// A 100% pure C#, allocation-free implementation of standard 2D Perlin Noise.
/// By completely bypassing Godot's P/Invoke marshaling overhead, this generator
/// can safely be evaluated millions of times per second across worker threads.
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
			// Swap
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
