using System.Reflection;
using AssetImport;
using UnityEngine;

internal static class DebugModeChecks
{
    internal static void Run(Action<bool, string> check)
    {
        string root = Path.Combine(Path.GetTempPath(), "AssetImport-debug-" + Guid.NewGuid().ToString("N"));
        string previousRoot = BepInEx.Paths.GameRootPath;
        var previousResources = Resources.Items;
        var host = new GameObject("debug contact model");
        var breast = new GameObject("breast").transform; breast.SetParent(host.transform, false);
        var arm = new GameObject("arm").transform; arm.SetParent(host.transform, false); arm.position = new(.15f, 0, 0);
        var chain = host.AddComponent<DynamicBone>(); chain.m_Root = breast;
        var env = host.AddComponent<AssetPhysicsEnvironment>(); env.Chains = [chain]; env.Settings.NativeBreastContacts = true;
        NativeContactController controller = null;
        try
        {
            BepInEx.Paths.GameRootPath = root; Resources.Items = [];
            NativeContactDiagnostics.Initialize();
            NativeContactDiagnostics.Request("disabled-startup"); NativeContactDiagnostics.Poll();
            check(!NativeContactDiagnostics.Save("manual") && !Directory.Exists(root) && NativeContactDiagnostics.ReportPath == null,
                "Debug off never creates a directory or report, even through manual export");
            int logs = AssetImport.AssetImport.Logger.Lines.Count, observers = Camera.Observers;
            NativeContactController.Attach(host, new NativeContactDefinition { Model = host.transform,
                Breasts = [new() { Bone = breast, Radius = .1f }], Obstacles = [new() { Bone = arm, Radius = .05f }] });
            controller = host.GetComponent<NativeContactController>(); Resources.Items = [controller];
            DynamicBone_Ver02.Output = new(.02f, 0, 0);
            controller.Tick(1f / 60);
            check((breast.position - DynamicBone_Ver02.Output).magnitude < 1e-6f && chain.enabled,
                "turning Debug off does not disable ordinary physics or endpoint contacts");
            var native = DynamicBone_Ver02.Created[^1]; int instances = DynamicBone_Ver02.Instances;
            check(native.ResetCalls > 0 && DynamicBone_Ver02.Steps > 16,
                "native initialization and safety self-checks still run without Debug");
            Camera.Render();
            typeof(NativeContactController).GetMethod("DiagnosticHeartbeat", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(controller, null);
            check(AssetImport.AssetImport.Logger.Lines.Count == logs && Camera.Observers == observers,
                "Debug off neither logs diagnostic summaries nor registers render observers");
            check(controller.BuildDiagnosticReport() == "Debug is disabled." &&
                (int)typeof(NativeContactController).GetField("_lastWriteFrame", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(controller)! == -1,
                "Debug off skips geometry/writeback sampling and detailed report inspection");

            AssetDebug.Enabled = true;
            controller.Tick(1f / 60); Camera.Render();
            NativeContactDiagnostics.Select(env, true, "selected"); NativeContactDiagnostics.Poll();
            string path = Path.Combine(root, "UserData/AssetImport/Diagnostics/breast-contacts.txt");
            string enabledReport = File.ReadAllText(path);
            check(enabledReport.Contains("reason: debug-enabled") && enabledReport.Contains("Model: debug contact model") &&
                enabledReport.Contains("Contact geometry: frame="), "enabling Debug in a loaded scene captures a fresh report without reimport");
            check(DynamicBone_Ver02.Instances == instances && Camera.Observers == observers + 1 &&
                (breast.position - DynamicBone_Ver02.Output).magnitude < 1e-6f,
                "enabling Debug adds one observer but never rebuilds the engine or changes contact displacement");

            NativeContactDiagnostics.Request("pending-before-disable");
            AssetDebug.Enabled = false; controller.Tick(1f / 60); Camera.Render();
            Time.realtimeSinceStartup += 10; NativeContactDiagnostics.Poll();
            check(!NativeContactDiagnostics.Save("manual") && File.ReadAllText(path) == enabledReport && NativeContactDiagnostics.ReportPath == null,
                "disabling Debug discards pending writes, blocks manual export and preserves existing report files");
            check(Camera.Observers == observers && DynamicBone_Ver02.Instances == instances &&
                (breast.position - DynamicBone_Ver02.Output).magnitude < 1e-6f, "disabling Debug removes its observer without resetting physics");
            logs = AssetImport.AssetImport.Logger.Lines.Count;
            for (int i = 0; i < 8; i++) { Time.realtimeSinceStartup += 5; controller.Tick(1f / 60); NativeContactDiagnostics.Poll(); }
            check(AssetImport.AssetImport.Logger.Lines.Count == logs && File.ReadAllText(path) == enabledReport,
                "continued playback with Debug off produces no diagnostic logs or periodic writes");
            AssetDebug.Enabled = true; controller.Tick(1f / 60); NativeContactDiagnostics.Poll();
            check(File.ReadAllText(path) != enabledReport && Camera.Observers == observers + 1 && DynamicBone_Ver02.Instances == instances,
                "re-enabling refreshes diagnostics without duplicate observers or solver instances");
            AssetDebug.Enabled = false; controller.ResetContacts(); DynamicBone_Ver02.FailCalibration = true; controller.Tick(1f / 60);
            check(controller.Status.English.Contains("unavailable") && AssetImport.AssetImport.Logger.Lines.Any(s => s.Contains("adapter stopped")),
                "actual physics failure still stops the contact layer and reports a warning when Debug is off");
        }
        finally
        {
            AssetDebug.Enabled = false; DynamicBone_Ver02.FailCalibration = false; DynamicBone_Ver02.Output = new();
            if (controller != null)
                typeof(NativeContactController).GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(controller, null);
            Resources.Items = previousResources; BepInEx.Paths.GameRootPath = previousRoot;
            if (Directory.Exists(root) && Path.GetDirectoryName(Path.GetFullPath(root)) == Path.TrimEndingDirectorySeparator(Path.GetTempPath()))
                Directory.Delete(root, true);
        }
    }
}
