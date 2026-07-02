using System;
using Godot;


namespace TheUniversalEntertainmentSystem;
using TheUniversalEntertainmentSystem.API;

/// <summary>
/// A global registry and entry point for world generation.
/// Defers actual generation logic to the currently active IDimensionGenerator.
/// </summary>
public static class WorldGenerator
{
	private static IDimensionGenerator? _activeGenerator;
	private static int _currentSeed = -1;
	private static string _currentGeneratorType = string.Empty;

	public static void Initialize(int seed, string generatorType = "superflat")
	{
		generatorType = generatorType.ToLower().Trim();

		if (_activeGenerator == null || _currentSeed != seed || _currentGeneratorType != generatorType)
		{
			// Try to load via TuesEngineLoader (Step 2.0 package pipeline)
			_activeGenerator = TuesEngineLoader.LoadPackageForWorld(generatorType, seed);

			if (_activeGenerator == null)
			{
				Logger.Error($"WorldGenerator: Could not load dimension package '{generatorType}'. The engine requires a valid .tuesengine package.");
				throw new Exception($"Missing dimension package: {generatorType}");
			}

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
