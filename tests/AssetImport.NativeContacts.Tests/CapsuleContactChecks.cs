using AssetImport;
using UnityEngine;

internal static class CapsuleContactChecks
{
    internal static void Run(Action<bool, string> check)
    {
        Geometry(check);
        Settings(check);
        Adapter(check);
    }

    private static void Geometry(Action<bool, string> check)
    {
        float Distance(Vector3 p, Vector3 q, Vector3 r, Vector3 s)
        { NativeContactGeometry.Closest(p, q, r, s, out var a, out var b); return (a - b).magnitude; }
        check(Math.Abs(Distance(new(), new(), new(0, 1, 0), new(0, 2, 0)) - 1) < 1e-6f, "point-segment endpoint distance");
        check(Math.Abs(Distance(new(), new(0, 2, 0), new(1, 1, 0), new(1, 3, 0)) - 1) < 1e-6f, "parallel overlapping segments");
        check(Math.Abs(Distance(new(), new(0, 1, 0), new(0, 3, 0), new(0, 2, 0)) - 1) < 1e-6f, "opposite collinear separated segments");
        check(Distance(new(-1, 0, 0), new(1, 0, 0), new(0, -1, 0), new(0, 1, 0)) < 1e-6f, "crossing segments");
        check(Distance(new(-1, 0, 0), new(1, 0, 0), new(-1, -.0001f, 0), new(1, .0001f, 0)) < 1e-6f, "nearly parallel crossing segments retain their interior solution");
        check(Math.Abs(Distance(new(), new(), new(1, 2, 2), new(1, 2, 2)) - 3) < 1e-6f, "two point degeneracy");
        check(NativeContactGeometry.HalfAxis(.1f, .1f, 1, Quaternion.identity).magnitude == 0, "short capsule becomes sphere");
        var axis = NativeContactGeometry.HalfAxis(.1f, 1, 1, Quaternion.Euler(0, 0, 90));
        check((axis - new Vector3(-.4f, 0, 0)).magnitude < 1e-6f, "capsule rotation changes the contact axis");

        // Independent convex one-dimensional minimization: project a point on
        // segment 1 to segment 2, then minimize the distance along segment 1.
        // This oracle does not use the production simultaneous quadratic solve.
        var random = new Random(14029);
        Vector3 V() => new((float)random.NextDouble() * 4 - 2, (float)random.NextDouble() * 4 - 2, (float)random.NextDouble() * 4 - 2);
        double Dot(Vector3 a, Vector3 b) => (double)a.x * b.x + (double)a.y * b.y + (double)a.z * b.z;
        for (int i = 0; i < 200; i++)
        {
            var p = V(); var q = V(); var r = V(); var s = V(); var v = s - r;
            double At(double t)
            {
                var a = p + (q - p) * (float)t;
                double u = Math.Clamp(Dot(a - r, v) / Dot(v, v), 0, 1);
                return (a - r - v * (float)u).magnitude;
            }
            double low = 0, high = 1;
            for (int k = 0; k < 64; k++)
            { double a = (2 * low + high) / 3, b = (low + 2 * high) / 3; if (At(a) < At(b)) high = b; else low = a; }
            double expected = Math.Min(At((low + high) / 2), Math.Min(At(0), At(1)));
            float distance = Distance(p, q, r, s);
            check(Math.Abs(distance - expected) < 2e-5 && Math.Abs(distance - Distance(s, r, q, p)) < 2e-5,
                "segment closest points match independent minimization and reversed order " + i);
        }
    }

