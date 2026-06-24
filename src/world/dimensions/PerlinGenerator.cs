using Godot;

namespace TheUniversalEntertainmentSystem.Dimensions;

/// <summary>
/// A dimension generator that uses standard Perlin Noise for rolling hills.
/// Refactored to eliminate duplicate chunk loop math and obsolete custom noise arrays.
/// </summary>
public sealed class PerlinGenerator : BaseNoiseGenerator
{
	protected override void ConfigureNoise(FastNoiseLite noise)
	{
		noise.NoiseType = Godot.FastNoiseLite.NoiseTypeEnum.Perlin;
		noise.Frequency = 0.005f;
		noise.FractalType = Godot.FastNoiseLite.FractalTypeEnum.Fbm;
		noise.FractalOctaves = 4;
		noise.FractalGain = 0.5f;
	}

	protected override int CalculateHeight(float finalNoise)
	{
		return 30 + (int)(finalNoise * 15f);
	}
}
