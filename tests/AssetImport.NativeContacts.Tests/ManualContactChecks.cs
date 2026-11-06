using System.Reflection;
using AssetImport;
using UnityEngine;

internal static class ManualContactChecks
{
    internal static void Run(Action<bool, string> check)
    {
        Transform Child(string name, Transform parent)
        { var t = new GameObject(name).transform; t.SetParent(parent, false); return t; }
        var host = new GameObject("manual test"); var model = Child("model", host.transform);
        var helper = Child("custom part A", model); var weighted = Child("unnamed weighted bone", helper);
        var nested = Child("nested part", weighted); var nestedBone = Child("other weighted bone", nested);
        var outside = Child("another part", model); var arm = Child("obstacle", model); arm.position = new(.3f, 0, 0);
        var a = host.AddComponent<DynamicBone>(); a.m_Root = helper;
        var b = host.AddComponent<DynamicBone>(); b.m_Root = nested;
        var c = host.AddComponent<DynamicBone>(); c.m_Root = outside;
        var environment = host.AddComponent<AssetPhysicsEnvironment>(); environment.Chains = [a, b, c];
        environment.Settings.NativeBreastContacts = true;
        var manual = new NativeContactShape { Bone = weighted, Radius = .04f, Center = new(.01f, .02f, .03f), HasReferencePose = true,
            ReferenceCenter = new(.01f, .02f, .03f), SourceBody = "source body" };
        var nestedShape = new NativeContactShape { Bone = nestedBone, Radius = .02f };
        var automatic = new NativeContactShape { Bone = outside, Radius = .03f };
        var definition = new NativeContactDefinition { Model = model, Breasts = [automatic], ManualCandidates = [nestedShape, manual, automatic],
            Obstacles = [new() { Bone = arm, Radius = .05f }] };
        NativeContactController.Attach(host, definition); var controller = host.GetComponent<NativeContactController>();
        check(controller.ContactShapes.Length == 1 && controller.ContactShapes[0].Bone == outside, "old scenes retain automatic recognition only");
        var draft = environment.Settings.Copy(); var choice = draft.EditRoot(helper.name);
        choice.BreastRole = 1; choice.BreastRadiusScale = 2.5f; choice.Mode = 1; choice.ForceScale = 2; choice.ShapeRetention = .4f;
        draft.EditRoot(outside.name).BreastRole = 2;
        var preview = controller.Preview(draft);
        check(preview.Length == 1 && preview[0].Bone == weighted && preview[0].Selection == "manual", "explicit root marking bypasses names but does not capture independent nested chains");
        check(Math.Abs(preview[0].Radius - .1f) < 1e-6f && preview[0].Center.x == manual.Center.x && preview[0].ReferenceCenter.x == manual.ReferenceCenter.x,
            "radius editing preserves the original body center and reference pose");
        check(controller.EligibleCount == 1 && controller.ContactShapes[0].Bone == outside && manual.Radius == .04f,
            "preview neither changes the live selection nor mutates source geometry");
        var original = weighted.position;
        environment.Settings = draft.Copy(); controller.ResetContacts(); DynamicBone_Ver02.Output = new(.025f, 0, 0);
        controller.Tick(1f / 60);
        check((weighted.position - original - DynamicBone_Ver02.Output).magnitude < 1e-6f && a.enabled && a.m_Weight == 1,
            "marked chain uses the existing contact writeback while ordinary physics stays enabled");
        var native = DynamicBone_Ver02.Created[^1];
        check(Math.Abs(native.Patterns[0].Params[1].CollisionRadius - .1f) < 1e-6f, "edited radius reaches the actual native initialization parameters");
        check(controller.BuildDiagnosticReport().Contains("selection=manual; chain=custom part A; source=source body; radius=0.1"),
            "manual source, chain and radius are exported for diagnosis");
        environment.Settings.EditRoot(helper.name).BreastRole = 2; controller.ResetContacts();
        check((weighted.position - original).magnitude < 1e-6f && controller.EligibleCount == 0, "excluding a running chain restores its old pose before rebuilding the selection");
        int steps = DynamicBone_Ver02.Steps; controller.Tick(1f / 60);
        check(DynamicBone_Ver02.Steps == steps && a.enabled, "excluded chains cannot retain hidden contact solvers");
        environment.Settings.EditRoot(outside.name).BreastRole = 0; controller.ResetContacts();
        check(controller.EligibleCount == 1 && controller.ContactShapes[0].Bone == outside, "Auto restores original recognition after exclusion");

        var asset = new Asset(); PhysicsEnvironmentPersistence.Write(asset, draft);
        draft.EditRoot(helper.name).BreastRadiusScale = .5f;
        var loaded = PhysicsEnvironmentPersistence.Read(asset); var restored = loaded.ForRoot(helper.name);
        check(restored.BreastRole == 1 && restored.BreastRadiusScale == 2.5f && restored.Mode == 1 && restored.ForceScale == 2 && restored.ShapeRetention == .4f,
            "manual role and radius persist alongside independent motion settings");
        restored.BreastRole = 2;
        check(PhysicsEnvironmentPersistence.Read(asset).ForRoot(helper.name).BreastRole == 1, "scene copies do not share mutable breast settings");
        check(new PhysicsChainSettings { BreastRadiusScale = 0 }.Copy().BreastRadiusScale == 1 &&
            new PhysicsChainSettings { BreastRole = 99, BreastRadiusScale = float.NaN }.Copy().BreastRole == 0 &&
            new PhysicsChainSettings { BreastRadiusScale = float.PositiveInfinity }.Copy().BreastRadiusScale == 1 &&
            new PhysicsChainSettings { BreastRadiusScale = 999 }.Copy().BreastRadiusScale == 4,
            "missing and malformed settings use safe compatible defaults");
        check(typeof(PhysicsChainSettings).GetField("BreastRole")!.GetCustomAttribute<MessagePack.KeyAttribute>()!.Key == "BreastContactRole" &&
            typeof(PhysicsChainSettings).GetField("BreastRadiusScale")!.GetCustomAttribute<MessagePack.KeyAttribute>()!.Key == "BreastRadiusScale", "stable named save keys for the new settings");
        controller.ResetContacts(); DynamicBone_Ver02.Output = new();
    }
}
