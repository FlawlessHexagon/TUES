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
    private ChunkManager? _chunkManager;
    private Camera3D _camera = null!;

    private float _yaw = 0f;
    private float _pitch = 0f;

    private double _lastInteractTime = 0;
    private const double InteractCooldown = 0.15; // 150ms rhythm

    public void Init(Player player, ChunkManager? chunkManager)
    {
        _targetPlayer = player;
        _chunkManager = chunkManager;
    }

    public override void _Ready()
    {
        _camera = new Camera3D();
        _camera.Position = new Vector3(0, 1.6f, 0); // Eye level
        _camera.Current = true; // Tell Godot to actually look through this camera!
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

    public override void _Process(double delta)
    {
        if (_targetPlayer == null) return;

        // Interaction
        if (_chunkManager != null && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            bool breaking = Input.IsActionPressed("interact_break");
            bool placing = Input.IsActionPressed("interact_place");

            if (breaking || placing)
            {
                double now = Time.GetTicksMsec() / 1000.0;
                if (now - _lastInteractTime >= InteractCooldown)
                {
                    _lastInteractTime = now;
                    Vector3 origin = _camera.GlobalPosition;
                    Vector3 direction = -_camera.GlobalTransform.Basis.Z;
                    
                    if (DdaRaycast.TryRaycast(origin, direction, 5.0f, _chunkManager, out Vector3I hitPos, out Vector3I prevPos))
                    {
                        if (breaking)
                        {
                            _chunkManager.ApplyVoxelDelta(hitPos, VoxelRegistry.AirId); 
                            GD.Print($"[Interact] Broke block at {hitPos}");
                        }
                        else
                        {
                            ushort stoneId = VoxelRegistry.GetRuntimeId("tues:stone");
                            _chunkManager.ApplyVoxelDelta(prevPos, stoneId); 
                            GD.Print($"[Interact] Placed stone at {prevPos}");
                        }
                    }
                }
            }
        }

        // Hardware Polling for Entity
        Vector2 inputDir = Vector2.Zero;
        if (InputMap.HasAction("move_forward")) 
        {
            inputDir = Input.GetVector("move_left", "move_right", "move_forward", "move_backward");
        }
        
        bool isJumping = InputMap.HasAction("jump") && Input.IsActionJustPressed("jump");
        bool isSprinting = InputMap.HasAction("sprint") && Input.IsActionPressed("sprint");

        _targetPlayer.CurrentInput = new PlayerInputState(inputDir, _yaw, isJumping, isSprinting);
    }
}
