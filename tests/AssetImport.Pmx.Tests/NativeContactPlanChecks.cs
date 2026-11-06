using AssetImport;

internal static class NativeContactPlanChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var model = new PmxModel {
            Bones = [new() { Name = "上半身", Parent = -1 }, new() { Name = "左胸", Parent = 0 },
                new() { Name = "腕", Parent = 0 }, new() { Name = "左胸先", Parent = 1 }, new() { Name = "breast_ribbon", Parent = 0 }],
            Vertices = [new() { Bones = [1, 3, 4], Weights = [.5f, .3f, .2f] }],
            RigidBodyData = [Body(3, 1), Body(1, 1), Body(2, 0), Body(3, 0), Body(4, 1)] };
        var plan = PmxNativeContactPlan.Build(model);
        check(plan.Breasts.Select(s => s.Bone).SequenceEqual(new[] { 1, 3 }), "weighted breast bodies sorted parent before child, including leaf bone");
        check(plan.Obstacles.Count == 1 && plan.Obstacles[0].Bone == 2, "stable animated arm retained and dynamic descendants excluded");
        check(plan.Breasts.All(s => Math.Abs(s.Radius - .0416f) < 1e-6f), "contact radius uses existing PMX coordinate conversion");
        check(plan.ManualCandidates.Select(s => s.Bone).Order().SequenceEqual(new[] { 1, 3, 4 }), "manual candidates include weighted dynamic bones regardless of their names");
        var report = plan.Describe(model);
        check(report.Contains("result=selected") && report.Contains("result=not-dynamic") && report.Contains("bone=breast_ribbon"), "recognition report covers selected, rejected and unknown-name source bodies");
        model.Bones[1].Position = new(2, 8, 3);
        model.RigidBodyData[1].Position = new(3, 10, 5);
        model.RigidBodyData[2].Position = new(-4, 12, 6);
        var import = new Import(model);
        // Binding must retain authored coordinates, regardless of the live pose.
        import.Bone(1).localPosition = new UnityEngine.Vector3(20, 30, 40);
        var definition = import.BuildNativeContacts();
        var boundBreast = definition.Breasts.Single(s => s.Bone == import.Bone(1));
        var boundArm = definition.Obstacles.Single(s => s.Bone == import.Bone(2));
        check(boundBreast.HasReferencePose && boundArm.HasReferencePose,
            "PMX binding supplies reference geometry for both probes and obstacles");
        check(Math.Abs(boundBreast.ReferenceCenter.x + 3 * .0832f) < 1e-6f &&
            Math.Abs(boundBreast.ReferenceCenter.y - 10 * .0832f) < 1e-6f &&
            Math.Abs(boundBreast.ReferenceCenter.z + 5 * .0832f) < 1e-6f &&
            Math.Abs(boundArm.ReferenceCenter.x - 4 * .0832f) < 1e-6f,
            "reference centers use PMX world positions and handedness instead of animated transforms");
        check(Math.Abs(boundBreast.Center.x + .0832f) < 1e-6f &&
            Math.Abs(boundBreast.ReferenceCenter.x - boundBreast.Center.x) > .1f,
            "source reference center remains distinct from bone-local collider offset");
        var fallbackModel = new PmxModel { Bones = [new() { Name = "helper", Parent = -1 }, new() { Name = "part", Parent = 0, Position = new(0, 2, 0) }],
            Vertices = [new() { Bones = [1], Weights = [1f] }], RigidBodyData = [] };
        var fallbackPlan = PmxNativeContactPlan.Build(fallbackModel);
        check(fallbackPlan.Breasts.Count == 0 && fallbackPlan.ManualCandidates.Count == 1 && fallbackPlan.ManualCandidates[0].Index == -1 &&
            Math.Abs(fallbackPlan.ManualCandidates[0].Radius - .0416f) < 1e-6f, "body-less weighted bone receives a manual-only sphere based on source bone spacing");
        var fallbackImport = new Import(fallbackModel); var fallbackDefinition = fallbackImport.BuildNativeContacts();
        check(fallbackDefinition.ManualCandidates.Length == 1 && fallbackDefinition.ManualCandidates[0].HasReferencePose &&
            Math.Abs(fallbackDefinition.ManualCandidates[0].ReferenceCenter.y - .1664f) < 1e-6f && fallbackDefinition.ManualCandidates[0].Center.y == 0,
            "manual fallback binds its source reference pose and bone-local center without needing a rigid-body record");
        fallbackModel.Vertices = [
            new() { Position = new(1, 2, 3), Bones = [1], Weights = [.5f] },
            new() { Position = new(3, 6, 7), Bones = [1], Weights = [.25f] },
            new() { Position = new(100, 100, 100), Bones = [1], Weights = [.02f] },
            new() { Position = new(float.NaN, 0, 0), Bones = [1], Weights = [1f] },
            new() { Position = new(100, 100, 100), Bones = [1], Weights = [float.NaN] }];
        var boundsPlan = PmxNativeContactPlan.Build(fallbackModel);
        check(boundsPlan.MeshBounds[1].Min.X == 1 && boundsPlan.MeshBounds[1].Max.Z == 7 && boundsPlan.MeshBounds[1].Max.Y == 6,
            "mesh volume bounds retain significant skin weights and reject weak tails/nonfinite vertices");
        var meshImport = new Import(fallbackModel);
        meshImport.Bone(1).localPosition = new(30, 40, 50);
        var meshShape = meshImport.BuildNativeContacts().ManualCandidates.Single();
        check(meshShape.HasMeshBounds && Math.Abs(meshShape.MeshMin.x + 3 * .0832f) < 1e-6f &&
            Math.Abs(meshShape.MeshMax.x + .0832f) < 1e-6f && Math.Abs(meshShape.MeshMin.z + 7 * .0832f) < 1e-6f &&
            Math.Abs(meshShape.MeshMax.y - 6 * .0832f) < 1e-6f,
            "mesh bounds bind in stable import units with correct min/max after handedness conversion");
        var followers = new PmxModel { Bones = [
            new() { Name = "root", Parent = -1 }, new() { Name = "joint", Parent = 0 },
            new() { Name = "append", Parent = 0, Flags = 0x300, GrantParent = 1, GrantWeight = 1 },
            new() { Name = "weighted leaf", Parent = 0, Flags = 0x300, GrantParent = 2, GrantWeight = 1, Position = new(0, 2, 0) }],
            Vertices = [new() { Bones = [3], Weights = [1f] }], RigidBodyData = [] };
        var appendImport = new Import(followers);
        var appendDefinition = appendImport.BuildNativeContacts();
        var follower = appendDefinition.ManualCandidates.Single();
        check(follower.Bone == appendImport.Bone(3) && follower.Driver == appendImport.Bone(1) &&
            Math.Abs(follower.ReferenceCenter.y - .1664f) < 1e-6f,
            "transitive PMX append dependencies identify the logical endpoint driver without changing weighted output or reference geometry");
        check(appendDefinition.PositionGrants.Length == 2 && appendDefinition.PositionGrants[0].Bone == appendImport.Bone(2) &&
            appendDefinition.PositionGrants[1].Source == appendImport.Bone(2), "contact position grants bind in dependency order");
        followers.Bones[2].GrantWeight = .5f; followers.Bones[3].GrantWeight = -.25f;
        appendDefinition = appendImport.BuildNativeContacts();
        check(appendDefinition.PositionGrants[0].Rate == .5f && appendDefinition.PositionGrants[1].Rate == -.25f,
            "contact propagation retains authored fractional and negative grant rates");
        followers.Bones[2].Flags = 0x100;
        check(appendImport.BuildNativeContacts().PositionGrants.All(g => g.Bone != appendImport.Bone(2)),
            "rotation-only grants do not manufacture positional deformation");
        followers.Bones[2].Flags = 0x380;
        check(appendImport.BuildNativeContacts().PositionGrants.All(g => g.Bone != appendImport.Bone(2)),
            "unsupported local grants do not acquire guessed contact propagation");
        followers.Bones[2].Flags = 0x300;
        followers.Bones[2].GrantParent = 3; followers.Bones[3].GrantParent = 1;
        appendDefinition = appendImport.BuildNativeContacts();
        check(appendDefinition.PositionGrants[0].Bone == appendImport.Bone(3) && appendDefinition.PositionGrants[1].Bone == appendImport.Bone(2),
            "forward-index grant sources are emitted before dependent followers");
        followers.Bones[2].GrantParent = 1; followers.Bones[3].GrantParent = 2;
        followers.Bones[3].GrantWeight = 0;
        check(appendImport.BuildNativeContacts().ManualCandidates.Single().Driver == appendImport.Bone(3),
            "zero-weight append operation does not collapse an independent contact branch");
        followers.Bones[3].GrantWeight = 1; followers.Bones[2].GrantParent = 3;
        check(appendImport.BuildNativeContacts().ManualCandidates.Single().Driver == appendImport.Bone(3),
            "disabled cyclic append operation retains the weighted bone as endpoint driver");
        check(appendImport.BuildNativeContacts().PositionGrants.Length == 0,
            "cyclic append operations cannot enter the contact propagation graph");
        followers.Bones[3].GrantParent = 99;
        check(appendImport.BuildNativeContacts().ManualCandidates.Single().Driver == appendImport.Bone(3),
            "out-of-range append parent cannot break optional contact metadata binding");
        model.Vertices = []; check(PmxNativeContactPlan.Build(model).Breasts.Count == 0, "unweighted helper bones cannot be selected as deforming breasts");
        check(PmxNativeContactPlan.Build(model).Describe(model).Contains("result=unweighted"), "unweighted rejection is diagnosed");
        model.RigidBodyData = [];
        check(PmxNativeContactPlan.Build(model).Describe(model).Contains("result=no-rigid-body"), "missing breast rigid body is diagnosed");
        foreach (string name in new[] { "左胸", "右乳", "左胸上", "Breast_L", "Right Breast 01", "bust.r" })
            check(PmxNativeContactPlan.IsBreast(name), "recognized breast name " + name);
        foreach (string name in new[] { "上半身", "breast ribbon", "breastplate", "胸飾り", "left arm" })
            check(!PmxNativeContactPlan.IsBreast(name), "accessory/body names excluded " + name);
        check(PmxNativeContactPlan.Build(null).Breasts.Count == 0, "non-PMX input has no native contact plan");
    }
    private static PmxModel.RigidBody Body(int bone, int mode) => new() {
        Bone = bone, Mode = mode, Name = "body", Shape = 0, Size = new(.5f, .5f, .5f), Group = 0, CollisionMask = 0 };

    internal static void Corpus(PmxModel model, Action<bool, string> check)
    {
        var plan = PmxNativeContactPlan.Build(model);
        check(plan.Breasts.Select(s => s.Bone).Distinct().Count() == plan.Breasts.Count, "native contact plan owns each breast once");
        foreach (var breast in plan.Breasts)
        {
            check(model.RigidBodyData[breast.Index].Mode != 0 && PmxNativeContactPlan.IsBreast(model.Bones[breast.Bone].Name), "real-model native breast retains its source dynamic body");
            check(float.IsFinite(breast.Radius) && breast.Radius > 0, "real-model native breast radius is finite and positive");
        }
        foreach (var obstacle in plan.Obstacles)
            check(model.RigidBodyData[obstacle.Index].Mode == 0 && float.IsFinite(obstacle.Radius) && obstacle.Radius > 0, "real-model native obstacle is static and finite");
        Console.WriteLine($"Native contact geometry: breasts={plan.Breasts.Count}; obstacles={plan.Obstacles.Count}. Native game simulation NOT run.");
    }
}
