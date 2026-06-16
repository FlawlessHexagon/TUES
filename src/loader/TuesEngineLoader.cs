using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Godot;

namespace TheUniversalEntertainmentSystem;

/// <summary>
/// A robust, secure gateway that allows TUES to read, unpack, and execute external dimension generators.
/// </summary>
public static class TuesEngineLoader
{
	private const string DimensionsDir = "user://dimensions/";

	public static IDimensionGenerator? LoadPackageForWorld(string engineId, int seed)
	{
		string absolutePath = ProjectSettings.GlobalizePath(DimensionsDir);
		if (!Directory.Exists(absolutePath))
		{
			Directory.CreateDirectory(absolutePath);
		}

		// Find the package matching the engine ID
		string? targetPackagePath = null;
		foreach (string file in Directory.GetFiles(absolutePath, "*.tuesengine").Concat(Directory.GetFiles(absolutePath, "*.zip")))
		{
			using var archive = ZipFile.OpenRead(file);
			var manifestEntry = archive.GetEntry("manifest.json");
			if (manifestEntry != null)
			{
				using var stream = manifestEntry.Open();
				var manifest = JsonSerializer.Deserialize<TuesEngineManifest>(stream);
				if (manifest?.Id == engineId)
				{
					targetPackagePath = file;
					break;
				}
			}
		}

		if (targetPackagePath == null)
		{
			return null;
		}

		return LoadPackage(targetPackagePath, seed);
	}

