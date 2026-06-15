using Godot;

namespace TheUniversalEntertainmentSystem;

/// <summary>
/// A visual debug script to test the decoupled Phase 1.1 Player controls.
/// </summary>
public partial class PlayableDebug : SceneTree
{
    public override void _Initialize()
    {
        GD.Print("=== Starting Playable Visual Debug ===");

        InputRegistration.RegisterCoreActions();

        var floor = new StaticBody3D();
        var floorCol = new CollisionShape3D();
        var boxShape = new BoxShape3D { Size = new Vector3(50, 1, 50) };
        floorCol.Shape = boxShape;
        floorCol.Position = new Vector3(0, -0.5f, 0); 
        var floorMesh = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(50, 1, 50) }, Position = new Vector3(0, -0.5f, 0) };
        floor.AddChild(floorCol);
        floor.AddChild(floorMesh);
        Root.AddChild(floor);

        var sun = new DirectionalLight3D();
        sun.Rotation = new Vector3(Mathf.DegToRad(-45), Mathf.DegToRad(45), 0);
        sun.ShadowEnabled = true;
        Root.AddChild(sun);

        // Spawn Player (Entity)
        var player = new Player();
        player.Init(null); 
        player.Position = new Vector3(0, 1.0f, 0); 
        Root.AddChild(player);

        // Spawn Controller (Presentation)
        var controller = new LocalPlayerController();
        controller.Init(player);
        player.AddChild(controller); // Parent to player

        if (!InputMap.HasAction("ui_cancel"))
        {
            InputMap.AddAction("ui_cancel");
            InputMap.ActionAddEvent("ui_cancel", new InputEventKey { PhysicalKeycode = Key.Escape });
        }
    }

    public override bool _Process(double delta)
    {
        if (Input.IsActionJustPressed("ui_cancel"))
        {
            Quit();
            return true;
        }
        return false;
    }
}
