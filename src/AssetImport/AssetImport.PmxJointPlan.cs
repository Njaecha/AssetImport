using System;
using System.Collections.Generic;

namespace AssetImport
{
    internal sealed class PmxJointPlan
    {
        internal sealed class AngularJoint
        {
            internal int Index, Parent, Bone;
            internal bool Reverse;
            internal PmxModel.V3 Rotation, Min, Max, SpringRate;
        }
        internal readonly List<AngularJoint> Angular = new List<AngularJoint>();
        internal readonly List<string> Warnings = new List<string>();

        // DynamicBone can represent bending along an ancestor-linked bone
        // chain. Cross links, unbound bodies and multiple bodies per bone need
        // a rigid-body solver; never silently pick one of competing joints.
        internal static PmxJointPlan Build(PmxModel model)
        {
            var result = new PmxJointPlan();
            var bodies = model.RigidBodyData ?? new PmxModel.RigidBody[0];
            var bones = model.Bones ?? new PmxModel.Bone[0];
            var joints = model.JointData ?? new PmxModel.Joint[0];
            var counts = new Dictionary<int, int>();
            foreach (var body in bodies)
                if (body != null && ValidBone(body.Bone, bones))
                { int count; counts.TryGetValue(body.Bone, out count); counts[body.Bone] = count + 1; }
            var linked = new Dictionary<int, int>();
            for (int i = 0; i < joints.Length; i++)
            {
                var j = joints[i];
                if (j == null || j.BodyA < 0 || j.BodyA >= bodies.Length || j.BodyB < 0 || j.BodyB >= bodies.Length ||
                    bodies[j.BodyA] == null || bodies[j.BodyB] == null || !Finite(j.Position) || !Finite(j.Rotation) ||
                    !Finite(j.MoveMin) || !Finite(j.MoveMax) || !Finite(j.RotateMin) || !Finite(j.RotateMax) ||
                    !Nonnegative(j.MoveSpring) || !Nonnegative(j.RotateSpring))
                { result.Warnings.Add("Joint " + i + " has missing bodies or invalid parameters; native shape retention remains active."); continue; }
                var a = bodies[j.BodyA]; var b = bodies[j.BodyB]; bool reverse = false;
                if (Ancestor(b.Bone, a.Bone, bones)) { var swap = a; a = b; b = swap; reverse = true; }
                if (!Ancestor(a.Bone, b.Bone, bones) || b.Mode == 0 || counts[a.Bone] != 1 || counts[b.Bone] != 1)
                { result.Warnings.Add("Joint " + i + " (" + j.Name + ") requires coupled rigid-body simulation; native shape retention remains active."); continue; }
                int incoming; linked.TryGetValue(b.Bone, out incoming); linked[b.Bone] = incoming + 1;
                result.Angular.Add(new AngularJoint
                {
                    Index = i,
                    Parent = a.Bone,
                    Bone = b.Bone,
                    Reverse = reverse,
                    Rotation = j.Rotation,
                    Min = new PmxModel.V3(-j.RotateMax.X, j.RotateMin.Y, -j.RotateMax.Z),
                    Max = new PmxModel.V3(-j.RotateMin.X, j.RotateMax.Y, -j.RotateMin.Z),
                    SpringRate = SpringRate(b, j.RotateSpring)
                });
            }
            result.Angular.RemoveAll(j => linked[j.Bone] != 1);
            foreach (var pair in linked) if (pair.Value > 1)
                    result.Warnings.Add("Bone " + pair.Key + " has multiple incoming joints; no single angular constraint was guessed.");
            return result;
        }

        private static PmxModel.V3 SpringRate(PmxModel.RigidBody body, PmxModel.V3 spring)
        {
            // Stable positional spring response k/I, using an isotropic inertia
            // approximation. PMX units cancel; this is not Bullet's torque solver.
            double x = body.Size.X, y = body.Size.Y, z = body.Size.Z;
            double extent = body.Shape == 0 ? .4 * x * x : body.Shape == 1 ? (x * x + y * y + z * z) / 3.0 : .4 * x * x + y * y / 12.0;
            double inertia = body.Mass * extent;
            if (double.IsNaN(inertia) || double.IsInfinity(inertia) || inertia <= 1e-12) return new PmxModel.V3();
            return new PmxModel.V3((float)Math.Min(1e8, spring.X / inertia), (float)Math.Min(1e8, spring.Y / inertia), (float)Math.Min(1e8, spring.Z / inertia));
        }
        private static bool ValidBone(int bone, PmxModel.Bone[] bones) { return bone >= 0 && bone < bones.Length && bones[bone] != null; }
        private static bool Ancestor(int parent, int child, PmxModel.Bone[] bones)
        {
            if (!ValidBone(parent, bones) || !ValidBone(child, bones) || parent == child) return false;
            for (int i = 0, b = bones[child].Parent; i < bones.Length && ValidBone(b, bones); i++, b = bones[b].Parent)
                if (b == parent) return true;
            return false;
        }
        private static bool Finite(PmxModel.V3 v)
        { return !(float.IsNaN(v.X) || float.IsNaN(v.Y) || float.IsNaN(v.Z) || float.IsInfinity(v.X) || float.IsInfinity(v.Y) || float.IsInfinity(v.Z)); }
        private static bool Nonnegative(PmxModel.V3 v) { return Finite(v) && v.X >= 0 && v.Y >= 0 && v.Z >= 0; }
    }
}
