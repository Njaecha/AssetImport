using System;
using System.Collections.Generic;
using System.Text;

namespace AssetImport
{
    internal sealed class PmxBodyCollider
    {
        internal int Index, Bone, Direction;
        internal string Name;
        internal PmxModel.V3 LocalPosition;
        internal float[] Rotation;
        internal float Radius, Height;
        internal bool BoxApproximation;
    }

    internal sealed class PmxChainCollision
    {
        // Indices refer to the plan's collider list, not the PMX body table.
        internal int[] ColliderIndices;
        // Subset added by the user's explicit hair-body collision override.
        internal int[] AssistedColliderIndices = new int[0];
        internal float ParticleRadius;
        internal bool HasPhysicsMetadata, MixedFilters;
    }

    // Pure data conversion. No Bullet bodies, Unity rigid bodies or joints are
    // created: animation-following PMX bodies become Dynamic Bone obstacles.
    internal sealed class PmxCollisionPlan
    {
        internal const float UnitScale = PmxSceneBuilder.UnitScale;
        internal readonly List<PmxBodyCollider> Colliders = new List<PmxBodyCollider>();
        internal readonly List<PmxChainCollision> Chains = new List<PmxChainCollision>();
        internal readonly List<string> Warnings = new List<string>();

        internal static PmxCollisionPlan Build(PmxModel model, IList<int[]> chainBones, bool hairCollisionAssist = false)
        {
            if (model == null) throw new ArgumentNullException("model");
            if (chainBones == null) throw new ArgumentNullException("chainBones");
            var plan = new PmxCollisionPlan();
            var bodies = model.RigidBodyData ?? new PmxModel.RigidBody[0];
            var bones = model.Bones ?? new PmxModel.Bone[0];
            var allDynamicBones = new HashSet<int>();
            var chainSets = new List<HashSet<int>>();
            var physicsBones = new HashSet<int>();
            foreach (var body in bodies)
                if (body != null && (body.Mode == 1 || body.Mode == 2)) physicsBones.Add(body.Bone);
            var hairRoots = hairCollisionAssist ? PmxPhysicsRootSuggestions.Build(model).HairRoots : new int[0];
            foreach (var chain in chainBones)
            {
                var set = new HashSet<int>();
                if (chain != null)
                    foreach (int index in chain)
                        if (index >= 0 && index < bones.Length) set.Add(index);
                chainSets.Add(set);
                allDynamicBones.UnionWith(set);
            }

            for (int i = 0; i < bodies.Length; i++)
            {
                var body = bodies[i];
                if (body == null || body.Mode != 0) continue;
                // Never attach an obstacle to a transform that any selected
                // Dynamic Bone chain also moves. That would create feedback.
                if (allDynamicBones.Contains(body.Bone)) continue;
                PmxBodyCollider collider;
                if (!TryCollider(body, i, bones, out collider))
                {
                    plan.Warnings.Add("Skipped invalid/degenerate PMX body collider " + i + " (" + body.Name + ").");
                    continue;
                }
                plan.Colliders.Add(collider);
                if (collider.BoxApproximation)
                    plan.Warnings.Add("PMX box " + i + " (" + body.Name + ") uses an enclosing capsule; clearance may be larger than the original box.");
            }

            for (int c = 0; c < chainSets.Count; c++)
            {
                var dynamicBodies = new List<PmxModel.RigidBody>();
                foreach (var body in bodies)
                    if (body != null && (body.Mode == 1 || body.Mode == 2) && chainSets[c].Contains(body.Bone))
                        dynamicBodies.Add(body);
                var chain = new PmxChainCollision { ColliderIndices = new int[0], HasPhysicsMetadata = dynamicBodies.Count != 0 };
                plan.Chains.Add(chain);
                if (!chain.HasPhysicsMetadata)
                {
                    plan.Warnings.Add("Physics chain " + c + " has no associated dynamic PMX bodies; no collision groups were guessed.");
                    continue;
                }
                var first = dynamicBodies[0];
                float minRadius = float.MaxValue;
                bool filtersValid = true;
                foreach (var body in dynamicBodies)
                {
                    chain.MixedFilters |= body.Group != first.Group || body.CollisionMask != first.CollisionMask;
                    filtersValid &= ValidFilter(body);
                    float radius = ParticleRadius(body);
                    if (radius > 0 && radius < minRadius) minRadius = radius;
                }
                chain.ParticleRadius = minRadius == float.MaxValue ? 0 : minRadius * UnitScale;
                if (!filtersValid)
                {
                    plan.Warnings.Add("Physics chain " + c + " has invalid PMX collision groups; collision binding was skipped.");
                    continue;
                }
                if (chain.MixedFilters)
                    plan.Warnings.Add("Physics chain " + c + " has mixed PMX collision filters; original PMX bindings keep only obstacles allowed by every associated body. Explicit hair-assist additions are reported separately.");
                var allowed = new List<int>();
                for (int i = 0; i < plan.Colliders.Count; i++)
                {
                    var obstacle = bodies[plan.Colliders[i].Index];
                    bool collides = true;
                    foreach (var body in dynamicBodies)
                    {
                        // The stored 16-bit mask is passed to Bullet unchanged
                        // by MMD implementations: 1 permits that target group.
                        // Bullet also requires the reverse pair to be allowed.
                        if ((body.CollisionMask & (1 << obstacle.Group)) == 0 ||
                            (obstacle.CollisionMask & (1 << body.Group)) == 0)
                        {
                            collides = false;
                            break;
                        }
                    }
                    if (collides) allowed.Add(i);
                }
                if (hairCollisionAssist && IsHairChain(chainSets[c], bones, hairRoots))
                {
                    var assisted = new List<int>();
                    for (int i = 0; i < plan.Colliders.Count; i++)
                    {
                        if (allowed.Contains(i)) continue;
                        var obstacle = bodies[plan.Colliders[i].Index];
                        if (!StableBodyObstacle(obstacle, bones, physicsBones)) continue;
                        allowed.Add(i);
                        assisted.Add(i);
                    }
                    chain.AssistedColliderIndices = assisted.ToArray();
                    if (assisted.Count != 0)
                        plan.Warnings.Add("Physics chain " + c + " explicitly overrides PMX collision groups for " + assisted.Count + " stable body obstacles (hair collision assist).");
                }
                chain.ColliderIndices = allowed.ToArray();
                if (allowed.Count == 0)
                    plan.Warnings.Add("Physics chain " + c + " has no body obstacle allowed by every associated PMX collision filter; this chain has no body collision.");
            }
            return plan;
        }

