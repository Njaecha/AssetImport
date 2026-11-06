using System.Reflection;
using AssetImport;
using UnityEngine;

internal static class SkinTimingChecks
{
    internal static void Run(Action<bool, string> check)
    {
        check(typeof(NativeContactController).GetCustomAttribute<DefaultExecutionOrder>()!.Order == -32000 &&
            typeof(NativeContactLateUpdate).GetCustomAttribute<DefaultExecutionOrder>()!.Order == 32000,
            "restore and writeback occupy opposite sides of ordinary script updates");
        var host = new GameObject("skin-test"); var bone = new GameObject("bone").transform;
        bone.SetParent(host.transform, false); var tip = new GameObject("tip").transform; tip.SetParent(bone, false);
        var duplicate = new GameObject("bone").transform; duplicate.SetParent(host.transform, false);
        var arm = new GameObject("arm").transform; arm.SetParent(host.transform, false); arm.position = new(.15f, 0, 0);
        var renderer = host.AddComponent<SkinnedMeshRenderer>(); renderer.bones = [bone, tip];
        renderer.sharedMesh = new Mesh { name = "weighted mesh", vertexCount = 12 };
        var chain = host.AddComponent<DynamicBone>(); chain.m_Root = bone;
        var environment = host.AddComponent<AssetPhysicsEnvironment>(); environment.Chains = [chain]; environment.Settings.NativeBreastContacts = true;
        var definition = new NativeContactDefinition { Model = host.transform,
            Breasts = [new() { Bone = bone, Radius = .1f }], Obstacles = [new() { Bone = arm, Radius = .1f, SourceBody = "arm" }] };
        NativeContactController.Attach(host, definition); var controller = host.GetComponent<NativeContactController>();
        typeof(NativeContactController).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(controller, null);
        DynamicBone_Ver02.Output = new(-.03f, 0, 0);
        Time.frameCount++; controller.ApplyLateUpdate(); Camera.Render();
        string report = controller.BuildDiagnosticReport();
        check(report.Contains("writePhase=LateUpdate") && report.Contains("renderWriteError=0"), "report identifies the phase and camera-stage pose consistency");
        check(report.Contains("Skin binding result: bone=bone; directSlots=1; descendantSlots=1; result=bound"), "report checks actual renderer bone references including inherited deformation");
        check(report.Contains("inputGap=-0.05") && report.Contains("outputGap=-0.02"), "paired geometry compares before and after writeback in one obstacle pose");
        renderer.bones = [duplicate];
        check(controller.BuildDiagnosticReport().Contains("result=NO_RENDERER_REFERENCE"), "same bone name on a different Transform does not pass skin binding verification");
        bone.position += new Vector3(.015f, 0, 0); var edited = bone.position;
        Camera.Render(); report = controller.BuildDiagnosticReport();
        check(report.Contains("renderWriteError=0.015") && (bone.position - edited).magnitude == 0, "a later writer is measured but not overwritten in the camera callback");
        int steps = DynamicBone_Ver02.Steps;
        typeof(NativeContactController).GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(controller, null);
        check(!host.GetComponent<NativeContactLateUpdate>().enabled, "disabling the controller disables its late updater");
        Camera.Render(); check(DynamicBone_Ver02.Steps == steps, "disabled controller cannot advance from render callbacks");
        DynamicBone_Ver02.Output = new();
    }
}
