using Godot;

namespace TheUniversalEntertainmentSystem;
using TheUniversalEntertainmentSystem.API;

/// <summary>
/// A lightweight, network-safe struct representing a player's physical intent for a single physics frame.
/// </summary>
public readonly struct PlayerInputState
{
    public readonly Vector2 MoveDirection;
    public readonly float TargetYaw;
    public readonly bool IsJumping;
    public readonly bool IsSprinting;
    public readonly bool IsDescending;

    public PlayerInputState(Vector2 moveDirection, float targetYaw, bool isJumping, bool isSprinting, bool isDescending)
    {
        MoveDirection = moveDirection;
        TargetYaw = targetYaw;
        IsJumping = isJumping;
        IsSprinting = isSprinting;
        IsDescending = isDescending;
    }
}
