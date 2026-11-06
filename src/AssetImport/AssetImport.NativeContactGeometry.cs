using System;
using UnityEngine;

namespace AssetImport
{
    internal static class NativeContactGeometry
    {
        internal static Vector3 HalfAxis(float radius, float height, int direction, Quaternion rotation)
        {
            var axis = direction == 0 ? new Vector3(1, 0, 0) : direction == 2 ? new Vector3(0, 0, 1) : new Vector3(0, 1, 0);
            return rotation * axis * Math.Max(0, height * .5f - radius);
        }
        internal static Vector3 ContactHalfAxis(NativeContactShape shape)
        { return shape.Capsule ? HalfAxis(shape.Radius, shape.Height, shape.Direction, shape.Rotation) : Vector3.zero; }
        private static double Dot(Vector3 a, Vector3 b) { return (double)a.x * b.x + (double)a.y * b.y + (double)a.z * b.z; }
        private static double Clamp(double x) { return Math.Max(0, Math.Min(1, x)); }

        // Closest points on two finite segments, including point and parallel
        // degeneracies. Solves the clamped quadratic in the segment parameters.
        internal static void Closest(Vector3 p, Vector3 q, Vector3 r, Vector3 s, out Vector3 first, out Vector3 second)
        {
            var u = q - p; var v = s - r; var w = p - r;
            double a = Dot(u, u), b = Dot(u, v), c = Dot(v, v), d = Dot(u, w), e = Dot(v, w);
            double x = 0, y = 0;
            if (a <= 1e-24) y = c <= 1e-24 ? 0 : Clamp(e / c);
            else if (c <= 1e-24) x = Clamp(-d / a);
            else
            {
                double denominator = a * c - b * b;
                x = denominator > a * c * 1e-15 ? Clamp((b * e - c * d) / denominator) : 0;
                y = (b * x + e) / c;
                if (y < 0) { y = 0; x = Clamp(-d / a); }
                else if (y > 1) { y = 1; x = Clamp((b - d) / a); }
            }
            first = p + u * (float)x; second = r + v * (float)y;
        }
        internal static float Gap(Vector3 center, Vector3 half, float radius, Vector3 other, Vector3 otherHalf, float otherRadius)
        {
            Vector3 a, b; Closest(center - half, center + half, other - otherHalf, other + otherHalf, out a, out b);
            return (a - b).magnitude - radius - otherRadius;
        }
    }
}
