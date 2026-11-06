using AssetImport;
using System;
using System.Linq;

internal static class CollisionPlanChecks
{
    internal static void Run(Action<bool, string> check)
    {
        const float scale = .0832f;
        var bones = new[] {
            Bone("body", 3, 5, 7), Bone("hair root", 3, 8, 7),
            Bone("hair tip", 3, 10, 7), Bone("other hair", 1, 8, 7), Bone("shoulder", 1, 6, 7)
        };
        var sphere = Body("chest", 0, 0, 0, 0xffff, 0, .5f, 0, 0);
        sphere.Position = new PmxModel.V3(4, 7, 10);
        sphere.Rotation = new PmxModel.V3(.31f, -.47f, .79f);
        var capsule = Body("neck", 0, 0, 15, 0xffff, 2, .25f, 2, 0);
        var box = Body("shoulder", 4, 0, 2, 0xffff, 1, 1, 2, 3);
        var rootBody = Body("model root body", -1, 0, 0, 0xffff, 0, 1, 0, 0);
        rootBody.Position = new PmxModel.V3(2, 4, 6);
        var model = Model(bones, sphere, capsule, box,
            Body("hair 1", 1, 1, 6, 0xffff, 0, .4f, 0, 0),
            Body("hair 2", 2, 2, 7, 0xffff, 1, .8f, .2f, .6f),
            Body("inside this chain", 2, 0, 0, 0xffff, 0, .1f, 0, 0),
            Body("inside other chain", 3, 0, 0, 0xffff, 0, .1f, 0, 0),
            Body("other dynamic", 3, 1, 6, 0xffff, 2, .3f, 1, 0), rootBody);
        var chains = new[] { new[] { 1, 2, 1 }, new[] { 3 }, new int[0] };
        var plan = PmxCollisionPlan.Build(model, chains);
        check(plan.Colliders.Select(c => c.Index).SequenceEqual(new[] { 0, 1, 2, 8 }), "PMX body obstacles exclude every selected dynamic chain and all dynamic bodies");
        check(plan.Chains.Count == 3 && plan.Chains[0].ColliderIndices.SequenceEqual(new[] { 0, 1, 2, 3 }), "chain collider indices address the compact plan list");
        check(plan.Chains[0].HasPhysicsMetadata && plan.Chains[0].MixedFilters, "mixed dynamic groups are explicitly identified");
        Near(check, plan.Chains[0].ParticleRadius, .2f * scale, "chain radius is smallest associated body radius/box half extent");
        Near(check, plan.Chains[1].ParticleRadius, .3f * scale, "capsule dynamic body supplies particle radius");
        check(!plan.Chains[2].HasPhysicsMetadata && plan.Chains[2].ColliderIndices.Length == 0 && plan.Chains[2].ParticleRadius == 0,
            "manual chain without dynamic metadata does not guess collision groups");
        check(plan.Warnings.Any(w => w.Contains("no associated dynamic")), "metadata absence is reported");

        var s = plan.Colliders[0];
        Near(check, s.LocalPosition.X, -scale, "body offset uses attached bone's nonzero X origin");
        Near(check, s.LocalPosition.Y, 2 * scale, "body offset uses attached bone's nonzero Y origin");
        Near(check, s.LocalPosition.Z, -3 * scale, "body offset uses attached bone's nonzero Z origin");
        Near(check, s.Radius, .5f * scale, "sphere radius converts units once");
        check(s.Height == 0 && !s.BoxApproximation, "sphere remains a sphere");
        var cap = plan.Colliders[1];
        Near(check, cap.Radius, .25f * scale, "capsule radius preserves PMX size X");
        Near(check, cap.Height, 2.5f * scale, "capsule full height includes both end radii");
        check(cap.Direction == 1, "native PMX capsule is along local Y");
        var b = plan.Colliders[2];
        check(b.BoxApproximation && b.Direction == 2, "box approximation chooses its longest local axis");
        Near(check, b.Radius, (float)Math.Sqrt(5) * scale, "box capsule encloses the two short half extents");
        Near(check, b.Height, (6 + 2 * (float)Math.Sqrt(5)) * scale, "box full height includes original axial span and round ends");
        // Every original corner must lie in the enclosing capsule. This tests
        // its geometric promise without reusing the construction formulas.
        foreach (int x in new[] { -1, 1 }) foreach (int y in new[] { -1, 1 }) foreach (int z in new[] { -1, 1 })
        {
            double px = x * scale, py = y * 2 * scale, pz = z * 3 * scale;
            double cylinderHalf = Math.Max(0, b.Height / 2 - b.Radius);
            double dz = Math.Max(0, Math.Abs(pz) - cylinderHalf);
            check(px * px + py * py + dz * dz <= b.Radius * b.Radius + 1e-7, "enclosing box capsule contains original corner");
        }
        check(plan.Warnings.Any(w => w.Contains("enclosing capsule")), "box approximation is reported, not presented as exact box physics");
        var world = plan.Colliders[3];
        check(world.Bone == -1, "unbound static PMX body stays attached to model root");
        Near(check, world.LocalPosition.X, -2 * scale, "unbound body converts model-space X");
        Near(check, world.LocalPosition.Y, 4 * scale, "unbound body converts model-space Y");
        Near(check, world.LocalPosition.Z, -6 * scale, "unbound body converts model-space Z");

        // An independent axis-matrix calculation verifies the Euler order and
        // basis conjugation. Multiple nonzero angles distinguish YXZ from XYZ.
        foreach (var axis in new[] { new double[] { 1, 0, 0 }, new double[] { 0, 1, 0 }, new double[] { 0, 0, 1 } })
        {
            var expected = Basis(axis);
            expected = AxisRotate(expected, 2, sphere.Rotation.Z);
            expected = AxisRotate(expected, 0, sphere.Rotation.X);
            expected = AxisRotate(expected, 1, sphere.Rotation.Y);
            expected = Basis(expected);
            var actual = QuaternionRotate(s.Rotation, axis);
            for (int i = 0; i < 3; i++) Near(check, (float)actual[i], (float)expected[i], "PMX YXZ quaternion agrees with independent converted basis matrix");
        }
        Near(check, s.Rotation.Sum(v => v * v), 1, "converted PMX quaternion remains normalized");

        FilterChecks(check, bones);
        DegenerateChecks(check, bones);
        HairAssistChecks(check);
        var repeated = PmxCollisionPlan.Build(model, chains);
        check(plan.Colliders.Select(c => c.Index).SequenceEqual(repeated.Colliders.Select(c => c.Index)), "repeated import preserves body ordering");
        for (int i = 0; i < plan.Colliders.Count; i++)
        {
            var a = plan.Colliders[i]; var r = repeated.Colliders[i];
            check(a.Rotation.SequenceEqual(r.Rotation) && a.Radius == r.Radius && a.Height == r.Height && a.Bone == r.Bone,
                "repeated import generates identical collider geometry");
        }
        for (int i = 0; i < plan.Chains.Count; i++)
            check(plan.Chains[i].ColliderIndices.SequenceEqual(repeated.Chains[i].ColliderIndices) && plan.Chains[i].ParticleRadius == repeated.Chains[i].ParticleRadius,
                "repeated import preserves chain bindings and radius");
        check(plan.Warnings.SequenceEqual(repeated.Warnings), "collision diagnostics have stable ordering");
    }

