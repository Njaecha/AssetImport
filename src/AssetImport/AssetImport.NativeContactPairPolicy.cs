using System;
using UnityEngine;

namespace AssetImport
{
    internal static class NativeContactPairPolicy
    {
        // A volume carried by this breast cannot be an independent obstacle.
        // Authored overlap also occurs between sibling append/deformation
        // branches. Those shapes are not incoming contacts. Reference data is
        // never recaptured when enabling in an animated pose.
        internal static string Exclusion(NativeContactShape breast, NativeContactShape obstacle)
        {
            if (!breast.Bone || !obstacle.Bone) return "missing-bone";
            if (obstacle.Bone.IsChildOf(breast.Bone)) return "moves-with-breast";
            if (breast.ContactRoot && (obstacle.Bone.IsChildOf(breast.ContactRoot) ||
                (obstacle.Driver && obstacle.Driver.IsChildOf(breast.ContactRoot)))) return "same-physics-chain";
            bool ancestor = breast.Bone.IsChildOf(obstacle.Bone);
            if (!breast.HasReferencePose || !obstacle.HasReferencePose) return ancestor ? "ancestor-without-reference-pose" : null;
            float gap = ReferenceGap(breast, obstacle);
            if (!PhysicsEnvironmentMath.Finite(gap)) return "invalid-reference-geometry";
            return gap < 0 ? (ancestor ? "ancestor-overlaps-reference-pose" : "overlaps-reference-pose") : null;
        }

        internal static float ReferenceGap(NativeContactShape breast, NativeContactShape obstacle)
        {
            return NativeContactGeometry.Gap(breast.ReferenceCenter, NativeContactGeometry.ContactHalfAxis(breast), breast.Radius,
                obstacle.ReferenceCenter, NativeContactGeometry.HalfAxis(obstacle.Radius, obstacle.Height, obstacle.Direction, obstacle.Rotation), obstacle.Radius);
        }
        internal static float Gap(Vector3 center, float radius, Vector3 obstacleCenter, Quaternion rotation,
            float obstacleRadius, float height, int direction)
        {
            var delta = center - obstacleCenter;
            var localAxis = direction == 0 ? new Vector3(1, 0, 0) : direction == 2 ? new Vector3(0, 0, 1) : new Vector3(0, 1, 0);
            var axis = rotation * localAxis;
            float along = delta.x * axis.x + delta.y * axis.y + delta.z * axis.z;
            float halfSegment = Math.Max(0, height * .5f - obstacleRadius);
            return (delta - axis * Math.Max(-halfSegment, Math.Min(halfSegment, along))).magnitude - radius - obstacleRadius;
        }
    }
}
