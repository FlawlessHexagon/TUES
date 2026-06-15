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
        bool isOnFloor,
        bool isFlying)
    {
        Vector3 nextVelocity = currentVelocity;

        float targetSpeed = input.IsSprinting ? sprintSpeed : walkSpeed;

        if (isFlying)
        {
            // Flight Mode: No gravity, distinct vertical movement, higher speed, instant deceleration
            targetSpeed = walkSpeed * 3.0f; // Creative flight is fast

            // Vertical flight
            nextVelocity.Y = 0;
            if (input.IsJumping) nextVelocity.Y += jumpVelocity * 0.8f;
            if (input.IsDescending) nextVelocity.Y -= jumpVelocity * 0.8f;

            // Horizontal flight
            Vector3 flatDir = new Vector3(input.MoveDirection.X, 0, input.MoveDirection.Y);
            if (flatDir.LengthSquared() > 0)
            {
                flatDir = flatDir.Normalized().Rotated(Vector3.Up, input.TargetYaw);
                nextVelocity.X = flatDir.X * targetSpeed;
                nextVelocity.Z = flatDir.Z * targetSpeed;
            }
            else
            {
                // Instant stop in air when flying
                nextVelocity.X = 0;
                nextVelocity.Z = 0;
            }

            return nextVelocity;
        }

        // --- Standard Walking Mode ---

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
        Vector3 walkDir = new Vector3(input.MoveDirection.X, 0, input.MoveDirection.Y);
        if (walkDir.LengthSquared() > 0)
        {
            walkDir = walkDir.Normalized().Rotated(Vector3.Up, input.TargetYaw);
            nextVelocity.X = walkDir.X * targetSpeed;
            nextVelocity.Z = walkDir.Z * targetSpeed;
        }
        else
        {
            nextVelocity.X = Mathf.MoveToward(currentVelocity.X, 0, targetSpeed);
            nextVelocity.Z = Mathf.MoveToward(currentVelocity.Z, 0, targetSpeed);
        }

        return nextVelocity;
    }
}
