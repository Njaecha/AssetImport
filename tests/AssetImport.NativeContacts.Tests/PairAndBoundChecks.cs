using AssetImport;
using UnityEngine;

internal static class PairAndBoundChecks
{
    internal static void Run(Action<bool, string> check)
    {
        Transform Child(string name, Transform parent, Vector3 position)
        { var t = new GameObject(name).transform; t.SetParent(parent, false); t.localPosition = position; return t; }
        var host = new GameObject("pair-test"); var model = Child("model", host.transform, new());
        var torso = Child("support", model, new(0, 1, 0));
        var carrier = Child("unweighted helper", torso, new());
        var bone = Child("deforming bone", carrier, new(.025f, 0, 0));
        var arm = Child("independent limb", torso, new(.25f, 0, 0));
        var child = Child("dependent volume", bone, new());
        const float radius = .032026f;
        var breast = new NativeContactShape { Bone = bone, Radius = radius, HasReferencePose = true, ReferenceCenter = bone.position };
        var support = new NativeContactShape { Bone = torso, Radius = .0497203f, Height = .165734f, Direction = 1,
            HasReferencePose = true, ReferenceCenter = torso.position, SourceBody = "support volume" };
        var limb = new NativeContactShape { Bone = arm, Radius = .03536f, HasReferencePose = true,
            ReferenceCenter = arm.position, SourceBody = "limb volume" };
        var separatedSupport = new NativeContactShape { Bone = torso, Radius = .04f, Center = new(0, 0, -.3f),
            HasReferencePose = true, ReferenceCenter = torso.position + new Vector3(0, 0, -.3f), SourceBody = "separated torso" };
        var dependent = new NativeContactShape { Bone = child, Radius = .01f, SourceBody = "dependent volume" };
        check(NativeContactPairPolicy.Exclusion(breast, support) == "ancestor-overlaps-reference-pose", "overlapping support is excluded through unweighted ancestors without matching model/bone names");
        check(NativeContactPairPolicy.Exclusion(breast, separatedSupport) == null, "separated torso volumes remain usable for later contact");
        check(NativeContactPairPolicy.Exclusion(breast, dependent) == "moves-with-breast", "a collider driven by the breast cannot push its own owner");
        arm.position = bone.position;
        check(NativeContactPairPolicy.Exclusion(breast, limb) == null, "arm already touching at activation is not blacklisted");
        bone.localPosition = new(.5f, 0, 0);
        check(NativeContactPairPolicy.Exclusion(breast, support) != null, "filter uses PMX reference pose rather than the pose used to activate it");
        bone.localPosition = new(.025f, 0, 0);
        var chain = host.AddComponent<DynamicBone>(); chain.m_Root = carrier;
        var environment = host.AddComponent<AssetPhysicsEnvironment>(); environment.Chains = [chain]; environment.Settings.NativeBreastContacts = true;
        var definition = new NativeContactDefinition { Model = model, Breasts = [breast], Obstacles = [support, limb, separatedSupport, dependent] };
        NativeContactController.Attach(host, definition); var controller = host.GetComponent<NativeContactController>();
        Vector3 original = bone.position;
        DynamicBone_Ver02.Output = new(.005f, 0, 0); DynamicBone_Ver02.AccumulateOutput = true;
        try
        {
            controller.Tick(1f / 60);
            var native = DynamicBone_Ver02.Created[^1];
            check(native.Colliders.Count == 2 && native.Colliders.All(c => c.transform.parent == arm || c.transform.localPosition.z < -.2f), "only the two eligible pairs are actually passed to the native component");
            // This double injects a growing offset, not a native collision.
            // Without feeding the bound back, the proxy crosses the hard limit.
            for (int i = 0; i < 3000; i++) controller.Tick(1f / 60);
            check(controller.Status.English.Contains("1 active") && (bone.position - original).magnitude <= radius + 1e-6f, "bounded writeback remains live through 3000 frames of injected accumulating displacement");
            check(native.ResetCalls > 100 && (native.LastResetBodyPosition - original).magnitude <= radius + 1e-6f, "native particle reset observes the same bounded position as the visible bone");
            var report = controller.BuildDiagnosticReport();
            check(report.Contains("result=ancestor-overlaps-reference-pose") && report.Contains("boundObstacles=2") && report.Contains("outputRebases="), "report preserves exclusion reasons and native bound feedback counts");
            check(chain.enabled && chain.m_Weight == 1, "ordinary simulation is not replaced");
            environment.Settings.NativeBreastContacts = false; controller.Tick(1f / 60);
            check((bone.position - original).magnitude < 1e-6f, "switching off after repeated bounds restores original pose");
            DynamicBone_Ver02.AccumulateOutput = false; DynamicBone_Ver02.Output = new(radius * 5, 0, 0);
            environment.Settings.NativeBreastContacts = true; controller.ResetContacts(); controller.Tick(1f / 60);
            report = controller.BuildDiagnosticReport();
            check(report.Contains("state=failed") && report.Contains("hardLimit=") && report.Contains("probe=deforming bone") && (bone.position - original).magnitude < 1e-6f,
                "hard limit remains enforced with measured values and offending bone in the error");
            check(report.Contains("failure snapshot; Probe:") && report.Contains("rawDisplacement=") && report.Contains("boundObstacles=2"),
                "automatic report retains native measurements after the failed engine is disposed");
            definition.Obstacles = [support]; controller.ResetContacts();
            int instances = DynamicBone_Ver02.Instances; controller.Tick(1f / 60);
            check(DynamicBone_Ver02.Instances == instances && controller.BuildDiagnosticReport().Contains("state=no-contact-pairs"), "no independent pairs is explicit and does not initialize a useless native solver");
        }
        finally { DynamicBone_Ver02.AccumulateOutput = false; DynamicBone_Ver02.Output = new(); controller.ResetContacts(); }
    }
}
