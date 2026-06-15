using Godot;

namespace TheUniversalEntertainmentSystem;

public partial class Player : CharacterBody3D
{
    [Export] public float WalkSpeed { get; set; } = 4.3f;
    [Export] public float SprintSpeed { get; set; } = 6.0f;
    [Export] public float JumpVelocity { get; set; } = 6.5f;

    private float _gravity;
    private ChunkManager? _chunkManager;
    private CollisionShape3D _collisionShape = null!;

    public void Init(ChunkManager? chunkManager)
    {
        _chunkManager = chunkManager;
    }

    public override void _Ready()
    {
        // Fetch default gravity from project settings
        _gravity = ProjectSettings.GetSetting("physics/3d/default_gravity").AsSingle();

        // Construct physical body
        _collisionShape = new CollisionShape3D();
        var boxShape = new BoxShape3D
        {
            Size = new Vector3(0.6f, 1.8f, 0.6f)
        };
        _collisionShape.Shape = boxShape;
        
        // Offset on Y-axis so CharacterBody3D origin (0, 0, 0) rests at bottom center of feet
        _collisionShape.Position = new Vector3(0, 0.9f, 0);

        AddChild(_collisionShape);
    }

    public override void _PhysicsProcess(double delta)
    {
        Vector3 velocity = Velocity;

        // Apply gravity
        if (!IsOnFloor())
        {
            velocity.Y -= _gravity * (float)delta;
        }

        // Handle jump state
        // To avoid unmapped action warnings, check if mapped, or safely default if missing
        if (InputMap.HasAction("jump") && Input.IsActionJustPressed("jump") && IsOnFloor())
        {
            velocity.Y = JumpVelocity;
        }

        // Input polling using placeholder action strings.
        // GetVector safely handles missing actions by returning Vector2.Zero.
        Vector2 inputDir = Vector2.Zero;
        if (InputMap.HasAction("move_left") && InputMap.HasAction("move_right") && InputMap.HasAction("move_forward") && InputMap.HasAction("move_backward"))
        {
            inputDir = Input.GetVector("move_left", "move_right", "move_forward", "move_backward");
        }
        
        Vector3 direction = new Vector3(inputDir.X, 0, inputDir.Y).Normalized();

        float currentSpeed = WalkSpeed;
        if (InputMap.HasAction("sprint") && Input.IsActionPressed("sprint"))
        {
            currentSpeed = SprintSpeed;
        }

        if (direction != Vector3.Zero)
        {
            velocity.X = direction.X * currentSpeed;
            velocity.Z = direction.Z * currentSpeed;
        }
        else
        {
            velocity.X = Mathf.MoveToward(Velocity.X, 0, currentSpeed);
            velocity.Z = Mathf.MoveToward(Velocity.Z, 0, currentSpeed);
        }

        Velocity = velocity;
        MoveAndSlide();
    }
}
