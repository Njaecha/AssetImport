using System;
using MessagePack;

namespace AssetImport
{
    internal enum PhysicsChainMode { Inherit, KeepShape, FreeHanging, Animation }
    internal enum BreastContactRole { Auto, Breast, Exclude }

    [Serializable, MessagePackObject]
    public sealed class PhysicsChainSettings
    {
        [Key("Root")] public string Root;
        [Key("Mode")] public int Mode;
        [Key("ForceScale")] public float ForceScale = 1f;
        [Key("ShapeRetention")] public float ShapeRetention = 1f;
        [Key("BreastContactRole")] public int BreastRole;
        [Key("BreastRadiusScale")] public float BreastRadiusScale = 1f;
        [Key("BreastMeshVolume")] public bool BreastMeshVolume;
        [Key("BreastRadiusOverride")] public float BreastRadiusOverride;
        [Key("BreastOffsetX")] public float BreastOffsetX;
        [Key("BreastOffsetY")] public float BreastOffsetY;
        [Key("BreastOffsetZ")] public float BreastOffsetZ;
        [Key("BreastCapsule")] public bool BreastCapsule;
        [Key("BreastLength")] public float BreastLength;
        [Key("BreastRotationX")] public float BreastRotationX;
        [Key("BreastRotationY")] public float BreastRotationY;
        [Key("BreastRotationZ")] public float BreastRotationZ;
        [Key("BreastMirrorX")] public bool BreastMirrorX;

        internal PhysicsChainSettings Copy()
        {
            return new PhysicsChainSettings
            {
                Root = Root ?? "",
                Mode = Mode >= 0 && Mode <= 3 ? Mode : 0,
                ForceScale = Clean(ForceScale, 0f, 4f, 1f),
                ShapeRetention = Clean(ShapeRetention, 0f, 1f, 1f),
                BreastRole = BreastRole >= 0 && BreastRole <= 2 ? BreastRole : 0,
                BreastRadiusScale = BreastRadiusScale <= 0 ? 1f : Clean(BreastRadiusScale, .1f, 4f, 1f),
                BreastMeshVolume = BreastMeshVolume,
                BreastRadiusOverride = Clean(BreastRadiusOverride, 0f, 2f, 0f),
                BreastOffsetX = Clean(BreastOffsetX, -2f, 2f, 0f),
                BreastOffsetY = Clean(BreastOffsetY, -2f, 2f, 0f),
                BreastOffsetZ = Clean(BreastOffsetZ, -2f, 2f, 0f),
                BreastCapsule = BreastCapsule, BreastLength = Clean(BreastLength, 0f, 4f, 0f),
                BreastRotationX = Clean(BreastRotationX, -180f, 180f, 0f),
                BreastRotationY = Clean(BreastRotationY, -180f, 180f, 0f),
                BreastRotationZ = Clean(BreastRotationZ, -180f, 180f, 0f),
                BreastMirrorX = BreastMirrorX
            };
        }
        private static float Clean(float value, float min, float max, float fallback)
        { return float.IsNaN(value) || float.IsInfinity(value) ? fallback : Math.Max(min, Math.Min(max, value)); }
    }
}
