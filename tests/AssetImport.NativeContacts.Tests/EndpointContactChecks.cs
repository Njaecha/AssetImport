using AssetImport;
using UnityEngine;

internal static class EndpointContactChecks
{
    internal static void Run(Action<bool, string> check)
    {
        Transform Child(string name, Transform parent, Vector3 pos)
        { var t = new GameObject(name).transform; t.SetParent(parent, false); t.localPosition = pos; return t; }
        var host = new GameObject("endpoint test"); var model = Child("model", host.transform, new());
        var root = Child("chain", model, new()); var joint = root;
        var candidates = new List<NativeContactShape>();
        for (int i = 0; i < 6; i++)
        {
            joint = Child("joint " + i, joint, new(0, 0, .025f));
            var follower = Child("weighted follower " + i, joint, new());
            candidates.Add(new NativeContactShape { Bone = follower, Driver = joint, Radius = .04f,
                HasReferencePose = true, ReferenceCenter = follower.position, SourceBody = "body " + i });
        }
        var branch = Child("short independent branch", root, new(.12f, 0, 0));
        var branchTip = Child("short branch tip", branch, new(0, 0, .03f));
        var branchShape = new NativeContactShape { Bone = branchTip, Radius = .04f, HasReferencePose = true, ReferenceCenter = branchTip.position };
        var arm = Child("independent arm", model, new(.3f, 0, .15f));
        var chest = Child("sibling support", model, new(0, 0, .15f));
        var chain = host.AddComponent<DynamicBone>(); chain.m_Root = root;
        var environment = host.AddComponent<AssetPhysicsEnvironment>(); environment.Chains = [chain];
        environment.Settings.NativeBreastContacts = true;
        var pref = environment.Settings.EditRoot(root.name); pref.BreastRole = 1;
        var limb = new NativeContactShape { Bone = arm, Radius = .025f, HasReferencePose = true, ReferenceCenter = arm.position, SourceBody = "arm" };
        var support = new NativeContactShape { Bone = chest, Radius = .05f, HasReferencePose = true, ReferenceCenter = chest.position, SourceBody = "sibling support" };
        var siblingFollower = new NativeContactShape { Bone = candidates[1].Bone, Radius = .04f, HasReferencePose = true,
            ReferenceCenter = candidates[1].Bone.position, SourceBody = "same chain body" };
        var definition = new NativeContactDefinition { Model = model, ManualCandidates = candidates.ToArray(), Obstacles = [limb, support, siblingFollower] };
        var endpoints = NativeContactSelection.Resolve(definition, environment.Chains, environment.Settings);
        check(endpoints.Length == 1 && endpoints[0].Bone == candidates[5].Bone,
            "six separate weighted leaf followers produce only the logical driving chain endpoint");
        check(NativeContactPairPolicy.Exclusion(endpoints[0], support) == "overlaps-reference-pose",
            "overlapping sibling support is excluded without relying on breast names");
        check(NativeContactPairPolicy.Exclusion(endpoints[0], siblingFollower) == "same-physics-chain",
            "sibling deformation bones in the contact chain cannot form a feedback obstacle");
        check(NativeContactPairPolicy.Exclusion(endpoints[0], limb) == null,
            "separated independent arm remains available for contact");
        arm.position = candidates[5].Bone.position;
        check(NativeContactPairPolicy.Exclusion(endpoints[0], limb) == null &&
            NativeContactController.SurfaceGap(endpoints[0].Bone.position, .04f, limb) < 0,
            "activating while the arm overlaps does not replace authored reference geometry or disable the arm");
        definition.ManualCandidates = candidates.Concat([branchShape]).ToArray();
        endpoints = NativeContactSelection.Resolve(definition, environment.Chains, environment.Settings);
        check(endpoints.Length == 2 && endpoints.Any(s => s.Bone == branchTip),
            "a shorter independent branch keeps its endpoint instead of selecting only the deepest bone in the whole chain");
        definition.ManualCandidates = candidates.ToArray();
        NativeContactController.Attach(host, definition); var controller = host.GetComponent<NativeContactController>();
        var originals = candidates.Select(s => s.Bone.position).ToArray();
        arm.position = new(.3f, 0, .15f); DynamicBone_Ver02.Output = new();
        controller.Tick(1f / 60);
        var native = DynamicBone_Ver02.Created[^1];
        check(native.Colliders.Count == 1 && native.Colliders[0].transform.parent == arm,
            "native collision list contains the arm and no internal support or chain-owned bodies");
        check(candidates.Select((s, i) => (s.Bone.position - originals[i]).magnitude).All(d => d < 1e-6f),
            "with no injected arm response the entire ordinary pose remains unchanged");
        arm.position = originals[5]; DynamicBone_Ver02.Output = new(.02f, 0, 0); controller.Tick(1f / 60);
        check((candidates[5].Bone.position - originals[5] - DynamicBone_Ver02.Output).magnitude < 1e-6f &&
            candidates.Take(5).Select((s, i) => (s.Bone.position - originals[i]).magnitude).All(d => d < 1e-6f),
            "arm response is written only to the endpoint, not all intermediate weighted leaves");
        var report = controller.BuildDiagnosticReport();
        check(report.Contains("branch-endpoints") && report.Contains("endpointNode=joint 5") && report.Contains("liveGap=") &&
            report.Contains("result=overlaps-reference-pose"), "report distinguishes endpoint identity, rejected internal support and live arm separation");
        check(report.Contains("endpointNode=joint 0; chain=chain; result=intermediate") &&
            report.Contains("endpointNode=joint 5; chain=chain; result=endpoint"),
            "diagnostics expose kept and discarded append followers for rigs unavailable locally");
        controller.ResetContacts();
        check(candidates.Select((s, i) => (s.Bone.position - originals[i]).magnitude).All(d => d < 1e-6f), "reset restores the terminal pose without changing intermediate bones");
        DynamicBone_Ver02.Output = new();
    }
}