        private static bool IsHairChain(HashSet<int> chain, PmxModel.Bone[] bones, int[] hairRoots)
        {
            // A manually selected subtree of a recognized hair root is valid.
            // A broad root containing hair and another branch is not hair.
            foreach (int root in hairRoots)
            {
                bool allBelow = chain.Count != 0;
                foreach (int bone in chain) allBelow &= Below(bone, root, bones);
                if (allBelow) return true;
            }
            return false;
        }

        private static bool StableBodyObstacle(PmxModel.RigidBody body, PmxModel.Bone[] bones, HashSet<int> physicsBones)
        {
            if (!ValidBone(body.Bone, bones) || !BodySkeletonName(bones[body.Bone].Name) || !BodyObstacleName(body.Name)) return false;
            // Never supplement a collider attached below another dynamic chain,
            // even when that chain was not selected for this import. Require a
            // recognized body/global skeleton path, excluding accessory helpers.
            for (int bone = body.Bone, steps = 0; ValidBone(bone, bones) && steps < bones.Length; steps++)
            {
                if (physicsBones.Contains(bone) || (!BodySkeletonName(bones[bone].Name) && !SkeletonConnectorName(bones[bone].Name))) return false;
                bone = bones[bone].Parent;
                if (bone == -1) return true;
            }
            return false;
        }

        private static bool ValidBone(int bone, PmxModel.Bone[] bones)
        { return bone >= 0 && bone < bones.Length && bones[bone] != null; }

