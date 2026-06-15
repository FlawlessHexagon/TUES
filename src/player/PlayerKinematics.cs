using Godot;

namespace TheUniversalEntertainmentSystem;

/// <summary>
/// A pure mathematical layer for player kinematics.
/// 100% deterministic and completely decoupled from Godot's SceneTree, allowing pristine unit testing.
/// </summary>
public static class PlayerKinematics
{
    public static Vector3 CalculateVelocity(
        Vector3 currentVelocity,
        PlayerInputState input,
        float walkSpeed,
        float sprintSpeed,
        float jumpVelocity,
        float gravity,
        double delta,
        bool isOnFloor)
    {
        Vector3 nextVelocity = currentVelocity;

        // Apply gravity
        if (!isOnFloor)
        {
            nextVelocity.Y -= gravity * (float)delta;
        }

        // Handle Jump
        if (input.IsJumping && isOnFloor)
        {
            nextVelocity.Y = jumpVelocity;
        }

        // Horizontal Movement
        float targetSpeed = input.IsSprinting ? sprintSpeed : walkSpeed;
        
        Vector3 flatDir = new Vector3(input.MoveDirection.X, 0, input.MoveDirection.Y);
        if (flatDir.LengthSquared() > 0)
        {
            flatDir = flatDir.Normalized().Rotated(Vector3.Up, input.TargetYaw);
            nextVelocity.X = flatDir.X * targetSpeed;
            nextVelocity.Z = flatDir.Z * targetSpeed;
        }
        else
        {
            nextVelocity.X = Mathf.MoveToward(currentVelocity.X, 0, targetSpeed);
            nextVelocity.Z = Mathf.MoveToward(currentVelocity.Z, 0, targetSpeed);
        }

        return nextVelocity;
    }
}
