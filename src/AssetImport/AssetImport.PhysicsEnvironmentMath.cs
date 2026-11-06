using System;
using UnityEngine;

namespace AssetImport
{
    internal static class PhysicsEnvironmentMath
    {
        internal static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }

        // Find the closest contact on the intersection of a horizontal plane
        // and the link-length sphere. Projection followed by normalizing alone
        // can pull the particle straight back below the floor.
        internal static Vector3 Contact(Vector3 position, Vector3 parent, Vector3 fallback, float length, float height)
        {
            if (!Finite(height) || !Finite(length) || length <= 0f || position.y >= height) return position;
            float dy = height - parent.y;
            if (dy >= length) return new Vector3(parent.x, parent.y + length, parent.z);
            if (dy <= -length) return position;
            float dx = position.x - parent.x, dz = position.z - parent.z;
            float horizontal = dx * dx + dz * dz;
            if (horizontal < 1e-12f * length * length)
            {
                dx = fallback.x; dz = fallback.z;
                horizontal = dx * dx + dz * dz;
                if (horizontal < 1e-12f * length * length) { dx = 1f; dz = 0f; horizontal = 1f; }
            }
            float factor = (float)Math.Sqrt(Math.Max(0f, length * length - dy * dy) / horizontal);
            return new Vector3(parent.x + dx * factor, height, parent.z + dz * factor);
        }
    }
}
