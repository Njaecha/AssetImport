using System.Reflection;
using AssetImport;
using UnityEngine;

int checks = 0;
void Check(bool value, string why) { checks++; if (!value) throw new Exception(why); }
void Invoke(NativeContactController c, string name) => typeof(NativeContactController).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(c, null);
void ApplyLate(NativeContactController c) => typeof(NativeContactLateUpdate).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(c.GetComponent<NativeContactLateUpdate>(), null);
Transform Child(string name, Transform parent, Vector3 local)
{ var t = new GameObject(name).transform; t.SetParent(parent, false); t.localPosition = local; return t; }

Check(!AssetDebug.Enabled, "Debug defaults to off independently of model physics settings");
DebugModeChecks.Run(Check);
// The remaining suites explicitly exercise the opt-in diagnostic paths.
AssetDebug.Enabled = true;

foreach (string target in new[] { "KK", "KKS" })
{
    using var game = new MetadataAssembly(Path.Combine(AppContext.BaseDirectory, "Fixtures", target, "Assembly-CSharp.dll"));
    foreach (string name in new[] { "InitNodeParticle", "InitLocalPosition", "SetupParticles", "InitTransforms", "ApplyParticlesToTransforms", "ResetParticlesPosition" })
        Check(!game.RequireMethod("DynamicBone_Ver02", name).IsNil, target + " native method " + name);
    Check(!game.RequireMethod("DynamicBone_Ver02", "setPtn", "System.Int32", "System.Boolean").IsNil, target + " pattern API");
    Check(!game.RequireMethod("DynamicBone_Ver02", "UpdateDynamicBones", "System.Single").IsNil, target + " update API");
    foreach (var type in new[] { typeof(DynamicBone_Ver02), typeof(DynamicBone_Ver02.BoneParameter), typeof(DynamicBone_Ver02.BonePtn), typeof(DynamicBone_Ver02.Particle) })
    {
        var real = game.Reader.GetTypeDefinition(game.FindType(type.FullName!));
        var fields = real.GetFields().Select(h => game.Reader.GetString(game.Reader.GetFieldDefinition(h).Name)).ToHashSet();
        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            Check(fields.Contains(field.Name), target + " data field " + type.Name + "." + field.Name);
    }
}