        private static bool Below(int bone, int ancestor, PmxModel.Bone[] bones)
        {
            for (int steps = 0; ValidBone(bone, bones) && steps < bones.Length; steps++, bone = bones[bone].Parent)
                if (bone == ancestor) return true;
            return false;
        }

        private static string BodyStem(string name)
        {
            var text = new StringBuilder();
            foreach (char ch in (name ?? "").Normalize(NormalizationForm.FormKC).ToLowerInvariant())
                if (char.IsLetter(ch)) text.Append(ch);
            string stem = text.ToString();
            foreach (string prefix in new[] { "mixamorig", "jbip", "bip" })
                if (stem.StartsWith(prefix, StringComparison.Ordinal)) { stem = stem.Substring(prefix.Length); break; }
            if (stem.StartsWith("左", StringComparison.Ordinal) || stem.StartsWith("右", StringComparison.Ordinal)) stem = stem.Substring(1);
            else if (stem.StartsWith("left", StringComparison.Ordinal)) stem = stem.Substring(4);
            else if (stem.StartsWith("right", StringComparison.Ordinal)) stem = stem.Substring(5);
            // Explicit L/R/C prefix and Blender .L/.R suffix conventions. Only
            // strip when what remains is itself an exact recognized body stem.
            if (stem.Length > 1 && (stem[0] == 'l' || stem[0] == 'r' || stem[0] == 'c') && BodyStemName(stem.Substring(1)))
                stem = stem.Substring(1);
            else if (stem.Length > 1 && (stem.EndsWith("l", StringComparison.Ordinal) || stem.EndsWith("r", StringComparison.Ordinal)) && BodyStemName(stem.Substring(0, stem.Length - 1)))
                stem = stem.Substring(0, stem.Length - 1);
            return stem;
        }

        private static bool BodySkeletonName(string name) { return BodyStemName(BodyStem(name)); }

        private static bool BodyStemName(string stem)
        {
            switch (stem)
            {
                case "頭": case "头": case "首": case "上半身": case "下半身": case "腰": case "脊柱": case "脊椎": case "脖子":
                case "肩": case "肩p": case "肩c": case "腕": case "腕捩": case "ひじ": case "肘": case "手捩": case "手首":
                case "足": case "足d": case "ひざ": case "ひざd": case "膝": case "足首": case "足首d": case "つま先": case "足先ex":
                case "上臂": case "前臂": case "手腕": case "大腿": case "小腿": case "脚踝": case "脚":
                case "head": case "neck": case "hips": case "pelvis": case "waist": case "spine": case "chest": case "upperchest":
                case "shoulder": case "clavicle": case "arm": case "upperarm": case "forearm": case "lowerarm": case "elbow": case "wrist": case "hand":
                case "upleg": case "thigh": case "leg": case "upperleg": case "lowerleg": case "calf": case "knee": case "ankle": case "foot": case "toe": case "toes": case "toebase": return true;
                default: return false;
            }
        }

        private static bool SkeletonConnectorName(string name)
        {
            switch (BodyStem(name))
            {
                case "全ての親": case "操作中心": case "センター": case "グルーブ": case "腰キャンセル左": case "腰キャンセル右":
                case "全亲": case "全親": case "中心": case "root": case "rootbone": case "armature": case "global": case "center": case "centre": case "groove": return true;
                default: return false;
            }
        }

        private static bool BodyObstacleName(string name)
        {
            string stem = BodyStem(name);
            if (BodyStemName(stem)) return true;
            // Body-region labels used on standard skeletal bones. Requiring a
            // body label also rejects a hair/ribbon/outerwear obstacle attached
            // directly to the head or torso bone.
            switch (stem)
            {
                case "でこ": case "耳": case "顎": case "胸": case "背": case "肋": case "お腹": case "腹": case "腹部": case "背部":
                case "脹脛": case "踵": case "腿": case "脛": case "額": case "额": case "下巴": case "臀": case "尻":
                case "forehead": case "ear": case "jaw": case "chin": case "back": case "rib": case "ribs": case "abdomen": case "belly": case "heel": case "shin": case "buttock": return true;
                default: return false;
            }
        }

