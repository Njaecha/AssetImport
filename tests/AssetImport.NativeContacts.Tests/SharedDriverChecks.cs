using AssetImport;
using UnityEngine;

internal static class SharedDriverChecks
{
    internal static void Run(Action<bool, string> check)
    {
        Transform Child(string name, Transform parent, Vector3 pos)
        { var t = new GameObject(name).transform; t.SetParent(parent, false); t.localPosition = pos; return t; }
        var host = new GameObject("shared driver"); var model = Child("model", host.transform, new());
        model.localScale = new(2, 2, 2);
        model.localRotation = new(0, 0, MathF.Sin(.3f), MathF.Cos(.3f));
        var candidates = new List<NativeContactShape>(); var grants = new List<NativeContactGrant>();
        var chains = new List<DynamicBone>(); var groups = new List<Transform[]>();
        foreach (var side in new[] { "left", "right" })
        {
            var root = Child(side + " chain", model, new(side == "left" ? -.2f : .2f, 0, 0));
            var ordinary = host.AddComponent<DynamicBone>(); ordinary.m_Root = root; chains.Add(ordinary);
            var weighted = new Transform[6]; var joint = root;
            // The reported rig has six separate weighted leaves on a serial
            // parent chain, all sharing one append source: the final leaf.
            for (int i = 0; i < 6; i++)
            {
                joint = Child(side + " joint " + i, joint, new(0, 0, .025f));
                weighted[i] = Child(side + " weighted " + i, joint, new());
            }
            groups.Add(weighted);
            foreach (var bone in weighted)
                candidates.Add(new NativeContactShape { Bone = bone, Driver = weighted[5], Radius = .06f,
                    HasReferencePose = true, ReferenceCenter = bone.position, SourceBody = "source" });
            for (int i = 0; i < 5; i++)
                grants.Add(new NativeContactGrant { Bone = weighted[i], Source = weighted[5], Rate = (i + 1) * .15f });
        }
        var arm = Child("arm", model, new(2, 0, 0));
        var environment = host.AddComponent<AssetPhysicsEnvironment>(); environment.Chains = chains.ToArray();
        environment.Settings.NativeBreastContacts = true;
        foreach (var chain in chains) environment.Settings.EditRoot(chain.m_Root.name).BreastRole = 1;
        var definition = new NativeContactDefinition { Model = model, ManualCandidates = candidates.ToArray(),
            PositionGrants = grants.ToArray(), Obstacles = [new() { Bone = arm, Radius = .03f,
                HasReferencePose = true, ReferenceCenter = arm.position, SourceBody = "arm" }] };
        var shapes = NativeContactSelection.Resolve(definition, environment.Chains, environment.Settings);
        check(shapes.Length == 2 && groups.All(g => shapes.Any(s => s.Bone == g[5])),
            "twelve weighted leaves sharing two actual endpoints yield exactly two contact solvers");
        definition.ManualCandidates = candidates.AsEnumerable().Reverse().ToArray();
        check(NativeContactSelection.Resolve(definition, environment.Chains, environment.Settings).Select(s => s.Bone).ToHashSet().SetEquals(shapes.Select(s => s.Bone)),
            "shared-driver selection is independent of candidate traversal order");
        NativeContactController.Attach(host, definition); var controller = host.GetComponent<NativeContactController>();
        typeof(NativeContactController).GetMethod("OnEnable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(controller, null);
        var original = candidates.ToDictionary(s => s.Bone, s => s.Bone.localPosition);
        DynamicBone_Ver02.Output = new(.03f, .01f, 0);
        int instances = DynamicBone_Ver02.Instances;
        controller.Tick(1f / 60);
        check(DynamicBone_Ver02.Instances - instances == 2 && controller.Status.English.Contains("2 active / 2"),
            "only the two retained endpoints initialize native state; followers have no separate springs");
        foreach (var group in groups)
        {
            var driverDelta = group[5].localPosition - original[group[5]];
            check(driverDelta.magnitude > 0 && group.Take(5).Select((t, i) =>
                (t.localPosition - original[t] - driverDelta * ((i + 1) * .15f)).magnitude).All(d => d < 1e-6f),
                "all same-side followers share the endpoint delta at authored rates under rotated/scaled parents");
        }
        string report = controller.BuildDiagnosticReport();
        check(report.Contains("result=shared-driver") && report.Contains("Contact append followers=10") && report.Contains("Follower writeback:"),
            "diagnostics distinguish shared followers from endpoints and record their actual writes");
        controller.Restore();
        check(original.All(p => (p.Key.localPosition - p.Value).magnitude < 1e-6f), "one restore removes endpoint and follower offsets together");
        for (int i = 0; i < 120; i++) controller.Tick(1f / 60);
        controller.Restore();
        check(original.All(p => (p.Key.localPosition - p.Value).magnitude < 1e-6f), "shared response never accumulates into the ordinary pose");
        // Simulate a decreasing native spring output. The double supplies the
        // trajectory; this checks synchronization, not game spring dynamics.
        foreach (float strength in new[] { 1f, .6f, .2f, 0f })
        {
            DynamicBone_Ver02.Output = new(.03f * strength, 0, 0); controller.Tick(1f / 60);
            check(groups.All(g => g.Take(5).Select((t, i) => (t.localPosition - original[t] -
                (g[5].localPosition - original[g[5]]) * ((i + 1) * .15f)).magnitude).All(d => d < 1e-6f)),
                "all followers return on the same injected trajectory without independent residual offsets");
        }
        DynamicBone_Ver02.Output = new(.03f, 0, 0); chains[0].enabled = false; controller.Tick(1f / 60);
        check(groups[0].All(t => (t.localPosition - original[t]).magnitude < 1e-6f) &&
            groups[1].All(t => (t.localPosition - original[t]).magnitude > 0), "disabling one side suppresses its whole response without suppressing the other side");
        chains[0].enabled = true; controller.Tick(1f / 60);
        var edited = groups[0][0]; edited.localPosition += new Vector3(0, .1f, 0); var edit = edited.localPosition;
        controller.Restore();
        check((edited.localPosition - edit).magnitude < 1e-6f && groups[0].Skip(1).All(t => (t.localPosition - original[t]).magnitude < 1e-6f),
            "manual edits to a follower are preserved while untouched group members restore");
        edited.localPosition = original[edited];
        // A position grant can traverse an unweighted helper and have a signed
        // rate; an independently configured nested chain must remain isolated.
        var helper = Child("unweighted helper", chains[0].m_Root, new());
        definition.PositionGrants = [new() { Bone = helper, Source = groups[0][5], Rate = .5f },
            new() { Bone = groups[0][0], Source = helper, Rate = -.5f }];
        controller.ResetContacts(); controller.Tick(1f / 60);
        check((groups[0][0].localPosition - original[groups[0][0]] +
            (groups[0][5].localPosition - original[groups[0][5]]) * .25f).magnitude < 1e-6f,
            "ordered transitive propagation preserves authored signed products through unweighted helpers");
        controller.ResetContacts();
        check(helper.localPosition.magnitude < 1e-6f && original.All(p => (p.Key.localPosition - p.Value).magnitude < 1e-6f),
            "reset restores transitive unweighted followers as well as the skin bones");
        var nested = host.AddComponent<DynamicBone>(); nested.m_Root = helper;
        environment.Chains = chains.Concat([nested]).ToArray();
        environment.Settings.EditRoot(helper.name).BreastRole = 2;
        controller.Tick(1f / 60);
        check(helper.localPosition.magnitude < 1e-6f && (groups[0][0].localPosition - original[groups[0][0]]).magnitude < 1e-6f,
            "append propagation cannot cross independently configured physics-chain ownership");
        controller.ResetContacts(); environment.Chains = chains.ToArray();
        definition.PositionGrants = [new() { Bone = groups[0][0], Source = groups[0][5], Rate = float.MaxValue }];
        controller.Tick(1f / 60);
        check(controller.Status.English.Contains("unavailable") && original.All(p => (p.Key.localPosition - p.Value).magnitude < 1e-6f),
            "nonfinite amplified grant is rejected and restores already written endpoint poses");
        controller.ResetContacts(); definition.PositionGrants = grants.ToArray();
        Time.frameCount++; controller.ApplyLateUpdate(); Camera.Render();
        check(controller.BuildDiagnosticReport().Contains("followerRenderWriteError=0"), "camera checks the grouped followers as well as contact endpoints");
        edited.localPosition += new Vector3(.01f, 0, 0); Camera.Render();
        check(controller.BuildDiagnosticReport().Contains("followerRenderWriteError=0.02"), "a later follower writer is measured in world units without being overwritten");
        typeof(NativeContactController).GetMethod("OnDisable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(controller, null);
        DynamicBone_Ver02.Output = new();
    }
}
