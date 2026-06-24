using Godot;

namespace TheUniversalEntertainmentSystem.Dimensions;

/// <summary>
/// A dimension generator that uses standard Simplex Noise for jagged, natural terrain.
/// Refactored to eliminate duplicate chunk loop math and obsolete custom noise arrays.
/// </summary>
public sealed class SimplexGenerator : BaseNoiseGenerator
{
	protected override void ConfigureNoise(FastNoiseLite noise)
	{
		noise.NoiseType = Godot.FastNoiseLite.NoiseTypeEnum.Simplex;
		noise.Frequency = 0.004f;
		noise.FractalType = Godot.FastNoiseLite.FractalTypeEnum.Fbm;
		noise.FractalOctaves = 3;
		noise.FractalGain = 0.5f;
	}

	protected override int CalculateHeight(float finalNoise)
	{
		return 40 + (int)(finalNoise * 20f);
	}
}