    private static void Settings(Action<bool, string> check)
    {
        var host = new GameObject("mirrored capsule test"); var model = Child("model", host.transform);
        var right = Child("right endpoint", model); right.localPosition = new(-.4f, 0, 0);
        var left = Child("left endpoint", model); left.localPosition = new(.4f, 0, 0);
        var arm = Child("arm", model);
        var a = host.AddComponent<DynamicBone>(); a.m_Root = right;
        var b = host.AddComponent<DynamicBone>(); b.m_Root = left;
        NativeContactShape Shape(Transform t) => new() { Bone = t, Radius = .1f, HasReferencePose = true, ReferenceCenter = t.localPosition };
        var definition = new NativeContactDefinition { Model = model, Breasts = [Shape(right), Shape(left)] };
        var settings = new PhysicsEnvironmentSettings();
        var original = NativeContactSelection.Resolve(definition, [a, b], settings);
        check(original.Length == 2 && original.All(s => !s.Capsule && s.Height == 0), "existing scenes remain spherical with no capsule helpers");
        foreach (var t in new[] { right, left })
        {
            var pref = settings.EditRoot(t.name);
            pref.BreastCapsule = true; pref.BreastLength = .8f; pref.BreastMirrorX = true;
            pref.BreastOffsetX = -.1f; pref.BreastOffsetY = .02f; pref.BreastOffsetZ = -.03f;
            pref.BreastRotationX = 23; pref.BreastRotationY = 31; pref.BreastRotationZ = 47;
        }
        NativeContactShape[] Resolve() => NativeContactSelection.Resolve(definition, [a, b], settings);
        var shapes = Resolve(); var r = shapes.Single(s => s.Bone == right); var l = shapes.Single(s => s.Bone == left);
        check(Math.Abs(r.ReferenceCenter.x + .3f) < 1e-6f && Math.Abs(l.ReferenceCenter.x - .3f) < 1e-6f &&
            r.Center.y == l.Center.y && r.Center.z == l.Center.z, "negative mirrored X moves both sides inward without flipping Y/Z translation");
        var rh = NativeContactGeometry.ContactHalfAxis(r); var lh = NativeContactGeometry.ContactHalfAxis(l);
        check((rh - new Vector3(-lh.x, lh.y, lh.z)).magnitude < 1e-6f, "Euler Y/Z reflection mirrors the capsule axis across model X");
        right.localPosition = new(3, 0, 0); model.localScale = new(2, 2, 2); model.localRotation = Quaternion.Euler(0, 180, 0);
        var moved = Resolve().Single(s => s.Bone == right);
        check((moved.Center - r.Center).magnitude < 1e-6f && (NativeContactGeometry.ContactHalfAxis(moved) - rh).magnitude < 1e-6f,
            "mirror side comes from original reference geometry, not animated position or model rotation/scale");
        settings.EditRoot(right.name).BreastMirrorX = false;
        check(Resolve().Single(s => s.Bone == right).Center.x == -.1f && Resolve().Single(s => s.Bone == left).Center.x == l.Center.x,
            "one side can be edited independently without changing the other");
        settings.EditRoot(right.name).BreastLength = .01f;
        check(Math.Abs(Resolve().Single(s => s.Bone == right).Height - .2f) < 1e-6f, "effective capsule length cannot be smaller than its diameter");
        var asset = new Asset(); PhysicsEnvironmentPersistence.Write(asset, settings);
        settings.EditRoot(left.name).BreastLength = 3;
        var saved = PhysicsEnvironmentPersistence.Read(asset).ForRoot(left.name);
        check(saved.BreastCapsule && saved.BreastMirrorX && saved.BreastLength == .8f && saved.BreastRotationX == 23 &&
            saved.BreastRotationY == 31 && saved.BreastRotationZ == 47, "capsule, mirror, length and rotations survive persistence with independent copies");
        var clean = new PhysicsChainSettings { BreastLength = float.PositiveInfinity, BreastRotationX = float.NaN,
            BreastRotationY = 400, BreastRotationZ = -400 }.Copy();
        check(clean.BreastLength == 0 && clean.BreastRotationX == 0 && clean.BreastRotationY == 180 && clean.BreastRotationZ == -180,
            "nonfinite or out-of-range capsule settings are sanitized");
        var contact = new NativeContactShape { Bone = left, Radius = .1f, Capsule = true, Height = 1,
            Direction = 1, Rotation = Quaternion.identity, HasReferencePose = true };
        var obstacle = new NativeContactShape { Bone = arm, Radius = .05f, HasReferencePose = true, ReferenceCenter = new(.08f, .4f, 0) };
        check(NativeContactPairPolicy.ReferenceGap(contact, obstacle) < 0 && NativeContactPairPolicy.Exclusion(contact, obstacle) == "overlaps-reference-pose",
            "reference overlap filtering includes capsule ends, not only its center sphere");
        contact.Capsule = false;
        check(NativeContactPairPolicy.Exclusion(contact, obstacle) == null, "central sphere alone does not cover the end-only overlap");
    }