    private static void FilterChecks(Action<bool, string> check, PmxModel.Bone[] bones)
    {
        var obstacle = Body("high group", 0, 0, 15, 1 << 6, 0, 1, 0, 0);
        var hair = Body("hair", 1, 1, 6, 1 << 15, 0, .1f, 0, 0);
        var model = Model(bones, obstacle, hair);
        var chains = new[] { new[] { 1 } };
        check(PmxCollisionPlan.Build(model, chains).Chains[0].ColliderIndices.Length == 1, "raw mask bit 15 permits matching group; no signed short/complement bug");
        obstacle.CollisionMask = 0;
        check(PmxCollisionPlan.Build(model, chains).Chains[0].ColliderIndices.Length == 0, "obstacle mask must allow dynamic body's group");
        obstacle.CollisionMask = 1 << 6; hair.CollisionMask = 0;
        check(PmxCollisionPlan.Build(model, chains).Chains[0].ColliderIndices.Length == 0, "dynamic body's mask must allow obstacle group");
        hair.CollisionMask = 1 << 15;
        var hair2 = Body("hair different filter", 2, 2, 7, 1 << 0, 0, .2f, 0, 0);
        var zero = Body("group zero", 0, 0, 0, (1 << 6) | (1 << 7), 0, 1, 0, 0);
        obstacle.CollisionMask |= 1 << 7;
        var mixedModel = Model(bones, obstacle, zero, hair, hair2);
        var mixedChains = new[] { new[] { 1, 2 } };
        var mixed = PmxCollisionPlan.Build(mixedModel, mixedChains);
        check(mixed.Chains[0].MixedFilters && mixed.Chains[0].ColliderIndices.Length == 0, "different masks intersect per chain; prohibited collisions are never unioned");
        hair.CollisionMask |= 1;
        mixed = PmxCollisionPlan.Build(mixedModel, mixedChains);
        check(mixed.Chains[0].ColliderIndices.SequenceEqual(new[] { 1 }), "only common collision group survives mixed-filter chain");
        // Common production pattern: group 0 body ignores group 0, group 6
        // hair ignores group 6. The raw masks still permit body/hair collision.
        zero.CollisionMask = 0xfffe; hair.CollisionMask = 0xffbf;
        var standard = PmxCollisionPlan.Build(Model(bones, zero, hair), chains);
        check(standard.Chains[0].ColliderIndices.SequenceEqual(new[] { 0 }), "typical PMX 0xfffe / 0xffbf masks permit body-hair collision");
        hair.Group = 16;
        var invalid = PmxCollisionPlan.Build(Model(bones, zero, hair), chains);
        check(invalid.Chains[0].ColliderIndices.Length == 0 && invalid.Warnings.Any(w => w.Contains("invalid PMX collision groups")),
            "invalid chain collision metadata cannot accidentally enable an obstacle");
    }

