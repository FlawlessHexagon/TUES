using System;
using Godot;

namespace TheUniversalEntertainmentSystem;

/// <summary>
/// A pure mathematical 3D Digital Differential Analyzer (DDA) for voxel raycasting.
/// Bypasses Godot's physics engine entirely for extreme performance and determinism.
/// </summary>
public static class DdaRaycast
{
    public static bool TryRaycast(Vector3 origin, Vector3 direction, float maxDistance, ChunkManager chunkManager, out Vector3I hitPos, out Vector3I previousEmptyPos)
    {
        hitPos = Vector3I.Zero;
        previousEmptyPos = Vector3I.Zero;

        if (direction == Vector3.Zero) return false;
        direction = direction.Normalized();

        int x = Mathf.FloorToInt(origin.X);
        int y = Mathf.FloorToInt(origin.Y);
        int z = Mathf.FloorToInt(origin.Z);

        // Epsilon safety: If the origin sits exactly on a grid boundary and we look negative,
        // we must step into the adjacent negative block immediately to avoid sampling behind the camera.
        const float epsilon = 0.00001f;
        if (direction.X < 0 && Mathf.Abs(origin.X - x) < epsilon) x -= 1;
        if (direction.Y < 0 && Mathf.Abs(origin.Y - y) < epsilon) y -= 1;
        if (direction.Z < 0 && Mathf.Abs(origin.Z - z) < epsilon) z -= 1;

        int stepX = Math.Sign(direction.X);
        int stepY = Math.Sign(direction.Y);
        int stepZ = Math.Sign(direction.Z);

        float tMaxX = IntBound(origin.X, direction.X);
        float tMaxY = IntBound(origin.Y, direction.Y);
        float tMaxZ = IntBound(origin.Z, direction.Z);

        float tDeltaX = stepX != 0 ? (float)stepX / direction.X : float.PositiveInfinity;
        float tDeltaY = stepY != 0 ? (float)stepY / direction.Y : float.PositiveInfinity;
        float tDeltaZ = stepZ != 0 ? (float)stepZ / direction.Z : float.PositiveInfinity;

        float distance = 0f;
        
        Vector3I currentPos = new Vector3I(x, y, z);
        previousEmptyPos = currentPos;

        while (distance <= maxDistance)
        {
            ushort voxelId = chunkManager.GetVoxelAtGlobalPos(currentPos);
            if (voxelId != 0) // tues:air is 0
            {
                hitPos = currentPos;
                return true;
            }

            previousEmptyPos = currentPos;

            if (tMaxX < tMaxY)
            {
                if (tMaxX < tMaxZ)
                {
                    currentPos.X += stepX;
                    distance = tMaxX;
                    tMaxX += tDeltaX;
                }
                else
                {
                    currentPos.Z += stepZ;
                    distance = tMaxZ;
                    tMaxZ += tDeltaZ;
                }
            }
            else
            {
                if (tMaxY < tMaxZ)
                {
                    currentPos.Y += stepY;
                    distance = tMaxY;
                    tMaxY += tDeltaY;
                }
                else
                {
                    currentPos.Z += stepZ;
                    distance = tMaxZ;
                    tMaxZ += tDeltaZ;
                }
            }
        }

        return false;
    }

    private static float IntBound(float s, float ds)
    {
        if (ds == 0) return float.PositiveInfinity;
        const float epsilon = 0.00001f;
        if (ds > 0)
        {
            float dist = Mathf.Ceil(s) - s;
            if (dist <= epsilon) dist = 1.0f;
            return dist / ds;
        }
        else
        {
            float dist = s - Mathf.Floor(s);
            if (dist <= epsilon) dist = 1.0f;
            return dist / -ds;
        }
    }
}