foreach (float scale in new[] { .1f, 1f, 3f })
{
    var host = new GameObject(); var model = Child("model", host.transform, new()); model.localScale = new(scale, scale, scale);
    var torso = Child("torso", model, new(0, 1, 0)); var breast = Child("breast", torso, new(0, 0, .2f));
    var arm = Child("arm", torso, new(.3f, 0, .2f));
    var ordinary = host.AddComponent<DynamicBone>(); ordinary.m_Root = breast;
    var environment = host.AddComponent<AssetPhysicsEnvironment>(); environment.Chains = [ordinary];
    var definition = new NativeContactDefinition { Model = model,
        Breasts = [new() { Bone = breast, Radius = .1f }], Obstacles = [new() { Bone = arm, Radius = .05f, Height = .2f }] };
    NativeContactController.Attach(host, definition); var controller = host.GetComponent<NativeContactController>();
    Invoke(controller, "OnEnable");
    Vector3 original = breast.position; int beforeInstances = DynamicBone_Ver02.Instances;
    controller.Tick(1f / 60);
    Check(DynamicBone_Ver02.Instances == beforeInstances && breast.position.sqrMagnitude == original.sqrMagnitude, "default off has no proxy or pose mutation");
    environment.Settings.NativeBreastContacts = true;
    DynamicBone_Ver02.Output = new(.02f * scale, 0, 0);
    controller.Tick(1f / 60);
    Check((breast.position - original - DynamicBone_Ver02.Output).magnitude < 1e-6f, "native proxy output is applied in world units");
    Check(ordinary.enabled && ordinary.m_Weight == 1 && ordinary.m_Root == breast, "ordinary physics is not disabled or reconfigured");
    controller.Restore(); Check((breast.position - original).magnitude < 1e-6f, "original physics output restored before next animation");
    for (int i = 0; i < 120; i++) controller.Tick(1f / 60);
    Check((breast.position - original - DynamicBone_Ver02.Output).magnitude < 1e-6f, "contact offsets never accumulate on the original skeleton");
    breast.localPosition += new Vector3(0, .03f, 0); var edit = breast.localPosition;
    controller.Restore(); Check((breast.localPosition - edit).sqrMagnitude < 1e-12f, "post-render user edit is preserved");
    original = breast.position;
    int steps = DynamicBone_Ver02.Steps;
    Time.frameCount++; Invoke(controller, "Update"); Camera.Render();
    Check(DynamicBone_Ver02.Steps == steps, "camera before LateUpdate cannot capture incomplete animation");
    Invoke(controller, "LateUpdate");
    Check(DynamicBone_Ver02.Steps == steps, "early controller LateUpdate does not run before ordinary physics");
    ApplyLate(controller);
    // Model the skinning boundary before camera callbacks. This fails when
    // writeback is deferred to onPreCull/onPreRender, even if counters advance.
    var skinningPose = breast.position;
    Check((skinningPose - original - DynamicBone_Ver02.Output).magnitude < 1e-6f,
        "skinning stage sees contact displacement before any camera callback");
    ApplyLate(controller); Camera.Render(); Camera.Render();
    Check(DynamicBone_Ver02.Steps == steps + 1 && (breast.position - skinningPose).magnitude == 0, "multiple cameras observe one pose and never advance or restore physics");
    Check(controller.BuildDiagnosticReport().Contains("renderWriteError=0") && controller.BuildDiagnosticReport().Contains("renderObservations=2"), "camera stage verifies the written pose without claiming GPU rendering");
    Time.frameCount++; Invoke(controller, "Update");
    Check((breast.position - original).magnitude < 1e-6f, "early Update removes the contact offset before next animation and physics");
    ApplyLate(controller);
    Check(DynamicBone_Ver02.Steps == steps + 2, "simulation progresses without a camera callback");
    AssetPhysicsEnvironment.Reset(ordinary);
    Check((breast.position - original).magnitude < 1e-6f, "reset immediately restores the input pose");
    ordinary.enabled = false; steps = DynamicBone_Ver02.Steps; controller.Tick(1f / 60);
    Check(DynamicBone_Ver02.Steps == steps, "physics-off cannot add contacts");
    ordinary.enabled = true;
    environment.Settings.EditRoot("breast").Mode = (int)PhysicsChainMode.Animation;
    controller.Tick(1f / 60); Check(DynamicBone_Ver02.Steps == steps, "no-simulation override prevents contact motion");
    environment.Settings.Chains = [];
    controller.ResetContacts(); DynamicBone_Ver02.FailSetup = true;
    controller.Tick(1f / 60);
    Check(controller.Status.English.Contains("unavailable") && ordinary.enabled && (breast.position - original).magnitude < 1e-6f, "missing native movable particles fail without touching ordinary physics");
    beforeInstances = DynamicBone_Ver02.Instances; controller.Tick(1f / 60);
    Check(DynamicBone_Ver02.Instances == beforeInstances, "failed engine does not retry each frame");
    DynamicBone_Ver02.FailSetup = false; controller.ResetContacts(); DynamicBone_Ver02.FailStep = true;
    controller.Tick(1f / 60);
    Check(controller.Status.English.Contains("unavailable") && (breast.position - original).magnitude < 1e-6f, "native update exception leaves original pose intact");
    DynamicBone_Ver02.FailStep = false; controller.ResetContacts(); DynamicBone_Ver02.Output = new(float.NaN, 0, 0);
    controller.Tick(1f / 60);
    Check(controller.Status.English.Contains("unavailable") && (breast.position - original).magnitude < 1e-6f, "nonfinite native output cannot contaminate the model");
    controller.ResetContacts(); DynamicBone_Ver02.FailCalibration = true;
    controller.Tick(1f / 60);
    Check(controller.Status.English.Contains("unavailable") && ordinary.enabled, "a native engine that never responds to the validation sphere is rejected before pose writeback");
    DynamicBone_Ver02.FailCalibration = false;
    controller.ResetContacts(); DynamicBone_Ver02.Output = new(.02f * scale, 0, 0); controller.Tick(1f / 60);
    environment.Settings.NativeBreastContacts = false; controller.Tick(1f / 60);
    Check((breast.position - original).magnitude < 1e-6f && ordinary.enabled, "disabling the feature restores ordinary physics immediately");
    Invoke(controller, "OnDisable");
    steps = DynamicBone_Ver02.Steps; Time.frameCount++; Camera.Render(); Check(DynamicBone_Ver02.Steps == steps, "disabled controller unsubscribes camera callbacks");
}

var asset = new Asset(); Check(!PhysicsEnvironmentPersistence.Read(asset).NativeBreastContacts, "old scenes default off");
var preference = new PhysicsEnvironmentSettings { NativeBreastContacts = true };
PhysicsEnvironmentPersistence.Write(asset, preference);
Check(PhysicsEnvironmentPersistence.Read(asset).NativeBreastContacts && preference.Copy().NativeBreastContacts, "opt-in survives scene/card setting copies");

