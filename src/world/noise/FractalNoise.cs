namespace TheUniversalEntertainmentSystem.Noise;

/// <summary>
/// A wrapper around pure PerlinNoise that applies Fractal Brownian Motion (FBm).
/// Stacks multiple octaves of noise to create natural, mountainous terrain.
/// </summary>
public sealed class FractalNoise
{
	private readonly PerlinNoise _perlin;

	public int Octaves { get; set; } = 4;
	
	/// <summary>
	/// Controls the scale of the terrain. Lower values mean wider hills.
	/// </summary>
	public float Frequency { get; set; } = 0.015f;
	
	/// <summary>
	/// The multiplier applied to frequency per octave.
	/// </summary>
	public float Lacunarity { get; set; } = 2.0f;
	
	/// <summary>
	/// The multiplier applied to amplitude per octave.
	/// </summary>
	public float Gain { get; set; } = 0.5f;

	public FractalNoise(int seed)
	{
		_perlin = new PerlinNoise(seed);
	}

	/// <summary>
	/// Evaluates FBm noise at the given coordinates.
	/// Normalises the output to strictly fall within the range [-1.0, 1.0].
	/// </summary>
	public float GetNoise2D(float x, float y)
	{
		float sum = 0f;
		float amp = 1f;
		float freq = Frequency;
		float maxVal = 0f;

		for (int i = 0; i < Octaves; i++)
		{
			sum += _perlin.GetNoise2D(x * freq, y * freq) * amp;
			maxVal += amp;
			
			amp *= Gain;
			freq *= Lacunarity;
		}

		return sum / maxVal;
	}
}
