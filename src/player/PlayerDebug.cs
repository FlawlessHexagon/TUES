using Godot;

namespace TheUniversalEntertainmentSystem;

/// <summary>
/// A headless debug script that mathematically asserts PlayerKinematics.
/// No frames. No time simulation. Pure data assertions.
/// </summary>
public partial class PlayerDebug : SceneTree
{
    public override void _Initialize()
    {
        GD.Print("=== Initializing PlayerDebug Pure Math Test ===");

        // Test 1: Gravity Application
        Vector3 vel1 = PlayerKinematics.CalculateVelocity(Vector3.Zero, new PlayerInputState(), 4.3f, 6.0f, 6.5f, 9.8f, 0.1, false);
        if (!Mathf.IsEqualApprox(vel1.Y, -0.98f)) GD.PrintErr($"Gravity failed: expected -0.98, got {vel1.Y}");

        // Test 2: Jump (Blocked in air)
        Vector3 vel2 = PlayerKinematics.CalculateVelocity(Vector3.Zero, new PlayerInputState(Vector2.Zero, 0, true, false), 4.3f, 6.0f, 6.5f, 9.8f, 0.1, false);
        if (!Mathf.IsEqualApprox(vel2.Y, -0.98f)) GD.PrintErr("Air Jump failed.");

        // Test 3: Jump (Success on floor)
        Vector3 vel3 = PlayerKinematics.CalculateVelocity(Vector3.Zero, new PlayerInputState(Vector2.Zero, 0, true, false), 4.3f, 6.0f, 6.5f, 9.8f, 0.1, true);
        if (!Mathf.IsEqualApprox(vel3.Y, 6.5f)) GD.PrintErr("Floor Jump failed.");

        // Test 4: Forward Movement (Yaw = 0) -> +Y input translates to -Z movement
        Vector3 vel4 = PlayerKinematics.CalculateVelocity(Vector3.Zero, new PlayerInputState(new Vector2(0, -1), 0, false, false), 4.3f, 6.0f, 6.5f, 9.8f, 0.1, true);
        if (!Mathf.IsEqualApprox(vel4.Z, -4.3f)) GD.PrintErr($"Forward movement failed. Z={vel4.Z}");

        // Test 5: Forward Movement (Yaw = 90 deg / PI/2) -> +Y input translates to -X movement
        Vector3 vel5 = PlayerKinematics.CalculateVelocity(Vector3.Zero, new PlayerInputState(new Vector2(0, -1), Mathf.Pi / 2f, false, false), 4.3f, 6.0f, 6.5f, 9.8f, 0.1, true);
        if (!Mathf.IsEqualApprox(vel5.X, -4.3f)) GD.PrintErr($"Rotated forward movement failed. X={vel5.X}");

        GD.Print("All kinematic math assertions passed perfectly.");
        GD.Print("=== PlayerDebug Finished ===");
        Quit();
    }
}
