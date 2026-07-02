using Godot;

namespace TheUniversalEntertainmentSystem;
using TheUniversalEntertainmentSystem.API;

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
            // Flight Mode: Smooth acceleration, high speed, no gravity
            targetSpeed = walkSpeed * 3.0f;

            // Smooth vertical flight
            float targetY = 0f;
            if (input.IsJumping) targetY += targetSpeed * 0.8f;
            if (input.IsDescending) targetY -= targetSpeed * 0.8f;
            nextVelocity.Y = Mathf.Lerp(currentVelocity.Y, targetY, 15.0f * (float)delta);

            // Smooth horizontal flight
            Vector3 flatDir = new Vector3(input.MoveDirection.X, 0, input.MoveDirection.Y);
            if (flatDir.LengthSquared() > 0)
            {
                flatDir = flatDir.Normalized().Rotated(Vector3.Up, input.TargetYaw);
            }
            
            Vector3 currentFlat = new Vector3(currentVelocity.X, 0, currentVelocity.Z);
            Vector3 targetFlat = flatDir * targetSpeed;
            currentFlat = currentFlat.Lerp(targetFlat, 15.0f * (float)delta);

            nextVelocity.X = currentFlat.X;
            nextVelocity.Z = currentFlat.Z;

            return nextVelocity;
        }

        // --- Minecraft + Valorant Style Kinematics ---

        Vector3 walkDir = new Vector3(input.MoveDirection.X, 0, input.MoveDirection.Y);
        if (walkDir.LengthSquared() > 0)
        {
            walkDir = walkDir.Normalized().Rotated(Vector3.Up, input.TargetYaw);
        }

        Vector3 targetWalkFlat = walkDir * targetSpeed;
        Vector3 currentWalkFlat = new Vector3(currentVelocity.X, 0, currentVelocity.Z);

        if (isOnFloor)
        {
            // Natural Ground Movement: Simulates physical weight.
            // 10.0f Lerp provides a split-second of momentum when stopping or starting, removing the robot snap.
            currentWalkFlat = currentWalkFlat.Lerp(targetWalkFlat, 10.0f * (float)delta);

            if (input.IsJumping)
            {
                nextVelocity.Y = jumpVelocity;
            }
        }
        else
        {
            // Natural Air Movement.
            // 1.5f Lerp provides very limited air-steering, mimicking realistic momentum carry-over.
            currentWalkFlat = currentWalkFlat.Lerp(targetWalkFlat, 1.5f * (float)delta);
            
            // Gravity
            nextVelocity.Y -= gravity * (float)delta;
        }

        nextVelocity.X = currentWalkFlat.X;
        nextVelocity.Z = currentWalkFlat.Z;

        return nextVelocity;
    }
}
