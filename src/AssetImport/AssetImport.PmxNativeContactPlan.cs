using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace AssetImport
{
    // Collision geometry only. The game owns the simulation algorithm.
    internal sealed class PmxNativeContactPlan
    {
        internal readonly List<PmxBodyCollider> Breasts = new List<PmxBodyCollider>();
        internal readonly List<PmxBodyCollider> Obstacles = new List<PmxBodyCollider>();
        internal readonly List<PmxBodyCollider> ManualCandidates = new List<PmxBodyCollider>();
        internal readonly Dictionary<int, PmxContactBounds> MeshBounds = new Dictionary<int, PmxContactBounds>();

        internal static PmxNativeContactPlan Build(PmxModel model)
        {
            var result = new PmxNativeContactPlan();
            if (model == null || model.Bones == null) return result;
            var bodies = model.RigidBodyData ?? new PmxModel.RigidBody[0];
            var moving = new HashSet<int>();
            foreach (var body in bodies) if (body != null && body.Mode != 0) moving.Add(body.Bone);
            var breastBones = new HashSet<int>();
            var candidateBones = new HashSet<int>();
            var weightedBones = new HashSet<int>();
            foreach (var vertex in model.Vertices ?? new PmxModel.Vertex[0])
                if (vertex != null && vertex.Bones != null && vertex.Weights != null)
                    for (int i = 0; i < Math.Min(vertex.Bones.Length, vertex.Weights.Length); i++)
                    {
                        int bone = vertex.Bones[i];
                        if (vertex.Weights[i] > .01f) weightedBones.Add(bone);
                        // Ignore weak blending tails when estimating the visible
                        // part. Bounds are captured once in the authored pose.
                        if (vertex.Weights[i] < .25f || !Finite(vertex.Weights[i]) || bone < 0 || bone >= model.Bones.Length ||
                            !Finite(vertex.Position.X) || !Finite(vertex.Position.Y) || !Finite(vertex.Position.Z)) continue;
                        PmxContactBounds bounds;
                        if (!result.MeshBounds.TryGetValue(bone, out bounds))
                            result.MeshBounds.Add(bone, new PmxContactBounds(vertex.Position));
                        else bounds.Include(vertex.Position);
                    }
            for (int i = 0; i < bodies.Length; i++)
            {
                var body = bodies[i]; PmxBodyCollider shape;
                if (body == null || body.Bone < 0 || body.Bone >= model.Bones.Length ||
                    !PmxCollisionPlan.TryCollider(body, i, model.Bones, out shape)) continue;
                bool candidate = (body.Mode == 1 || body.Mode == 2) && model.Bones[body.Bone].Parent >= 0 && weightedBones.Contains(body.Bone);
                if (candidate && candidateBones.Add(body.Bone)) result.ManualCandidates.Add(shape);
                if (candidate && IsBreast(model.Bones[body.Bone].Name) && breastBones.Add(body.Bone))
                {
                    // One spherical probe per deforming bone; no generated tail
                    // and no root-selection changes in the ordinary chain.
                    result.Breasts.Add(shape);
                }
                if (body.Mode != 0) continue;
                int ancestor = body.Bone, depth = 0;
                while (ancestor >= 0 && ancestor < model.Bones.Length && depth++ < model.Bones.Length && !moving.Contains(ancestor))
                    ancestor = model.Bones[ancestor].Parent;
                if (ancestor < 0) result.Obstacles.Add(shape);
            }
            // Manual chain selection can opt in a weighted bone even without a
            // dynamic PMX body. Its rest spacing gives an editable sphere size;
            // no new ordinary physics root or unweighted helper is introduced.
            foreach (int bone in weightedBones)
            {
                if (bone < 0 || bone >= model.Bones.Length || model.Bones[bone].Parent < 0 ||
                    model.Bones[bone].Parent >= model.Bones.Length || candidateBones.Contains(bone)) continue;
                var position = model.Bones[bone].Position;
                var parent = model.Bones[model.Bones[bone].Parent].Position;
                float x = position.X - parent.X, y = position.Y - parent.Y, z = position.Z - parent.Z;
                float radius = (float)Math.Sqrt(x * x + y * y + z * z) * PmxCollisionPlan.UnitScale * .25f;
                if (float.IsNaN(radius) || float.IsInfinity(radius)) radius = .005f;
                radius = Math.Max(.005f, Math.Min(.15f, radius));
                result.ManualCandidates.Add(new PmxBodyCollider { Index = -1, Bone = bone, Radius = radius,
                    Rotation = new[] { 0f, 0f, 0f, 1f }, Name = "Bone spacing sphere" });
            }
            result.Breasts.Sort((a, b) => Depth(a.Bone, model).CompareTo(Depth(b.Bone, model)));
            result.ManualCandidates.Sort((a, b) => Depth(a.Bone, model).CompareTo(Depth(b.Bone, model)));
            return result;
        }

        private static int Depth(int bone, PmxModel model)
        { int depth = 0; for (; bone >= 0 && bone < model.Bones.Length && depth < model.Bones.Length; bone = model.Bones[bone].Parent) depth++; return depth; }

        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }

        internal string Describe(PmxModel model)
        {
            var report = new StringBuilder();
            var bodies = model.RigidBodyData ?? new PmxModel.RigidBody[0];
            report.AppendLine("bones=" + model.Bones.Length + "; bodies=" + bodies.Length + "; selectedBreasts=" + Breasts.Count + "; selectedObstacles=" + Obstacles.Count);
            int matches = 0;
            for (int bone = 0; bone < model.Bones.Length; bone++)
            {
                if (!IsBreast(model.Bones[bone].Name)) continue;
                matches++;
                bool found = false;
                for (int i = 0; i < bodies.Length; i++)
                {
                    var body = bodies[i]; if (body == null || body.Bone != bone) continue;
                    found = true; PmxBodyCollider shape;
                    string reason = !Weighted(model, bone) ? "unweighted" : model.Bones[bone].Parent < 0 ? "no-parent" :
                        body.Mode != 1 && body.Mode != 2 ? "not-dynamic" : !PmxCollisionPlan.TryCollider(body, i, model.Bones, out shape) ? "invalid-shape" :
                        Breasts.Exists(s => s.Index == i) ? "selected" : "duplicate-bone";
                    report.AppendLine("breast bone=" + model.Bones[bone].Name + "; body[" + i + "]=" + body.Name + "; mode=" + body.Mode + "; result=" + reason);
                }
                if (!found) report.AppendLine("breast bone=" + model.Bones[bone].Name + "; result=no-rigid-body");
            }
            report.AppendLine("recognizedBoneNames=" + matches);
            // Include a bounded source inventory so unknown naming conventions
            // can be distinguished from absent physics metadata.
            int listed = 0;
            for (int i = 0; i < bodies.Length; i++)
            {
                var body = bodies[i]; if (body == null || body.Mode == 0) continue;
                if (listed++ >= 128) continue;
                string bone = body.Bone >= 0 && body.Bone < model.Bones.Length ? model.Bones[body.Bone].Name : "unbound";
                report.AppendLine("source dynamic body[" + i + "]=" + body.Name + "; bone=" + bone + "; mode=" + body.Mode);
            }
            if (listed > 128) report.AppendLine("Source inventory truncated: " + (listed - 128) + " further dynamic bodies.");
            return report.ToString();
        }

        internal static bool IsBreast(string name)
        {
            var key = Regex.Replace((name ?? "").Normalize().ToLowerInvariant(), @"[\s_\.\-0-9]", "");
            return Regex.IsMatch(key, @"^(?:左|右)(?:胸|乳)(?:上|下|先|先端)?$") ||
                Regex.IsMatch(key, @"^(?:left|right|l|r)?(?:breast|bust|boob)(?:left|right|l|r)?(?:tip|end)?$");
        }

        private static bool Weighted(PmxModel model, int bone)
        {
            foreach (var vertex in model.Vertices ?? new PmxModel.Vertex[0])
                if (vertex != null && vertex.Bones != null && vertex.Weights != null)
                    for (int i = 0; i < Math.Min(vertex.Bones.Length, vertex.Weights.Length); i++)
                        if (vertex.Bones[i] == bone && vertex.Weights[i] > .01f) return true;
            return false;
        }
    }

    internal sealed class PmxContactBounds
    {
        internal PmxModel.V3 Min, Max;
        internal PmxContactBounds(PmxModel.V3 point) { Min = Max = point; }
        internal void Include(PmxModel.V3 point)
        {
            Min = new PmxModel.V3(Math.Min(Min.X, point.X), Math.Min(Min.Y, point.Y), Math.Min(Min.Z, point.Z));
            Max = new PmxModel.V3(Math.Max(Max.X, point.X), Math.Max(Max.Y, point.Y), Math.Max(Max.Z, point.Z));
        }
    }
}
