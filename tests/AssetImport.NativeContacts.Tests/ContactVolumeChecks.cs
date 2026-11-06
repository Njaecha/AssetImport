using AssetImport;
using UnityEngine;

internal static class ContactVolumeChecks
{
    internal static void Run(Action<bool, string> check)
    {
        Transform Child(string name, Transform parent)
        { var t = new GameObject(name).transform; t.SetParent(parent, false); return t; }
        var host = new GameObject("contact volume test"); var model = Child("model", host.transform);
        var root = Child("root", model); var part = Child("deforming root", root);
        var tip = Child("dense helper", part); var nested = Child("independent chain", part);
        var arm = Child("arm", model); arm.position = new(.8f, 0, 0);
        var chain = host.AddComponent<DynamicBone>(); chain.m_Root = root;
        var nestedChain = host.AddComponent<DynamicBone>(); nestedChain.m_Root = nested;
        var env = host.AddComponent<AssetPhysicsEnvironment>(); env.Chains = [chain, nestedChain];
        var mainShape = new NativeContactShape { Bone = part, Radius = .001f, Center = new(.01f, 0, 0),
            HasReferencePose = true, ReferenceCenter = new(.05f, 0, 0), SourceBody = "tiny PMX body",
            HasMeshBounds = true, MeshMin = new(.1f, -.1f, 0), MeshMax = new(.2f, 0, .1f) };
        var tipShape = new NativeContactShape { Bone = tip, Radius = .001f,
            HasReferencePose = true, HasMeshBounds = true, MeshMin = new(.2f, 0, .1f), MeshMax = new(.3f, .1f, .2f) };
        var independent = new NativeContactShape { Bone = nested, Radius = .01f, HasMeshBounds = true,
            MeshMin = new(10, 10, 10), MeshMax = new(20, 20, 20) };
        var definition = new NativeContactDefinition { Model = model, ManualCandidates = [tipShape, mainShape, independent],
            Obstacles = [new() { Bone = arm, Radius = .05f }] };
        var settings = new PhysicsEnvironmentSettings { NativeBreastContacts = true };
        var pref = settings.EditRoot(root.name); pref.BreastRole = 1; pref.BreastRadiusScale = 4;
        var legacy = NativeContactSelection.Resolve(definition, env.Chains, settings);
        check(legacy.Length == 1 && legacy[0].Bone == tip && Math.Abs(legacy[0].Radius - .004f) < 1e-6f, "source mode retains saved endpoint size while removing intermediate probes");
        pref.BreastMeshVolume = true; pref.BreastRadiusScale = 1;
        var fit = NativeContactSelection.Resolve(definition, env.Chains, settings);
        check(fit.Length == 1 && fit[0].Bone == tip && Math.Abs(fit[0].Radius - .05f) < 1e-6f,
            "mesh fitting sizes only the endpoint and cannot reintroduce a root probe");
        check((fit[0].ReferenceCenter - new Vector3(.25f, .05f, .15f)).magnitude < 1e-6f &&
            (fit[0].Center - new Vector3(.25f, .05f, .15f)).magnitude < 1e-6f,
            "mesh fit moves both reference and local centers to the visible region");
        check(fit[0].Radius < 1 && fit[0].SourceBody == "Endpoint weighted mesh region", "independent nested chain cannot inflate the endpoint volume");
        part.position = new(5, 6, 7);
        var posed = NativeContactSelection.Resolve(definition, env.Chains, settings);
        check((posed[0].Center - fit[0].Center).magnitude < 1e-6f, "fitting uses source mesh bounds rather than an animated or scaled pose");
        pref.BreastRadiusOverride = .24f; pref.BreastOffsetX = .03f; pref.BreastOffsetY = -.02f; pref.BreastOffsetZ = .04f;
        pref.BreastRadiusScale = 4;
        var custom = NativeContactSelection.Resolve(definition, env.Chains, settings);
        check(custom.Length == 1 && custom[0].Radius == .24f &&
            (custom[0].Center - fit[0].Center - new Vector3(.03f, -.02f, .04f)).magnitude < 1e-6f &&
            (custom[0].ReferenceCenter - fit[0].ReferenceCenter - new Vector3(.03f, -.02f, .04f)).magnitude < 1e-6f,
            "absolute radius bypasses the 400 percent limit and center offset changes live/reference geometry equally");
        check(mainShape.Radius == .001f && mainShape.Center.x == .01f && tipShape.Bone == tip, "fitting and editing do not mutate authored source records");
        env.Settings = settings.Copy(); NativeContactController.Attach(host, definition);
        var controller = host.GetComponent<NativeContactController>(); var preview = controller.Preview(settings);
        model.localScale = new(2, 2, 2); var original = part.position; var originalTip = tip.position;
        DynamicBone_Ver02.Output = new(.04f, 0, 0); controller.Tick(1f / 60);
        var native = DynamicBone_Ver02.Created[^1];
        check(preview.Length == controller.ContactShapes.Length && Math.Abs(native.Patterns[0].Params[1].CollisionRadius - .48f) < 1e-6f,
            "preview and actual native solver share the custom radius with model scale applied exactly once");
        check((part.position - original).magnitude < 1e-5f &&
            (tip.position - originalTip - DynamicBone_Ver02.Output).magnitude < 1e-5f,
            "endpoint contact leaves the chain's parent transforms under ordinary physics control");
        check(controller.BuildDiagnosticReport().Contains("Endpoint weighted mesh region") &&
            controller.BuildDiagnosticReport().Contains("worldRadius=0.48"), "diagnostic report contains actual fitted geometry and scaled radius");
        controller.ResetContacts();
        check((part.position - original).magnitude < 1e-5f && (tip.position - originalTip).magnitude < 1e-5f,
            "reset restores parent and child poses after fitted contact");
        var asset = new Asset(); PhysicsEnvironmentPersistence.Write(asset, settings);
        var saved = PhysicsEnvironmentPersistence.Read(asset).ForRoot(root.name);
        check(saved.BreastMeshVolume && saved.BreastRadiusOverride == .24f && saved.BreastOffsetZ == .04f,
            "mesh fitting, absolute radius and center offsets survive scene persistence");
        pref.BreastMeshVolume = false; pref.BreastRadiusOverride = 0; pref.BreastOffsetX = pref.BreastOffsetY = pref.BreastOffsetZ = 0;
        check(NativeContactSelection.Resolve(definition, env.Chains, settings).Length == 1,
            "source volume mode cannot reintroduce intermediate spheres");
        mainShape.MeshMax = mainShape.MeshMin; tipShape.HasMeshBounds = false; pref.BreastMeshVolume = true;
        check(NativeContactSelection.Resolve(definition, env.Chains, settings).Length == 1,
            "missing or degenerate mesh regions retain valid endpoint source volumes");
        var clean = new PhysicsChainSettings { BreastRadiusOverride = float.NaN, BreastOffsetX = float.PositiveInfinity,
            BreastOffsetY = -99, BreastOffsetZ = 99 }.Copy();
        check(clean.BreastRadiusOverride == 0 && clean.BreastOffsetX == 0 && clean.BreastOffsetY == -2 && clean.BreastOffsetZ == 2,
            "malformed absolute radius and offsets are sanitized at load");

        // A selected child with no response must inherit its parent's offset,
        // even in source mode where the two probes remain separate.
        definition.Breasts = [mainShape, tipShape]; env.Chains = [chain, nestedChain]; nestedChain.m_Root = tip; nestedChain.enabled = false;
        env.Settings = new PhysicsEnvironmentSettings { NativeBreastContacts = true };
        controller.ResetContacts(); original = part.position; originalTip = tip.position;
        mainShape.Radius = tipShape.Radius = .1f;
        controller.Tick(1f / 60);
        check((tip.position - originalTip - DynamicBone_Ver02.Output).magnitude < 1e-5f,
            "an inactive child probe does not pin the mesh when the parent responds");
        controller.ResetContacts();
        check((part.position - original).magnitude < 1e-5f && (tip.position - originalTip).magnitude < 1e-5f,
            "inherited parent response restores cleanly in source mode");
        DynamicBone_Ver02.Output = new();
    }
}
