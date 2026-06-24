using Godot;

namespace TheUniversalEntertainmentSystem.Dimensions;

/// <summary>
/// A dimension generator that uses standard Simplex Noise for extreme, amplified mountain terrain.
/// Refactored to use the unified base class while maintaining specialized clamping bounds.
/// </summary>
public sealed class ExtremeGenerator : BaseNoiseGenerator
{
	protected override void ConfigureNoise(FastNoiseLite noise)
	{
		noise.NoiseType = Godot.FastNoiseLite.NoiseTypeEnum.Simplex;
		noise.Frequency = 0.002f; // lower frequency for massive mountains
		noise.FractalType = Godot.FastNoiseLite.FractalTypeEnum.Fbm;
		noise.FractalOctaves = 5;
		noise.FractalGain = 0.5f;
	}

	protected override int CalculateHeight(float finalNoise)
	{
		// Base height 60, amplify hills up to +50 and down to -50
		int h = 60 + (int)(finalNoise * 50f);
		
		// Clamp to ensure we never drop below Y=1 (saving Y=0 for bedrock)
		if (h < 1) h = 1;
		// Clamp to ensure we never exceed Y=126
		if (h > 126) h = 126;
		
		return h;
	}

	protected override int GetDirtDepth() => 5;
}