    private static void Adapter(Action<bool, string> check)
    {
        var owner = new GameObject("scaled contact owner"); owner.transform.localScale = new(2, 2, 2);
        owner.transform.localRotation = Quaternion.Euler(15, 25, 35);
        var arm = Child("body capsule", owner.transform).gameObject.AddComponent<DynamicBoneCollider>();
        arm.m_Radius = .05f; arm.m_Height = .6f; arm.m_Direction = DynamicBoneCollider.Direction.Y;
        arm.transform.position = new(.08f, .5f, 0); arm.transform.rotation = Quaternion.Euler(0, 0, 80);
        var half = new Vector3(0, .4f, 0); float radius = .1f;
        using (var adapter = new NativeContactCapsule(owner.transform, [arm]))
        {
            for (int i = 0; i < 24; i++)
            {
                var center = new Vector3(i * .02f, 0, -.1f);
                arm.transform.rotation = Quaternion.Euler(i * 7, i * 11, i * 13);
                arm.transform.position += new Vector3(.001f, -.005f, .002f);
                var axis = Quaternion.Euler(i * 3, i * 5, i * 9) * half;
                adapter.Update(center, axis); var proxy = adapter.Colliders.Single();
                float scale = Math.Abs(arm.transform.lossyScale.x);
                var bodyHalf = NativeContactGeometry.HalfAxis(arm.m_Radius * scale, arm.m_Height * scale, (int)arm.m_Direction, arm.transform.rotation);
                float expected = NativeContactGeometry.Gap(center, axis, radius, arm.transform.position, bodyHalf, arm.m_Radius * scale);
                float mapped = NativeContactPairPolicy.Gap(center, radius, proxy.transform.position, proxy.transform.rotation,
                    proxy.m_Radius, proxy.m_Height, (int)proxy.m_Direction);
                check(Math.Abs(expected - mapped) < 2e-6f && Math.Abs(proxy.transform.lossyScale.x - 1) < 1e-6f &&
                    proxy.m_Radius == .1f && proxy.m_Height == 1.2f,
                    "capsule mapping preserves the sampled gap while arms move/rotate and applies scale once " + i);
            }
            check(adapter.Host.activeInHierarchy && adapter.Colliders.Count == 1, "capsule adapter has active obstacle geometry but no additional native solver");
        }
        var logs = new List<string>(); int instances = DynamicBone_Ver02.Instances;
        using (var engine = new NativeContactEngine(owner.transform, new(), Quaternion.identity, radius, [arm], logs.Add, half))
        {
            check(DynamicBone_Ver02.Instances == instances + 1 && engine.ParticleCount == 2 && engine.ColliderCount == 1,
                "one capsule retains one native engine with one anchor and one movable particle");
            check(logs.Any(s => s.Contains("selfCheck.capsuleEndDisplacement=")), "capsule activation checks end contact response separately");
            DynamicBone_Ver02.Output = new(.02f, 0, 0);
            check((engine.Step(new(), Quaternion.identity, 1f / 60) - DynamicBone_Ver02.Output).magnitude < 1e-6f,
                "capsule output is one shared translation, not independently returning sample spheres");
            var helper = engine.CapsuleObstacles; engine.Dispose();
            check(!helper.activeInHierarchy && helper.Destroyed && engine.Host.Destroyed, "capsule disposal deactivates and destroys owned helpers");
        }
        DynamicBone_Ver02.Output = new();
        using (var sphere = new NativeContactEngine(owner.transform, new(), Quaternion.identity, radius, [arm]))
            check(sphere.CapsuleObstacles == null, "switching back to sphere needs no capsule helper objects");
        // Force the second calibration to fail, after capsule helpers exist.
        try
        {
            using var broken = new NativeContactEngine(owner.transform, new(), Quaternion.identity, radius, [arm],
                line => { if (line.StartsWith("selfCheck.sphereDisplacement=")) DynamicBone_Ver02.FailCalibration = true; }, half);
            check(false, "capsule calibration failure must reject the engine");
        }
        catch (InvalidOperationException e)
        { check(e.Message.Contains("capsule end self-check"), "capsule-specific failure retains its cause"); }
        finally { DynamicBone_Ver02.FailCalibration = false; }
        check(!owner.transform.Children.Any(t => t.name == "AI_BreastCapsuleObstacles" && t.gameObject.activeInHierarchy),
            "failed capsule calibration leaves no active helper colliders");

        // Exercise the production controller so both the engine and clone-safe
        // ownership list have to clean up on feature disable.
        var host = new GameObject("capsule controller"); var model = Child("model", host.transform);
        var breast = Child("breast", model); var body = Child("arm", model); body.localPosition = new(.2f, .4f, 0);
        var chain = host.AddComponent<DynamicBone>(); chain.m_Root = breast;
        var env = host.AddComponent<AssetPhysicsEnvironment>(); env.Chains = [chain]; env.Settings.NativeBreastContacts = true;
        var pref = env.Settings.EditRoot(breast.name); pref.BreastCapsule = true; pref.BreastLength = 1;
        NativeContactController.Attach(host, new NativeContactDefinition { Model = model,
            Breasts = [new() { Bone = breast, Radius = .1f }], Obstacles = [new() { Bone = body, Radius = .1f }] });
        var controller = host.GetComponent<NativeContactController>(); instances = DynamicBone_Ver02.Instances;
        DynamicBone_Ver02.Output = new(.02f, 0, 0); controller.Tick(1f / 60);
        string report = controller.BuildDiagnosticReport();
        check(DynamicBone_Ver02.Instances == instances + 1 && report.Contains("shape=capsule; length=1") && report.Contains("selfCheck.capsuleEndDisplacement="),
            "controller uses the configured capsule and reports its geometry and calibration");
        check(Math.Abs(breast.position.x - .02f) < 1e-6f, "controller writes capsule response to the actual endpoint");
        env.Settings.NativeBreastContacts = false; controller.Tick(1f / 60);
        check(breast.position.magnitude < 1e-6f && host.GetComponentsInChildren<DynamicBoneCollider>(false).Length == 0,
            "disabling capsule contacts restores the endpoint and removes active owned colliders");
        DynamicBone_Ver02.Output = new(); controller.ResetContacts();
    }

    private static Transform Child(string name, Transform parent)
    { var child = new GameObject(name).transform; child.SetParent(parent, false); return child; }
}