    private static void DegenerateChecks(Action<bool, string> check, PmxModel.Bone[] bones)
    {
        var model = Model(bones,
            Body("zero sphere", 0, 0, 0, 0xffff, 0, 0, 0, 0),
            Body("negative capsule length", 0, 0, 0, 0xffff, 2, 1, -1, 0),
            Body("flat box", 0, 0, 0, 0xffff, 1, 1, 0, 1),
            Body("unknown shape", 0, 0, 0, 0xffff, 3, 1, 1, 1),
            Body("bad position", 0, 0, 0, 0xffff, 0, 1, 0, 0),
            Body("bad rotation", 0, 0, 0, 0xffff, 0, 1, 0, 0),
            Body("bad group", 0, 0, 16, 0xffff, 0, 1, 0, 0),
            Body("degenerate dynamic", 1, 1, 6, 0xffff, 0, 0, 0, 0));
        model.RigidBodyData[4].Position = new PmxModel.V3(float.NaN, 0, 0);
        model.RigidBodyData[5].Rotation = new PmxModel.V3(0, float.PositiveInfinity, 0);
        var plan = PmxCollisionPlan.Build(model, new[] { new[] { 1 } });
        check(plan.Colliders.Count == 0 && plan.Warnings.Count(w => w.Contains("Skipped invalid/degenerate")) == 7,
            "degenerate or nonfinite collider geometry is skipped and diagnosed");
        check(plan.Chains[0].HasPhysicsMetadata && plan.Chains[0].ParticleRadius == 0, "unusable dynamic body dimensions do not invent particle thickness");
        var empty = PmxCollisionPlan.Build(new PmxModel { Bones = bones }, new[] { new[] { 1 } });
        check(empty.Colliders.Count == 0 && !empty.Chains[0].HasPhysicsMetadata, "missing rigid body metadata is safe");
        var sphereCapsule = Body("zero cylinder", 0, 0, 0, 0xffff, 2, 1, 0, 0);
        var shortCapsule = PmxCollisionPlan.Build(Model(bones, sphereCapsule), new int[0][]);
        check(shortCapsule.Colliders.Count == 1, "capsule with zero cylinder length is a valid sphere-like obstacle");
        Near(check, shortCapsule.Colliders[0].Height, 2 * .0832f, "zero-cylinder capsule height remains its diameter");
    }

