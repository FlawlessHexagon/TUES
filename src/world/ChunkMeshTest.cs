using Godot;
using System;

namespace TheUniversalEntertainmentSystem;

/// <summary>
/// Verification scene script for Step 0.2 — Chunk Meshing.
/// Creates a test chunk, fills it with layered terrain, meshes it, and visually
/// displays the result in the Godot scene with a camera and lighting.
/// </summary>
public partial class ChunkMeshTest : Node3D
{
	private int _passed;
	private int _failed;

	public override void _Ready()
	{
		GD.Print("═══════════════════════════════════════════════════");
		GD.Print("  Step 0.2 — Chunk Meshing Verification");
		GD.Print("═══════════════════════════════════════════════════");
		GD.Print("");

		try
		{
			SetupTestEnvironment();
			RunVerifications();
		}
		catch (Exception e)
		{
			GD.PrintErr($"Fatal error during chunk mesh testing: {e}");
		}

		GD.Print("");
		GD.Print("═══════════════════════════════════════════════════");
		GD.Print($"  Results: {_passed} passed, {_failed} failed");
		GD.Print("═══════════════════════════════════════════════════");

		if (_failed > 0)
			GD.PrintErr($"  ⚠ {_failed} verification(s) FAILED.");
		else
			GD.Print("  All verifications passed.");
	}

	private void SetupTestEnvironment()
	{
		// 1. Setup Voxel Registry
		VoxelRegistry.Reset();
		VoxelRegistration.RegisterCoreTypes();
		VoxelRegistry.FreezeRegistry();

		// 2. Setup Chunk Mesher
		Image atlasImage = ChunkMesher.CreateAtlasImage();
		ImageTexture atlasTexture = ImageTexture.CreateFromImage(atlasImage);
		ChunkMesher.Initialize(atlasTexture);
	}

	private void RunVerifications()
	{
		// ── Test 1: All-Air chunk returns null ────────────────────────────────
		var emptyChunk = new Chunk(Vector3I.Zero);
		MeshResult? emptyResult = ChunkMesher.BuildMesh(emptyChunk);
		
		Assert(!emptyResult.HasValue,
			"All-air chunk returns null from BuildMesh()",
			"Expected null, got a MeshResult");

		// ── Test 2 & 3: Layered terrain meshing and internal face culling ─────
		var terrainChunk = new Chunk(Vector3I.Zero);
		FillLayeredTerrain(terrainChunk);

		MeshResult? terrainResult = ChunkMesher.BuildMesh(terrainChunk);

		if (!terrainResult.HasValue)
		{
			Fail("Layered terrain chunk generates mesh", "BuildMesh() returned null");
			return; // Can't proceed
		}
		Pass("Layered terrain chunk generates mesh");

		ArrayMesh mesh = terrainResult.Value.Mesh;
		
		// The terrain chunk is 16x16 and 9 blocks high (y=0 to y=8).
		// Exposed faces:
		// Top (y=8): 16x16 = 256 faces
		// Bottom (y=0): 16x16 = 256 faces
		// Sides (4x): 16x9 = 144 faces each, 144x4 = 576 faces
		// Total exposed faces = 256 + 256 + 576 = 1088 faces.
		// Each face = 2 triangles = 6 indices.
		// Expected index count = 1088 * 6 = 6528.
		// All these are opaque. We should only have 1 surface (opaque).

		int surfaceCount = mesh.GetSurfaceCount();
		Assert(surfaceCount == 1,
			"Terrain mesh has exactly 1 surface (no transparent voxels)",
			$"Expected 1 surface, got {surfaceCount}");

		if (surfaceCount > 0)
		{
			// Verify internal faces are culled by checking vertex count
			var arrays = mesh.SurfaceGetArrays(0);
			var vertexArray = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
			int vertexCount = vertexArray.Length;

			// 1088 faces * 4 vertices = 4352 vertices
			Assert(vertexCount == 4352,
				"Internal faces are culled (verified via vertex count)",
				$"Expected 4352 vertices for 16x16x9 block, got {vertexCount}. (If > 4352, internal faces leaked)");
		}

		// ── Visual Setup ──────────────────────────────────────────────────────
		// Add the visual mesh to the scene tree so it can be seen
		var meshInstance = new MeshInstance3D
		{
			Mesh = mesh,
			Name = "ChunkMesh"
		};
		AddChild(meshInstance);

		// Ensure solid blocks generate collision geometry
		MeshResult? meshResult = ChunkMesher.BuildMesh(terrainChunk);
		if (meshResult == null || meshResult.Value.CollisionFaces == null)
		{
			GD.PrintErr("Test failed: Solid chunks must produce a collision shape.");
			return;
		}

		if (meshResult.Value.CollisionFaces.Length == 0)
		{
			GD.PrintErr("Test failed: Solid chunks must have faces in collision shape.");
		}
		// Add collision geometry
		if (terrainResult.Value.CollisionFaces is not null)
		{
            var concaveShape = new ConcavePolygonShape3D();
            concaveShape.SetFaces(terrainResult.Value.CollisionFaces);
            concaveShape.BackfaceCollision = true;

			var staticBody = new StaticBody3D { Name = "ChunkCollision" };
			var collisionShape = new CollisionShape3D
			{
				Shape = concaveShape
			};
			staticBody.AddChild(collisionShape);
			AddChild(staticBody);
		}

		SetupLightingAndCamera();
	}

	private void FillLayeredTerrain(Chunk chunk)
	{
		ushort stoneId = VoxelRegistry.GetRuntimeId("tues:stone");
		ushort dirtId = VoxelRegistry.GetRuntimeId("tues:dirt");
		ushort grassId = VoxelRegistry.GetRuntimeId("tues:grass");

		// Directly access voxel array for bulk generation
		ushort[] voxels = chunk.Voxels;

		for (int y = 0; y < Chunk.SizeY; y++)
		{
			ushort currentId = VoxelRegistry.AirId;
			if (y <= 5) currentId = stoneId;
			else if (y <= 7) currentId = dirtId;
			else if (y == 8) currentId = grassId;

			if (currentId == VoxelRegistry.AirId)
				continue; // Skip air layers, they are already 0

			for (int z = 0; z < Chunk.SizeZ; z++)
			{
				for (int x = 0; x < Chunk.SizeX; x++)
				{
					voxels[Chunk.FlatIndex(x, y, z)] = currentId;
				}
			}
		}
	}

	private void SetupLightingAndCamera()
	{
		// Add a directional light
		var light = new DirectionalLight3D
		{
			ShadowEnabled = true,
			RotationDegrees = new Vector3(-45, 45, 0)
		};
		AddChild(light);

		// Add a camera looking at the chunk
		var camera = new Camera3D
		{
			Position = new Vector3(8, 20, 24),
			Current = true
		};
		camera.LookAtFromPosition(camera.Position, new Vector3(8, 8, 8), Vector3.Up);
		AddChild(camera);
	}

	// ── Assertion helpers ──────────────────────────────────────────────────

	private void Assert(bool condition, string testName, string failureDetail)
	{
		if (condition)
			Pass(testName);
		else
			Fail(testName, failureDetail);
	}

	private void Pass(string testName)
	{
		_passed++;
		GD.Print($"  [PASS] {testName}");
	}

	private void Fail(string testName, string detail)
	{
		_failed++;
		GD.PrintErr($"  [FAIL] {testName}: {detail}");
	}
}
