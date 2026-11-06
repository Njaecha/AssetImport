using AssetImport;
using UnityEngine;

internal static class JointChecks
{
    internal static void Run(byte[] fixture, Action<bool, string> check)
    {
        byte[] bodyFixture = RigidBodyReaderChecks.Fixture(fixture, new() { Bone = 0 }, new() { Bone = 0, Mode = 1 });
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(bodyFixture, 0, bodyFixture.Length - 4);
        writer.Write(1); // joint count
        writer.Write(0); writer.Write(0); // names
        int typePosition = (int)stream.Position;
        writer.Write((byte)0); writer.Write((sbyte)0); writer.Write((sbyte)1);
        int floatsPosition = (int)stream.Position;
        for (int i = 1; i <= 24; i++) writer.Write(i * .125f);
        byte[] bytes = stream.ToArray();
        var read = PmxReader.Read(bytes);
        check(read.Joints == 1 && read.JointData.Length == 1 && read.JointData[0].BodyB == 1, "joint count and body indices retained");
        var joint = read.JointData[0];
        var vectors = new[] { joint.Position, joint.Rotation, joint.MoveMin, joint.MoveMax, joint.RotateMin, joint.RotateMax, joint.MoveSpring, joint.RotateSpring };
        for (int i = 0; i < vectors.Length; i++)
            check(vectors[i].X == (i * 3 + 1) * .125f && vectors[i].Y == (i * 3 + 2) * .125f && vectors[i].Z == (i * 3 + 3) * .125f,
                "joint vector wire order " + i);
        for (int i = bodyFixture.Length - 4; i < bytes.Length; i++) Reject(bytes[..i], check, "truncated joint record");
        foreach (int offset in new[] { typePosition, typePosition + 1, typePosition + 2 })
        {
            var bad = (byte[])bytes.Clone(); bad[offset] = 3;
            Reject(bad, check, "unsupported type or invalid body reference");
        }
        for (int i = 0; i < 24; i++)
        {
            var bad = (byte[])bytes.Clone(); BitConverter.GetBytes(float.NaN).CopyTo(bad, floatsPosition + i * 4);
            var parsed = PmxReader.Read(bad);
            check(parsed.Vertices.Length == read.Vertices.Length && PmxJointPlan.Build(parsed).Warnings.Count > 0,
                "invalid optional joint parameter leaves geometry importable with a diagnostic");
        }
        check(PmxReader.Read((byte[])bytes.Clone()).JointData[0].RotateSpring.Z == 3f, "cached PMX blob retains joint metadata");

        var model = Model();
        var plan = PmxJointPlan.Build(model);
        check(plan.Angular.Count == 2 && plan.Warnings.Count == 0, "ancestor-linked PMX joints recognized without semantic names");
        var first = plan.Angular[0];
        check(first.Parent == 0 && first.Bone == 1 && !first.Reverse && first.Min.X == -.4f && first.Max.X == .1f &&
            first.Min.Y == -.2f && first.Max.Y == .5f && first.Min.Z == -.6f && first.Max.Z == .3f,
            "angular limits convert asymmetric PMX axes exactly once");
        check(first.SpringRate.X > 0 && first.SpringRate.Y > first.SpringRate.X, "mass and dimensions influence spring response");
        model.JointData[0].RotateMin.X = 1; model.JointData[0].RotateMax.X = -1;
        check(PmxJointPlan.Build(model).Angular[0].Min.X > PmxJointPlan.Build(model).Angular[0].Max.X,
            "coordinate conversion preserves a PMX free axis");
        model = Model();
        model.JointData[0].BodyA = 1; model.JointData[0].BodyB = 0;
        check(PmxJointPlan.Build(model).Angular[0].Reverse, "reversed body pair retains the original frame convention");
        model.JointData = [model.JointData[0], model.JointData[0]];
        check(PmxJointPlan.Build(model).Angular.Count == 0, "multiple incoming joints are not arbitrarily selected");
        model = Model(); model.RigidBodyData = [.. model.RigidBodyData, model.RigidBodyData[1]];
        check(PmxJointPlan.Build(model).Angular.Count == 0, "multiple bodies on one bone require rigid-body dynamics");
        model = Model(); model.Bones[2].Parent = 0;
        check(PmxJointPlan.Build(model).Angular.Count == 1 && PmxJointPlan.Build(model).Warnings.Count == 1,
            "cross-linked siblings fall back with a diagnostic");
        model = Model(); model.RigidBodyData[1].Bone = -1;
        check(PmxJointPlan.Build(model).Angular.Count == 0, "unbound bodies cannot be mapped to bone rotations");
        model = Model(); model.JointData[0].RotateSpring.X = -1;
        check(PmxJointPlan.Build(model).Angular.Count == 1, "negative optional springs are skipped");

        // Production import bindings, including copy/reload ownership.
        model = Model();
        var import = new Import(model); var chain = import.Chain(1);
        import.PreparePmxJointPhysics([chain]);
        check(import.JointPhysicsBindings.Length == 1 && import.JointPhysicsBindings[0].Joints.Length == 2,
            "runtime import maps the selected subtree's angular joints");
        check(import.JointPhysicsBindings[0].Joints[0].Parent == import.Bone(0) &&
            import.JointPhysicsBindings[0].Joints[0].Bone == import.Bone(1), "joint binding uses this model's transforms");
        var copy = new Import(Model()); var copyChain = copy.Chain(1); copy.PreparePmxJointPhysics([copyChain]);
        check(copy.JointPhysicsBindings[0].Joints[0].Bone != import.JointPhysicsBindings[0].Joints[0].Bone,
            "copied models reconstruct independent joint bindings");
        import.PreparePmxJointPhysics([import.Chain(-1)]);
        check(import.JointPhysicsBindings.Length == 0, "whole-model DynamicBone root is excluded");
        import.PreparePmxJointPhysics([copyChain]);
        check(import.JointPhysicsBindings.Length == 0, "a foreign model's chain cannot claim joint bindings");
        var other = new Import(Model(), false); other.PreparePmxJointPhysics([other.Chain(1)]);
        check(other.JointPhysicsBindings.Length == 0, "non-PMX chains do not receive PMX constraints");
    }

    private static PmxModel Model()
    {
        var result = new PmxModel { Bones = [
            new() { Name = "a", Parent = -1 }, new() { Name = "b", Parent = 0 },
            new() { Name = "c", Parent = 1 }, new() { Name = "d", Parent = 2 }],
            RigidBodyData = Enumerable.Range(0, 3).Select(i => new PmxModel.RigidBody {
                Bone = i, Mode = i == 0 ? 0 : 1, Mass = 2, Shape = 0, Size = new(.5f, 0, 0) }).ToArray(),
            JointData = Enumerable.Range(0, 2).Select(i => new PmxModel.Joint { Name = "joint", BodyA = i, BodyB = i + 1,
                RotateMin = new(-.1f, -.2f, -.3f), RotateMax = new(.4f, .5f, .6f), RotateSpring = new(1, 2, 3) }).ToArray() };
        return result;
    }

    private static void Reject(byte[] bytes, Action<bool, string> check, string label)
    {
        try { PmxReader.Read(bytes); }
        catch (FormatException) { check(true, label); return; }
        check(false, "accepted " + label);
    }
}