        private static bool ValidFilter(PmxModel.RigidBody body)
        {
            return body.Group >= 0 && body.Group < 16 && body.CollisionMask >= 0 && body.CollisionMask <= 65535;
        }

        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        private static bool Finite(PmxModel.V3 value) { return Finite(value.X) && Finite(value.Y) && Finite(value.Z); }

        private static float ParticleRadius(PmxModel.RigidBody body)
        {
            if (!Finite(body.Size)) return 0;
            if (body.Shape == 0 && body.Size.X > 0) return body.Size.X;
            if (body.Shape == 2 && body.Size.X > 0 && body.Size.Y >= 0) return body.Size.X;
            if (body.Shape == 1 && body.Size.X > 0 && body.Size.Y > 0 && body.Size.Z > 0)
                return Math.Min(body.Size.X, Math.Min(body.Size.Y, body.Size.Z));
            return 0;
        }

        internal static bool TryCollider(PmxModel.RigidBody body, int index, PmxModel.Bone[] bones, out PmxBodyCollider collider)
        {
            collider = null;
            if (!ValidFilter(body) || body.Bone < -1 || body.Bone >= bones.Length ||
                !Finite(body.Position) || !Finite(body.Rotation) || ParticleRadius(body) <= 0) return false;
            var origin = body.Bone < 0 ? new PmxModel.V3() : bones[body.Bone].Position;
            if (!Finite(origin)) return false;
            float radius = body.Size.X, height = 0;
            int direction = 1;
            if (body.Shape == 2)
                height = body.Size.Y + 2 * radius; // PMX stores cylinder length; DB stores full capsule height.
            else if (body.Shape == 1)
            {
                // A box stores half extents. Enclose its cross-section in a
                // circle and use the longest dimension as the cylinder axis.
                // This deliberately enlarges the obstacle, especially at ends.
                float length = body.Size.X, u = body.Size.Y, v = body.Size.Z;
                direction = 0;
                if (body.Size.Y > length) { length = body.Size.Y; u = body.Size.X; v = body.Size.Z; direction = 1; }
                if (body.Size.Z > length) { length = body.Size.Z; u = body.Size.X; v = body.Size.Y; direction = 2; }
                radius = (float)Math.Sqrt((double)u * u + (double)v * v);
                height = 2 * length + 2 * radius;
            }
            var local = new PmxModel.V3(-(body.Position.X - origin.X) * UnitScale,
                (body.Position.Y - origin.Y) * UnitScale, -(body.Position.Z - origin.Z) * UnitScale);
            if (!Finite(radius) || !Finite(height) || !Finite(local)) return false;
            collider = new PmxBodyCollider
            {
                Index = index, Bone = body.Bone, Name = body.Name, LocalPosition = local,
                Rotation = ConvertRotation(body.Rotation), Radius = radius * UnitScale, Height = height * UnitScale,
                Direction = direction, BoxApproximation = body.Shape == 1
            };
            return true;
        }

        internal static float[] ConvertRotation(PmxModel.V3 angles)
        {
            // PMX/MMD Y-X-Z Euler order: Ry * Rx * Rz. Convert the resulting
            // quaternion by C * R * C^-1 for C = diag(-1, 1, -1), exactly the
            // coordinate conversion used for PMX vertices and bone positions.
            double sx = Math.Sin(angles.X * .5), cx = Math.Cos(angles.X * .5);
            double sy = Math.Sin(angles.Y * .5), cy = Math.Cos(angles.Y * .5);
            double sz = Math.Sin(angles.Z * .5), cz = Math.Cos(angles.Z * .5);
            return new[] {
                (float)-(cy * sx * cz + sy * cx * sz),
                (float)(sy * cx * cz - cy * sx * sz),
                (float)-(cy * cx * sz - sy * sx * cz),
                (float)(cy * cx * cz + sy * sx * sz)
            };
        }
    }
}
