using Godot;

namespace TheUniversalEntertainmentSystem;

/// <summary>
/// The physical representation of a player entity. 
/// Purely responsive to `PlayerInputState`. Contains NO hardware polling or local camera logic.
/// </summary>
public partial class Player : CharacterBody3D
{
    [Export] public float WalkSpeed { get; set; } = 4.3f;
    [Export] public float SprintSpeed { get; set; } = 6.0f;
    [Export] public float JumpVelocity { get; set; } = 5.0f; // Yields ~1.27m jump height with 9.8 gravity

    public PlayerInputState CurrentInput { get; set; }
    public bool IsFlying { get; set; } = false;

    private float _gravity;
    private ChunkManager? _chunkManager;
    private CollisionShape3D _collisionShape = null!;

    public void Init(ChunkManager? chunkManager)
    {
        _chunkManager = chunkManager;
    }

    public override void _Ready()
    {
        _gravity = ProjectSettings.GetSetting("physics/3d/default_gravity").AsSingle();

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
            WalkSpeed, SprintSpeed, JumpVelocity, _gravity, delta, IsOnFloor(), IsFlying
        );

        MoveAndSlide();

        // Clear one-shot states (like jumping) after processing
        CurrentInput = new PlayerInputState(CurrentInput.MoveDirection, CurrentInput.TargetYaw, false, CurrentInput.IsSprinting, CurrentInput.IsDescending);
    }
}