// Zero output and absent render callbacks must be visible without successful
// contact. Reports must neither run the solver nor write bone positions.
{
    var log = global::AssetImport.AssetImport.Logger.Lines; log.Clear();
    var host = new GameObject("diagnostic-host"); var model = Child("diagnostic-model", host.transform, new());
    var breast = Child("breast", model, new(0, 1, 0)); var arm = Child("arm", model, new(0, 1, 0));
    var chain = host.AddComponent<DynamicBone>(); chain.m_Root = breast;
    var environment = host.AddComponent<AssetPhysicsEnvironment>(); environment.Chains = [chain];
    var obstacle = new NativeContactShape { Bone = arm, Radius = .1f, Height = .4f, Direction = 1, SourceBody = "[2]arm" };
    var definition = new NativeContactDefinition { Model = model, RecognitionReport = "breast bone=breast; result=selected",
        Breasts = [new() { Bone = breast, Radius = .1f }], Obstacles = [obstacle] };
    NativeContactController.Attach(host, definition); var c = host.GetComponent<NativeContactController>();
    Invoke(c, "OnEnable"); c.Tick(1f / 60);
    Check(log.Any(s => s.Contains("state=off")), "disabled opt-in produces a state log");
    int lines = log.Count; for (int i = 0; i < 300; i++) c.Tick(1f / 60);
    Check(log.Count == lines, "stable state does not log each frame");
    environment.Settings.NativeBreastContacts = true; c.ReportSettingsApplied();
    DynamicBone_Ver02.Output = new(); c.Tick(1f / 60);
    Check(log.Any(s => s.Contains("selfCheck.sphereDisplacement=")), "self-check records measured response");
    Check(log.Any(s => s.Contains("stage=SetupParticles")), "native initialization stage is logged before invocation");
    lines = log.Count; Invoke(c, "DiagnosticHeartbeat");
    Check(log.Count == lines, "periodic logging respects wall-clock interval");
    Time.realtimeSinceStartup += 5.1f; Invoke(c, "DiagnosticHeartbeat");
    Check(log.Any(s => s.Contains("summary;") && s.Contains("lastDisplacement=0") && s.Contains("state=running")), "running solver with zero response still logs a summary");
    Check(log.Any(s => s.Contains("estimatedOverlaps=1") && s.Contains("[2]arm")), "geometry report names intersecting obstacle without claiming native contact");
    var before = breast.position; int steps = DynamicBone_Ver02.Steps;
    string report = c.BuildDiagnosticReport();
    Check(report.Contains("result=selected") && report.Contains("Applied enabled: True") && report.Contains("poseWrites="), "export contains recognition, applied setting and write counters");
    Check((breast.position - before).magnitude == 0 && DynamicBone_Ver02.Steps == steps, "capturing diagnostics never moves bones or advances native solver");
    Time.realtimeSinceStartup += 5.1f; Invoke(c, "DiagnosticHeartbeat");
    Check(c.Status.English.Contains("no late update") && log.Any(s => s.Contains("ticksSinceSummary=0")), "watchdog distinguishes a missing late update from an absent camera");
    c.Tick(0); Check(c.BuildDiagnosticReport().Contains("state=zero-delta-time"), "paused update reports zero delta separately from running");
    c.ResetContacts(); DynamicBone_Ver02.FailCalibration = true; c.Tick(1f / 60);
    report = c.BuildDiagnosticReport();
    Check(report.Contains("state=failed") && report.Contains("overlapping sphere produced no response"), "self-check failure retains exact cause for export");
    c.ResetContacts(); Check(c.BuildDiagnosticReport().Contains("overlapping sphere produced no response"), "retry does not erase diagnostic failure evidence");
    DynamicBone_Ver02.FailCalibration = false;
    environment.enabled = false; c.Tick(1f / 60);
    Check(c.BuildDiagnosticReport().Contains("state=environment-disabled"), "previously silent environment guard has explicit reason");
    environment.enabled = true; c.ResetContacts(); definition.Breasts = []; c.Tick(1f / 60);
    Check(c.BuildDiagnosticReport().Contains("state=no-breast-bodies"), "zero recognized breasts has its own diagnostic state");
    Check(Math.Abs(NativeContactController.SurfaceGap(new(0, 1.4f, 0), .1f, obstacle) - .1f) < 1e-6f, "capsule signed gap accounts for segment length and both radii");
    var culture = System.Globalization.CultureInfo.CurrentCulture;
    System.Globalization.CultureInfo.CurrentCulture = new("de-DE");
    Check(NativeContactController.Number(.125f) == "0.125", "diagnostic numbers are locale independent");
    System.Globalization.CultureInfo.CurrentCulture = culture;
    Invoke(c, "OnDisable");
}
PairAndBoundChecks.Run(Check);
SkinTimingChecks.Run(Check);
ManualContactChecks.Run(Check);
ContactVolumeChecks.Run(Check);
EndpointContactChecks.Run(Check);
SharedDriverChecks.Run(Check);
CapsuleContactChecks.Run(Check);

