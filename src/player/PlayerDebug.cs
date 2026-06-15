using Godot;

namespace TheUniversalEntertainmentSystem;

/// <summary>
/// A headless debug script inheriting SceneTree to verify the Player kinematics programmatically.
/// </summary>
public partial class PlayerDebug : SceneTree
{
    private Player _player = null!;
    private int _frames = 0;

    public override void _Initialize()
    {
        GD.Print("=== Initializing PlayerDebug SceneTree ===");

        // Setup temporary input actions
        string[] actions = { "move_forward", "move_backward", "move_left", "move_right", "jump", "sprint" };
        foreach (var action in actions)
        {
            if (!InputMap.HasAction(action))
            {
                InputMap.AddAction(action);
            }
        }

        // Add a floor
        var floor = new StaticBody3D();
        var floorCol = new CollisionShape3D();
        floorCol.Shape = new BoxShape3D { Size = new Vector3(20, 1, 20) };
        floorCol.Position = new Vector3(0, -0.5f, 0); 
        floor.AddChild(floorCol);
        Root.AddChild(floor);

        // Add player
        _player = new Player();
        _player.Init(null); // Injecting null ChunkManager for test
        _player.Position = new Vector3(0, 1.0f, 0); // Start falling near the ground
        Root.AddChild(_player);

        GD.Print($"[Frame 0] Initial Player Position: {_player.Position}");
    }

    public override bool _Process(double delta)
    {
        _frames++;

        if (_frames == 10)
        {
            GD.Print(">>> Artificial Input: move_forward PRESS");
            Input.ActionPress("move_forward");
        }

        if (_frames == 60)
        {
            GD.Print(">>> Artificial Input: move_forward RELEASE");
            Input.ActionRelease("move_forward");
        }

        if (_frames % 10 == 0)
        {
            GD.Print($"[Frame {_frames:000}] Pos: {_player.Position:F3}, Vel: {_player.Velocity:F3}, OnFloor: {_player.IsOnFloor()}");
        }

        if (_frames >= 100)
        {
            GD.Print("=== PlayerDebug Finished ===");
            Quit();
            return true;
        }
        return false;
    }
}
