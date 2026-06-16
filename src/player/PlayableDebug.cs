using Godot;

namespace TheUniversalEntertainmentSystem;

/// <summary>
/// A visual debug script to test the decoupled Phase 1.1 Player controls.
/// Includes synchronous loading lock to prevent falling through ungenerated terrain.
/// </summary>
public partial class PlayableDebug : SceneTree
{
    private Player _player = null!;
    private ChunkManager _chunkManager = null!;
    private bool _worldLoaded = false;
    private int _frameCount = 0;

    public override void _Initialize()
    {
        GD.Print("=== Starting Playable Visual Debug ===");

        // Set FPS to 120 and disable VSync for uncapped performance testing
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = 120;

        InputRegistration.RegisterCoreActions();

        // Bootstrapper for Phase 0 Voxel system
        VoxelRegistration.RegisterCoreTypes();
        
        // Execute Step 2.0 Integration Test
        TuesEngineLoaderTest.RunTest();
        
        VoxelRegistry.FreezeRegistry();

        GameSettings.Load();

        _chunkManager = new ChunkManager();
        _chunkManager.ReferencePosition = new Vector3(0, 100, 0); // Pre-load where player spawns
        _chunkManager.OnVoxelChanged += (pos, id) => {
            GD.Print($"[Network Delta Event] Voxel at {pos} changed to {id}");
        };
        Root.AddChild(_chunkManager);

        var sun = new DirectionalLight3D();
        sun.Rotation = new Vector3(Mathf.DegToRad(-45), Mathf.DegToRad(45), 0);
        sun.ShadowEnabled = true;
        Root.AddChild(sun);

        var env = new WorldEnvironment();
        var sky = new ProceduralSkyMaterial();
        var skyEnv = new Godot.Environment();
        skyEnv.Sky = new Sky { SkyMaterial = sky };
        skyEnv.BackgroundMode = Godot.Environment.BGMode.Sky;
        env.Environment = skyEnv;
        Root.AddChild(env);

        // Spawn Player (Entity)
        _player = new Player();
        _player.Init(null); 
        _player.Position = new Vector3(0, 100.0f, 0); // Safe drop above max terrain height
        _player.SetPhysicsProcess(false); // Explicitly lock physics until world loads
        Root.AddChild(_player);

        // Spawn Controller (Presentation)
        var controller = new LocalPlayerController();
        controller.Init(_player, _chunkManager);
        _player.AddChild(controller); // Parent to player

        if (!InputMap.HasAction("ui_cancel"))
        {
            InputMap.AddAction("ui_cancel");
            InputMap.ActionAddEvent("ui_cancel", new InputEventKey { PhysicalKeycode = Key.Escape });
        }
    }

    public override bool _Process(double delta)
    {
        if (_player != null && _chunkManager != null)
        {
            _chunkManager.ReferencePosition = _player.GlobalPosition;

            if (!_worldLoaded && _chunkManager.IsChunkRadiusLoaded(Vector3.Zero, 4))
            {
                _worldLoaded = true;

                // Detect exact terrain height at X=0, Z=0
                float spawnY = 100.0f;
                for (int y = 100; y > 0; y--)
                {
                    ushort id = _chunkManager.GetVoxelAtGlobalPos(new Vector3I(0, y, 0));
                    if (id != VoxelRegistry.AirId)
                    {
                        spawnY = y + 1.0f; // Exactly flush with the top of the block, zero drop
                        break;
                    }
                }

                _player.Position = new Vector3(0, spawnY, 0);
                GD.Print($"World loaded! Spawning player smoothly at Y={spawnY}");

                // Defer physics unlocking by 0.1 seconds to allow the Godot PhysicsServer3D 
                // to synchronize the hundreds of newly attached collision meshes.
                // This completely eradicates the 'falling through the floor' bug on frame 1.
                CreateTimer(0.1).Timeout += () => 
                {
                    _player.SetPhysicsProcess(true);
                };
            }

            if (_player.Position.Y < -50)
            {
                _player.Position = new Vector3(0, 100.0f, 0); // Reset if fell into void
                _player.Velocity = Vector3.Zero;
            }
        }


        if (Input.IsActionJustPressed("ui_cancel"))
        {
            Quit();
            return true;
        }

        return false;
    }
}