// Exercise the production report writer on disk, including the no-model and
// unwritable-game-directory paths, without requiring a UI click or game loop.
{
    string testRoot = Path.Combine(Path.GetTempPath(), "AssetImport-report-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(testRoot);
    string fallbackDirectory = null;
    try
    {
        BepInEx.Paths.GameRootPath = testRoot; Resources.Items = [];
        NativeContactDiagnostics.Select(null, null, "no selection");
        NativeContactDiagnostics.Initialize();
        string path = Path.Combine(testRoot, "UserData/AssetImport/Diagnostics/breast-contacts.txt");
        Check(File.Exists(path), "startup creates report without models or user interaction");
        string initial = File.ReadAllText(path);
        Check(initial.Contains("reason: startup") && initial.Contains("No contact controller was created") && initial.Contains("Game root: " + testRoot), "startup report identifies session directory and absent model");
        Check(Debug.Lines.Any(s => s.Contains("AssetImport diagnostics: build=") && s.Contains("breast-contacts.txt")), "report path also reaches Unity player log");
        NativeContactDiagnostics.Request("sample-one"); NativeContactDiagnostics.Poll();
        Check(File.ReadAllText(path) == initial, "automatic report writes are rate limited");
        NativeContactDiagnostics.Request("sample-two"); Time.realtimeSinceStartup += 5.1f;
        NativeContactDiagnostics.Poll();
        Check(File.ReadAllText(path).Contains("reason: sample-two"), "multiple model events are coalesced and saved without UI");
        Check(Directory.GetFiles(Path.GetDirectoryName(path)).Length == 1, "automatic capture updates one report instead of accumulating files");
        var model = new GameObject("report-model"); var broken = model.AddComponent<NativeContactController>();
        broken.Definition = new NativeContactDefinition { Model = model.transform, Obstacles = null };
        Resources.Items = [broken];
        Check(NativeContactDiagnostics.Save("manual") && File.ReadAllText(path).Contains("Diagnostic capture failed:"), "one broken model cannot prevent report creation");
        Resources.Items = [];
        Resources.FailEnumeration = true;
        Check(NativeContactDiagnostics.Save("manual") && File.ReadAllText(path).Contains("injected type load failure"), "component discovery failure still produces a minimal diagnostic file");
        Resources.FailEnumeration = false;
        var blockedRoot = Path.Combine(testRoot, "blocked-game"); Directory.CreateDirectory(blockedRoot);
        File.WriteAllText(Path.Combine(blockedRoot, "UserData"), "directory deliberately blocked by file");
        BepInEx.Paths.GameRootPath = blockedRoot;
        NativeContactDiagnostics.Initialize();
        fallbackDirectory = Path.GetDirectoryName(NativeContactDiagnostics.ReportPath);
        Check(File.Exists(NativeContactDiagnostics.ReportPath) && NativeContactDiagnostics.ReportPath != path && NativeContactDiagnostics.LastError.Contains("temporary folder"), "write failure uses a visible temporary fallback");
        Check(File.ReadAllText(NativeContactDiagnostics.ReportPath).Contains("Primary report path unavailable"), "fallback report retains original write error");
    }
    finally
    {
        // Delete only these freshly created test directories.
        if (Path.GetDirectoryName(Path.GetFullPath(testRoot)) == Path.TrimEndingDirectorySeparator(Path.GetTempPath())) Directory.Delete(testRoot, true);
        if (fallbackDirectory != null && Path.GetDirectoryName(Path.GetFullPath(fallbackDirectory)) == Path.Combine(Path.TrimEndingDirectorySeparator(Path.GetTempPath()), "AssetImport-Diagnostics"))
            Directory.Delete(fallbackDirectory, true);
    }
}
Console.WriteLine($"PASS: {checks} native contact adapter lifecycle, diagnostic files and KK/KKS metadata checks. Collision output was injected; game physics and visual behavior are NOT tested.");