    private static void HairAssistChecks(Action<bool, string> check)
    {
        PmxModel.Bone B(string name, int parent) => new PmxModel.Bone { Name = name, Parent = parent };
        var bones = new[] {
            B("全ての親", -1), B("上半身2", 0), B("首", 1), B("頭", 2),
            B("hair 1", 3), B("hair 2", 4), B("hair 3", 5),
            B("スカート_0_0", 1), B("スカート_1_0", 7), B("ribbon", 3),
            B("unknown accessory", 1), B("head", 10), B("spine", 1), B("head", 12),
            B("head", 3), B("head tip", 14)
        };
        var model = Model(bones,
            Body("首", 2, 0, 1, 4, 0, 1, 0, 0), // Already permitted by ponytail mask.
            Body("上半身2_1", 1, 0, 0, 0, 0, 1, 0, 0),
            Body("頭２", 3, 0, 0, 0, 0, 1, 0, 0),
            Body("hair 1", 4, 0, 0, 0, 0, 1, 0, 0),
            Body("アウター親", 3, 0, 0, 0, 0, 1, 0, 0),
            Body("head", 11, 0, 0, 0, 0, 1, 0, 0),
            Body("head", 13, 0, 0, 0, 0, 1, 0, 0),
            Body("head", 14, 0, 0, 0, 0, 1, 0, 0),
            Body("chest", -1, 0, 0, 0, 0, 1, 0, 0),
            Body("head", 0, 0, 0, 0, 0, 1, 0, 0),
            Body("unidentified obstacle", 3, 0, 0, 0, 0, 1, 0, 0),
            Body("hair 2", 5, 1, 2, 0xffda, 0, .9f, 0, 0),
            Body("hair 3", 6, 1, 2, 0xffda, 0, .5f, 0, 0),
            Body("skirt", 7, 1, 4, 0xffff, 0, .2f, 0, 0),
            Body("spine", 12, 1, 2, 0xffff, 0, .2f, 0, 0),
            Body("head", 14, 1, 2, 0xffff, 0, .2f, 0, 0));
        var chains = new[] { new[] { 5, 6 }, new[] { 7, 8 }, new[] { 14, 15 } };
        var original = PmxCollisionPlan.Build(model, chains);
        var explicitOff = PmxCollisionPlan.Build(model, chains, false);
        var assisted = PmxCollisionPlan.Build(model, chains, true);
        int[] BodyIds(PmxCollisionPlan plan, int chain) => plan.Chains[chain].ColliderIndices.Select(i => plan.Colliders[i].Index).ToArray();
        check(BodyIds(original, 0).SequenceEqual(new[] { 0 }), "ponytail's original bilateral PMX mask remains authoritative by default");
        check(original.Chains.All(c => c.AssistedColliderIndices.Length == 0) && BodyIds(original, 0).SequenceEqual(BodyIds(explicitOff, 0)),
            "missing/false hair assist preserves legacy collision binding exactly");
        check(BodyIds(assisted, 0).SequenceEqual(new[] { 0, 1, 2 }),
            "explicit hair assist supplements only named stable body obstacles and keeps already permitted collisions once");
        check(assisted.Chains[0].AssistedColliderIndices.Select(i => assisted.Colliders[i].Index).SequenceEqual(new[] { 1, 2 }),
            "override diagnostics distinguish added body IDs from originally permitted IDs");
        check(BodyIds(assisted, 1).SequenceEqual(BodyIds(original, 1)) && assisted.Chains[1].AssistedColliderIndices.Length == 0,
            "hair collision override never changes a skirt chain");
        check(BodyIds(assisted, 2).SequenceEqual(BodyIds(original, 2)) && assisted.Chains[2].AssistedColliderIndices.Length == 0,
            "hair collision override never applies to a manually selected body/action chain");
        check(assisted.Chains[0].ParticleRadius == original.Chains[0].ParticleRadius,
            "collision assist does not change particle radius or PMX body geometry");
        check(assisted.Warnings.Any(w => w.Contains("explicitly overrides PMX collision groups")), "source-group override is explicitly diagnosed");
        // A manually selected lower portion of an identified hair chain is still
        // hair; a broad root that also contains a non-hair branch is not.
        check(PmxCollisionPlan.Build(model, new[] { new[] { 6 } }, true).Chains[0].AssistedColliderIndices.Length == 2,
            "manual hair subtree retains hair identification for collision assist");
        check(PmxCollisionPlan.Build(model, new[] { new[] { 3, 4, 5, 6, 9 } }, true).Chains[0].AssistedColliderIndices.Length == 0,
            "broad head root containing hair/accessories is not treated as a hair chain");

        foreach (var names in new[] {
            new[] { "root", "mixamorig:Spine2", "mixamorig:Neck", "mixamorig:Head" },
            new[] { "全亲", "脊柱", "脖子", "头" },
            new[] { "Bip01", "Bip01 Spine2", "Bip01 Neck", "Bip01 Head" }
        })
        {
            for (int i = 0; i < 4; i++) bones[i].Name = names[i];
            // Bip01 itself is not one of the accepted global-root names: the
            // bone names on their own must not broaden an unknown parent path.
            bool knownPath = names[0] != "Bip01";
            var translated = PmxCollisionPlan.Build(model, chains, true);
            check(translated.Chains[0].AssistedColliderIndices.Length == (knownPath ? 2 : 0),
                "exact Japanese/Chinese/English body names require a recognized skeleton path: " + names[0]);
        }
        bones[0].Name = "root";
        model.RigidBodyData[11].Group = 16;
        check(PmxCollisionPlan.Build(model, chains, true).Chains[0].ColliderIndices.Length == 0,
            "invalid PMX filter metadata cannot activate collision assist");
    }

