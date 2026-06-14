using Godot;
using System;

namespace TheUniversalEntertainmentSystem;

/// <summary>
/// Visual test scene for Step 0.3.
/// Tests the ChunkManager's ability to stream chunks organically by moving the
/// reference point and camera continuously over the world.
/// </summary>
public partial class ChunkManagerTest : Node3D
{
	private ChunkManager? _chunkManager;
	private Camera3D? _camera;
	
	// Start at Y=120 and angle sharply downward to get a massive sweeping view of the chunks.
	private Vector3 _position = new Vector3(0, 120, 0);

	public override void _Ready()
	{
		GD.Print("═══════════════════════════════════════════════════");
		GD.Print("  Step 0.3 — Chunk Manager Streaming Test");
		GD.Print("═══════════════════════════════════════════════════");
		
		// 1. Setup Voxel Registry
		VoxelRegistry.Reset();
		VoxelRegistration.RegisterCoreTypes();
		VoxelRegistry.FreezeRegistry();

		// 2. Setup ChunkManager
		_chunkManager = new ChunkManager
		{
			LoadDistance = 16, // Much larger render distance
			UnloadDistance = 20
		};
		AddChild(_chunkManager);

		// 3. Setup Camera
		_camera = new Camera3D
		{
			Current = true
		};
		// Look sharply down and slightly left
		_camera.RotationDegrees = new Vector3(-60, -45, 0);
		AddChild(_camera);

		// 4. Setup Lighting
		var light = new DirectionalLight3D
		{
			ShadowEnabled = true,
			RotationDegrees = new Vector3(-60, 30, 0)
		};
		AddChild(light);
	}

	public override void _Process(double delta)
	{
		// Move diagonally to cross chunks on multiple axes
		float speed = 15.0f;
		_position.X += speed * (float)delta;
		_position.Z += speed * (float)delta;

		if (_camera is not null)
			_camera.Position = _position;

		if (_chunkManager is not null)
			_chunkManager.ReferencePosition = _position;
	}
}
