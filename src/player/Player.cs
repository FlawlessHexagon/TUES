using Godot;

namespace TheUniversalEntertainmentSystem;
using TheUniversalEntertainmentSystem.API;

/// <summary>
/// The physical representation of a player entity. 
/// Purely responsive to `PlayerInputState`. Contains NO hardware polling or local camera logic.
/// </summary>
public partial class Player : CharacterBody3D
{
    [Export] public float WalkSpeed { get; set; } = 4.3f;
    [Export] public float SprintSpeed { get; set; } = 6.0f;
    [Export] public float JumpVelocity { get; set; } = 7.0f; // Yields exactly ~1.25m jump height with 19.6 gravity
    [Export] public float Gravity { get; set; } = 19.6f; // Realistic 2G game gravity (9.8 is too floaty for games, 32 is a brick)

    public PlayerInputState CurrentInput { get; set; }
    public bool IsFlying { get; set; } = false;

    private ChunkManager? _chunkManager;
    private CollisionShape3D _collisionShape = null!;

    public void Init(ChunkManager? chunkManager)
    {
        _chunkManager = chunkManager;
    }

    public override void _Ready()
    {
        _collisionShape = new CollisionShape3D();
        var boxShape = new BoxShape3D { Size = new Vector3(0.6f, 1.8f, 0.6f) };
        _collisionShape.Shape = boxShape;
        _collisionShape.Position = new Vector3(0, 0.9f, 0);

        AddChild(_collisionShape);
    }

    public override void _PhysicsProcess(double delta)
    {
        Velocity = PlayerKinematics.CalculateVelocity(
            Velocity, 
            CurrentInput, 
            WalkSpeed, SprintSpeed, JumpVelocity, Gravity, delta, IsOnFloor(), IsFlying
        );

        MoveAndSlide();

        // Void Safety Net
        if (GlobalPosition.Y < -50.0f)
        {
            Logger.Warning("[Physics] Player fell into the void. Resurrecting at skybox.");
            GlobalPosition = new Vector3(GlobalPosition.X, 200, GlobalPosition.Z);
            Velocity = Vector3.Zero;
        }
    }
}
