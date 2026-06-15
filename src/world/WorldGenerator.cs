using System;
using Godot;
using TheUniversalEntertainmentSystem.Dimensions;

namespace TheUniversalEntertainmentSystem;

/// <summary>
/// A global registry and entry point for world generation.
/// Defers actual generation logic to the currently active IDimensionGenerator.
/// </summary>
public static class WorldGenerator
{
	private static IDimensionGenerator? _activeGenerator;
	private static int _currentSeed = -1;
	private static string _currentGeneratorType = string.Empty;

	/// <summary>
	/// Must be called once on the main thread during world load to initialize
	/// the shared thread-safe generator.
	/// </summary>
	public static void Initialize(int seed, string generatorType = "superflat")
	{
		generatorType = generatorType.ToLower().Trim();

		if (_activeGenerator == null || _currentSeed != seed || _currentGeneratorType != generatorType)
		{
			_activeGenerator = generatorType switch
			{
				"perlin" => new PerlinGenerator(),
				"simplex" => new SimplexGenerator(),
				_ => new SuperflatGenerator()
			};

			_activeGenerator.Initialize(seed);
			
			_currentSeed = seed;
			_currentGeneratorType = generatorType;
		}
	}

	/// <summary>
	/// Defers chunk generation to the active dimension engine.
	/// Executes safely on background worker threads.
	/// </summary>
	public static void GenerateChunk(Chunk chunk)
	{
		if (_activeGenerator == null)
			throw new InvalidOperationException("WorldGenerator must be Initialized before generating chunks.");

		_activeGenerator.GenerateChunk(chunk);
	}
}