    private static PmxModel Model(PmxModel.Bone[] bones, params PmxModel.RigidBody[] bodies)
    { return new PmxModel { Bones = bones, RigidBodyData = bodies, RigidBodies = bodies.Length }; }

    private static PmxModel.Bone Bone(string name, float x, float y, float z)
    { return new PmxModel.Bone { Name = name, Parent = -1, Position = new PmxModel.V3(x, y, z) }; }

    private static PmxModel.RigidBody Body(string name, int bone, int mode, int group, int mask, int shape, float x, float y, float z)
    {
        return new PmxModel.RigidBody {
            Name = name, Bone = bone, Mode = mode, Group = group, CollisionMask = mask, Shape = shape,
            Size = new PmxModel.V3(x, y, z), Position = new PmxModel.V3(), Rotation = new PmxModel.V3()
        };
    }

    private static void Near(Action<bool, string> check, float actual, float expected, string message)
    { check(Math.Abs(actual - expected) < 1e-6f, message + ": " + actual + " vs " + expected); }

    private static double[] Basis(double[] v) { return new[] { -v[0], v[1], -v[2] }; }

    private static double[] AxisRotate(double[] v, int axis, double angle)
    {
        double c = Math.Cos(angle), s = Math.Sin(angle);
        if (axis == 0) return new[] { v[0], c * v[1] - s * v[2], s * v[1] + c * v[2] };
        if (axis == 1) return new[] { c * v[0] + s * v[2], v[1], -s * v[0] + c * v[2] };
        return new[] { c * v[0] - s * v[1], s * v[0] + c * v[1], v[2] };
    }

    private static double[] QuaternionRotate(float[] q, double[] v)
    {
        double x = q[0], y = q[1], z = q[2], w = q[3];
        return new[] {
            (1 - 2 * y * y - 2 * z * z) * v[0] + (2 * x * y - 2 * z * w) * v[1] + (2 * x * z + 2 * y * w) * v[2],
            (2 * x * y + 2 * z * w) * v[0] + (1 - 2 * x * x - 2 * z * z) * v[1] + (2 * y * z - 2 * x * w) * v[2],
            (2 * x * z - 2 * y * w) * v[0] + (2 * y * z + 2 * x * w) * v[1] + (1 - 2 * x * x - 2 * y * y) * v[2]
        };
    }
}
