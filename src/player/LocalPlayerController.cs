using Godot;

namespace TheUniversalEntertainmentSystem;

/// <summary>
/// Manages the local human player's presentation layer: Camera, Mouse Capture, and Input Polling.
/// Feeds raw deterministic intents down into the Player entity.
/// </summary>
public partial class LocalPlayerController : Node3D
{
    [Export] public float MouseSensitivity { get; set; } = 0.002f;
    
    private Player _targetPlayer = null!;
    private Camera3D _camera = null!;

    private float _yaw = 0f;
    private float _pitch = 0f;

    public void Init(Player player)
    {
        _targetPlayer = player;
    }

    public override void _Ready()
    {
        _camera = new Camera3D();
        _camera.Position = new Vector3(0, 1.6f, 0); // Eye level
        AddChild(_camera);

        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion mouseMotion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            _yaw -= mouseMotion.Relative.X * MouseSensitivity;
            _pitch -= mouseMotion.Relative.Y * MouseSensitivity;
            
            // Clamp pitch to prevent gimbal lock
            _pitch = Mathf.Clamp(_pitch, Mathf.DegToRad(-89f), Mathf.DegToRad(89f));

            _camera.Rotation = new Vector3(_pitch, 0, 0); 
            
            // Visually rotate the player body so others see the rotation
            _targetPlayer.Rotation = new Vector3(0, _yaw, 0);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_targetPlayer == null) return;

        // Poll actual hardware inputs
        Vector2 inputDir = Vector2.Zero;
        if (InputMap.HasAction("move_forward")) 
        {
            inputDir = Input.GetVector("move_left", "move_right", "move_forward", "move_backward");
        }
        
        bool isJumping = InputMap.HasAction("jump") && Input.IsActionJustPressed("jump");
        bool isSprinting = InputMap.HasAction("sprint") && Input.IsActionPressed("sprint");

        // Feed purely constructed intent to the physical entity
        _targetPlayer.CurrentInput = new PlayerInputState(inputDir, _yaw, isJumping, isSprinting);
    }
}