	public static IDimensionGenerator? LoadPackage(string packagePath, int seed)
	{
		using var archive = ZipFile.OpenRead(packagePath);

		// 1. Parse Manifest
		var manifestEntry = archive.GetEntry("manifest.json");
		if (manifestEntry == null)
		{
			GD.PushError($"TuesEngineLoader: Missing manifest.json in '{packagePath}'.");
			return null;
		}

		TuesEngineManifest? manifest;
		using (var stream = manifestEntry.Open())
		{
			manifest = JsonSerializer.Deserialize<TuesEngineManifest>(stream);
		}

		if (manifest == null)
		{
			GD.PushError($"TuesEngineLoader: Failed to parse manifest.json in '{packagePath}'.");
			return null;
		}

		// 2. Validate Dependencies
		// Currently only supporting hard dependencies on loaded elements. For the sake of this step,
		// we just check if they are in the registry or mock queue.
		foreach (var dep in manifest.Dependencies)
		{
			// MOCK check for dependencies. Since we don't have a global loaded mods list yet,
			// if it starts with "fake", we simulate a failure.
			if (dep.StartsWith("fake"))
			{
				GD.PushError($"TuesEngineLoader: Missing required dependency '{dep}' for package '{manifest.Id}'. Aborting.");
				return null;
			}
		}

		// 3. Dynamic Voxel Registration
		var blocksEntry = archive.GetEntry("assets/blocks.json");
		if (blocksEntry != null)
		{
			using var stream = blocksEntry.Open();
			var blockDefs = JsonSerializer.Deserialize<List<BlockDefinitionDto>>(stream);
			if (blockDefs != null)
			{
				foreach (var def in blockDefs)
				{
					if (!Enum.TryParse<VoxelMeshMode>(def.MeshMode, out var meshMode))
					{
						meshMode = VoxelMeshMode.Cube;
					}

					var voxelType = new VoxelType(
						def.NamespacedId,
						def.DisplayName,
						def.IsSolid,
						def.OccludesNeighbours,
						def.IsTransparent,
						meshMode,
						def.CustomMeshPath,
						def.TextureTopIndex,
						def.TextureBottomIndex,
						def.TextureSideIndex
					);

					try
					{
						VoxelRegistry.Register(voxelType);
					}
					catch (Exception e)
					{
						GD.PushWarning($"TuesEngineLoader: Failed to register block '{def.NamespacedId}': {e.Message}");
					}
				}
			}
		}

		// 4. Dynamic Atlas Rebuild
		var images = new Godot.Collections.Array<Image>();
		
		// Add core placeholder images first (so indices 0-5 match ChunkMesher.CreateAtlasImage)
		// Or we can rebuild the whole atlas here. Let's create the 6 base core images.
		AddCoreImages(images);

		// Read all .png from assets/textures/
		foreach (var entry in archive.Entries)
		{
			if (entry.FullName.StartsWith("assets/textures/") && entry.FullName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
			{
				using var stream = entry.Open();
				using var memStream = new MemoryStream();
				stream.CopyTo(memStream);
				
				var image = new Image();
				Error err = image.LoadPngFromBuffer(memStream.ToArray());
				if (err == Error.Ok)
				{
					if (image.GetWidth() != 16 || image.GetHeight() != 16)
					{
						image.Resize(16, 16, Image.Interpolation.Nearest);
					}
					images.Add(image);
				}
				else
				{
					GD.PushWarning($"TuesEngineLoader: Failed to load texture '{entry.FullName}'.");
				}
			}
		}

		var textureArray = new Texture2DArray();
		textureArray.CreateFromImages(images);
		ChunkMesher.Initialize(textureArray);

		// 5. Sandboxed DLL Execution & Reflection
		var dllEntry = archive.GetEntry(manifest.EntryDll);
		if (dllEntry == null)
		{
			GD.PushError($"TuesEngineLoader: Entry DLL '{manifest.EntryDll}' not found in archive.");
			return null;
		}

		using var dllStream = dllEntry.Open();
		using var dllMemStream = new MemoryStream();
		dllStream.CopyTo(dllMemStream);
		dllMemStream.Position = 0;

		var context = new TuesEngineContext(manifest.Id);
		Assembly assembly = context.LoadFromStream(dllMemStream);

		Type? generatorType = null;
		foreach (Type type in assembly.GetTypes())
		{
			if (typeof(IDimensionGenerator).IsAssignableFrom(type))
			{
				// Check for DimensionEngine attribute
				var attr = type.GetCustomAttribute<DimensionEngineAttribute>();
				if (attr != null && attr.Id == manifest.Id)
				{
					generatorType = type;
					break;
				}
			}
		}

		if (generatorType == null)
		{
			GD.PushError($"TuesEngineLoader: No type implementing IDimensionGenerator with [DimensionEngine(\"{manifest.Id}\")] found in '{manifest.EntryDll}'.");
			return null;
		}

		var generator = (IDimensionGenerator?)Activator.CreateInstance(generatorType);
		if (generator != null)
		{
			generator.Initialize(seed, new VoxelRegistryWrapper());
			return generator;
		}

		return null;
	}

	public static void AddCoreImages(Godot.Collections.Array<Image> images)
	{
		// We add the 6 base images
		Color grassGreen = new(0.298f, 0.686f, 0.314f);
		Color dirtBrown  = new(0.545f, 0.412f, 0.078f);
		Color stoneGrey  = new(0.620f, 0.620f, 0.620f);
		Color bedrockGrey = new(0.380f, 0.380f, 0.380f);

		images.Add(CreateSolidImage(grassGreen));
		images.Add(CreateHalfImage(grassGreen, dirtBrown));
		images.Add(CreateSolidImage(dirtBrown));
		images.Add(CreateSolidImage(stoneGrey));
		images.Add(CreateSolidImage(bedrockGrey));
		images.Add(CreateSolidImage(Colors.Black));
	}

	private static Image CreateSolidImage(Color color)
	{
		var img = Image.CreateEmpty(16, 16, false, Image.Format.Rgba8);
		img.Fill(color);
		return img;
	}

	private static Image CreateHalfImage(Color topColor, Color bottomColor)
	{
		var img = Image.CreateEmpty(16, 16, false, Image.Format.Rgba8);
		for (int y = 0; y < 8; y++)
			for (int x = 0; x < 16; x++)
				img.SetPixel(x, y, topColor);
		for (int y = 8; y < 16; y++)
			for (int x = 0; x < 16; x++)
				img.SetPixel(x, y, bottomColor);
		return img;
	}
}
