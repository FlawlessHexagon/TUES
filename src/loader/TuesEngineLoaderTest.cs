using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using Godot;

namespace TheUniversalEntertainmentSystem;

[DimensionEngine("test:engine")]
public class DummyDimensionGenerator : IDimensionGenerator
{
	public void Initialize(int seed, IRegistryAccess registry)
	{
		GD.Print("DummyDimensionGenerator Initialized with seed " + seed);
	}

	public void GenerateChunk(Chunk chunk)
	{
		// Dummy implementation
	}
}

public static class TuesEngineLoaderTest
{
	public static void RunTest()
	{
		GD.Print("=== Running TuesEngineLoaderTest ===");

		// 1. Create a dummy .zip archive in memory
		using var ms = new MemoryStream();
		using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
		{
			var manifest = new TuesEngineManifest
			{
				Id = "test:engine",
				Version = "1.0.0",
				EntryDll = "generator.dll",
				Dependencies = Array.Empty<string>()
			};
			var manifestEntry = archive.CreateEntry("manifest.json");
			using (var stream = manifestEntry.Open())
			{
				JsonSerializer.Serialize(stream, manifest);
			}

			var blocks = new[]
			{
				new BlockDefinitionDto
				{
					NamespacedId = "test:dummy_block",
					DisplayName = "Dummy Block",
					IsSolid = true,
					OccludesNeighbours = true,
					IsTransparent = false,
					MeshMode = "Cube"
				}
			};
			var blocksEntry = archive.CreateEntry("assets/blocks.json");
			using (var stream = blocksEntry.Open())
			{
				JsonSerializer.Serialize(stream, blocks);
			}

			string currentAssemblyPath = Assembly.GetExecutingAssembly().Location;
			if (string.IsNullOrEmpty(currentAssemblyPath) || !File.Exists(currentAssemblyPath))
			{
				currentAssemblyPath = ProjectSettings.GlobalizePath("res://.godot/mono/temp/bin/Debug/The Universal Entertainment System.dll");
			}

			if (!string.IsNullOrEmpty(currentAssemblyPath) && File.Exists(currentAssemblyPath))
			{
				var dllEntry = archive.CreateEntry("generator.dll");
				using var dllStream = dllEntry.Open();
				using var fileStream = File.OpenRead(currentAssemblyPath);
				fileStream.CopyTo(dllStream);
			}
			else
			{
				GD.PushError("TuesEngineLoaderTest: Could not find current assembly path to copy.");
				return;
			}
		}

		ms.Position = 0;

		string tempFilePath = Path.GetTempFileName() + ".zip";
		File.WriteAllBytes(tempFilePath, ms.ToArray());

		try
		{
			var generator = TuesEngineLoader.LoadPackage(tempFilePath, 12345);

			if (generator == null)
			{
				GD.PushError("TuesEngineLoaderTest: Failed to load dummy generator.");
				return;
			}

			if (VoxelRegistry.GetType("test:dummy_block") == null)
			{
				GD.PushError("TuesEngineLoaderTest: 'test:dummy_block' was not registered.");
				return;
			}

			GD.Print("TuesEngineLoaderTest: SUCCESS!");
		}
		finally
		{
			File.Delete(tempFilePath);
		}
		
		// Fake dependency failure test
		using var failMs = new MemoryStream();
		using (var failArchive = new ZipArchive(failMs, ZipArchiveMode.Create, true))
		{
			var manifest = new TuesEngineManifest
			{
				Id = "test:engine_fail",
				Version = "1.0.0",
				EntryDll = "generator.dll",
				Dependencies = new[] { "fake:dependency" }
			};
			var manifestEntry = failArchive.CreateEntry("manifest.json");
			using (var stream = manifestEntry.Open())
			{
				JsonSerializer.Serialize(stream, manifest);
			}
		}
		string tempFailPath = Path.GetTempFileName() + ".zip";
		File.WriteAllBytes(tempFailPath, failMs.ToArray());
		try
		{
			var failGenerator = TuesEngineLoader.LoadPackage(tempFailPath, 12345);
			if (failGenerator == null)
			{
				GD.Print("TuesEngineLoaderTest: Fake dependency test SUCCESS (it correctly failed).");
			}
			else
			{
				GD.PushError("TuesEngineLoaderTest: Fake dependency test FAILED (it loaded anyway).");
			}
		}
		finally
		{
			File.Delete(tempFailPath);
		}
	}
}
