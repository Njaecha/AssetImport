using System.Reflection;
using AssetImport;
using UnityEngine;

int checks = 0;
void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
bool Near(float a, float b, float tolerance = .00001f) => Math.Abs(a - b) < tolerance;
void Tick(AssetPhysicsEnvironment e) => typeof(AssetPhysicsEnvironment).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(e, null);
void Lifecycle(AssetPhysicsEnvironment e, string method) => typeof(AssetPhysicsEnvironment).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(e, null);
Transform Child(Transform parent, Vector3 position)
{
    var t = new GameObject().transform; t.parent = parent; t.position = position; return t;
}
(AssetPhysicsEnvironment Environment, DynamicBone Chain, Transform Root) Create(PhysicsEnvironmentSettings settings, float scale = 1)
{
    var host = new GameObject(); var root = Child(host.transform, new()); root.lossyScale = new(scale, scale, scale);
    var anchor = Child(root, new(0, scale * .5f, 0));
    var tip = Child(anchor, new(scale, scale * .5f, 0));
    var chain = host.AddComponent<DynamicBone>(); chain.m_Root = anchor;
    chain.m_Particles.Add(new() { m_Transform = anchor, m_Position = anchor.position, m_PrevPosition = anchor.position });
    chain.m_Particles.Add(new() { m_ParentIndex = 0, m_Transform = tip, m_Position = tip.position, m_PrevPosition = tip.position });
    chain.SetupParticles();
    AssetPhysicsEnvironment.Attach(host, root, [chain], settings);
    return (host.GetComponent<AssetPhysicsEnvironment>(), chain, root);
}

// Check production Harmony declarations against both actual reference APIs.
foreach (string target in new[] { "KK", "KKS" })
{
    using var game = new MetadataAssembly(Path.Combine(AppContext.BaseDirectory, "Fixtures", target, "Assembly-CSharp.dll"));
    foreach (var hook in typeof(PhysicsEnvironmentHooks).GetMethods(BindingFlags.NonPublic | BindingFlags.Static))
    {
        var patch = hook.GetCustomAttribute<HarmonyLib.HarmonyPatch>();
        if (patch == null) continue;
        var parameters = patch.info.methodName == "UpdateDynamicBones" ? new[] { "System.Single" } : Array.Empty<string>();
        var method = game.Reader.GetMethodDefinition(game.RequireMethod(patch.info.declaringType.Name, patch.info.methodName, parameters));
        Check(method.DecodeSignature(game.Names, null).ReturnType == "System.Void" && (method.Attributes & MethodAttributes.Static) == 0,
            target + " compatible hook: " + patch.info.methodName);
    }
    game.RequireMethod("DynamicBone", "InitTransforms"); game.RequireMethod("DynamicBone", "UpdateDynamicBones", "System.Single");
    game.RequireMethod("DynamicBoneCollider", "Collide", "UnityEngine.Vector3&", "System.Single");
    foreach (var pair in new[] { ("DynamicBone", "m_Weight"), ("DynamicBone", "m_DistantDisabled"),
        ("DynamicBone+Particle", "m_Position"), ("DynamicBone+Particle", "m_PrevPosition"), ("DynamicBone+Particle", "m_EndOffset") })
        Check(game.Reader.GetTypeDefinition(game.FindType(pair.Item1)).GetFields().Any(f => game.Reader.GetString(game.Reader.GetFieldDefinition(f).Name) == pair.Item2), target + " particle API " + pair.Item2);
}

DrapeChecks.InstallAndRun(Check);
ChainPhysicsChecks.Run(Check);
JointSolverChecks.Run(Check);

var legacy = PhysicsEnvironmentPersistence.Read(new Asset());
Check(!legacy.Floor && !legacy.Settle && !legacy.NaturalDrape && legacy.DownwardForce == 0, "legacy records retain their behavior");
var saved = new Asset(); var original = new PhysicsEnvironmentSettings { Floor = true, FloorOffset = -.4f, DownwardForce = .003f, Settle = true };
PhysicsEnvironmentPersistence.Write(saved, original);
var copied = new Asset(); PhysicsEnvironmentPersistence.Write(copied, PhysicsEnvironmentPersistence.Read(saved));
original.FloorOffset = 5;
var restored = PhysicsEnvironmentPersistence.Read(copied);
Check(restored.Floor && restored.Settle && restored.FloorOffset == -.4f && restored.DownwardForce == .003f, "value settings survive persistence/copy independently");
foreach (string key in new[] { "FloorCollision", "FloorOffset", "DownwardForce", "SettlePhysics", "NaturalDrape" })
    Check(typeof(Asset).GetProperty(key)!.GetCustomAttribute<MessagePack.KeyAttribute>()!.Key == key, "stable MessagePack key " + key);
var sanitized = new PhysicsEnvironmentSettings { FloorOffset = float.NaN, DownwardForce = float.PositiveInfinity }.Copy();
Check(sanitized.FloorOffset == 0 && sanitized.DownwardForce == 0, "invalid persisted values sanitized");

// Random plane/sphere contacts include arbitrary heights, scales, vertical
// singularities and anchors below the plane. A fixed root cannot be lifted.
var random = new Random(171);
for (int i = 0; i < 5000; i++)
{
    float length = .001f + (float)random.NextDouble() * 4;
    float height = (float)random.NextDouble() * 20 - 10;
    float dy = ((float)random.NextDouble() * 1.98f - .99f) * length;
    var parent = new Vector3(7, height - dy, -3);
    var position = parent + new Vector3(0, -length, 0);
    var result = PhysicsEnvironmentMath.Contact(position, parent, new(0, 0, 1), length, height);
    Check(Near(result.y, height) && Near((result - parent).magnitude, length), "contact preserves floor and link length");
}
var unreachable = PhysicsEnvironmentMath.Contact(new(0, -4, 0), new(0, -3, 0), new(1, 0, 0), 1, 0);
Check(Near(unreachable.y, -2), "unreachable floor does not stretch the fixed link");

var model = Create(PhysicsEnvironmentSettings.NewImport());
var p = model.Chain.m_Particles[1]; var anchorParticle = model.Chain.m_Particles[0];
var before = p.m_Position; model.Chain.m_Force = new(1, 2, 3); model.Chain.m_Gravity = new(4, 5, 6);
AssetPhysicsEnvironment.AfterIntegrate(model.Chain);
Check(Near(p.m_Position.y, before.y - .001f) && Near(anchorParticle.m_Position.y, .5f), "downward load without moving the anchor");
Check(model.Chain.m_Force.y == 2 && model.Chain.m_Gravity.y == 5, "editor and Boop fields untouched");
var foreign = model.Chain.gameObject.AddComponent<DynamicBone>(); foreign.m_Root = model.Chain.m_Root;
foreign.m_Particles.Add(new() { m_ParentIndex = 0, m_Position = new(0, 2, 0) });
AssetPhysicsEnvironment.AfterIntegrate(foreign); AssetPhysicsEnvironment.AfterConstraints(foreign);
Check(foreign.m_Particles[0].m_Position.y == 2, "unregistered chain on same object remains untouched");
model.Chain.enabled = false; before = p.m_Position; AssetPhysicsEnvironment.AfterIntegrate(model.Chain);
Check(p.m_Position.y == before.y && model.Environment.RequestSettle() == 0, "disabled physics not simulated");
model.Chain.enabled = true; model.Chain.m_Weight = 0; AssetPhysicsEnvironment.AfterIntegrate(model.Chain);
Check(p.m_Position.y == before.y && model.Environment.RequestSettle() == 0, "zero weight not simulated");
model.Chain.m_Weight = 1; model.Chain.m_DistantDisabled = true; AssetPhysicsEnvironment.AfterIntegrate(model.Chain);
Check(p.m_Position.y == before.y && model.Environment.RequestSettle() == 0, "distance-disabled physics not simulated");
model.Chain.m_DistantDisabled = false;
model.Chain.m_Root.gameObject.activeInHierarchy = false; AssetPhysicsEnvironment.AfterIntegrate(model.Chain);
Check(p.m_Position.y == before.y, "hidden root not simulated"); model.Chain.m_Root.gameObject.activeInHierarchy = true;

// Multi-link chain: projecting a parent must not stretch its children, even
// when those children were already above ground before the parent moved.
var floorModel = Create(new() { Floor = true });
var first = floorModel.Chain.m_Particles[1]; first.m_Position = new(0, -.5f, 0); first.m_PrevPosition = new(0, -.3f, 0);
var secondTransform = Child(first.m_Transform, new(2, .5f, 0));
floorModel.Chain.m_Particles.Add(new() { m_ParentIndex = 1, m_Transform = secondTransform, m_Position = new(0, .5f, 0) });
AssetPhysicsEnvironment.AfterConstraints(floorModel.Chain);
Check(first.m_Position.y >= 0 && floorModel.Chain.m_Particles[2].m_Position.y >= 0, "linked particles kept above floor");
Check(Near((first.m_Position - first.m_PrevPosition).sqrMagnitude, 0), "contact correction does not inject lateral or downward velocity");
Check(Near((first.m_Position - floorModel.Chain.m_Particles[0].m_Position).magnitude, 1) &&
    Near((floorModel.Chain.m_Particles[2].m_Position - first.m_Position).magnitude, 1), "all link lengths preserved after ancestor contact");
first.m_Radius = .1f; first.m_Position = new(0, -.5f, 0); AssetPhysicsEnvironment.AfterConstraints(floorModel.Chain);
Check(Near(first.m_Position.y, .1f), "particle radius included");
floorModel.Root.position = new(0, -.25f, 0); floorModel.Root.rotation = Quaternion.Turn(1);
first.m_Position = new(0, -.5f, 0); AssetPhysicsEnvironment.AfterConstraints(floorModel.Chain);
Check(Near(first.m_Position.y, -.15f), "floor follows origin height and stays horizontal under rotation");
var scaled = Create(new() { DownwardForce = .001f }, 2); AssetPhysicsEnvironment.AfterIntegrate(scaled.Chain);
Check(Near(scaled.Chain.m_Particles[1].m_Position.y, .998f), "force scales with imported model");
scaled.Environment.Apply(new() { Floor = true, FloorOffset = .2f });
scaled.Chain.m_Particles[1].m_Position = new(0, -1, 0); AssetPhysicsEnvironment.AfterConstraints(scaled.Chain);
Check(Near(scaled.Chain.m_Particles[1].m_Position.y, .4f), "floor offset scales with the model");
var end = Create(new() { Floor = true });
end.Chain.m_Particles[1].m_Transform = null; end.Chain.m_Particles[1].m_EndOffset = new(1, 0, 0);
end.Chain.m_Particles[1].m_Position = new(0, -.5f, 0); AssetPhysicsEnvironment.AfterConstraints(end.Chain);
Check(Near(end.Chain.m_Particles[1].m_Position.y, 0), "virtual end particles contact ground too");
var neutral = Create(new()); var neutralTip = neutral.Chain.m_Particles[1]; neutralTip.m_Position = new(0, -.5f, 0);
AssetPhysicsEnvironment.AfterIntegrate(neutral.Chain); AssetPhysicsEnvironment.AfterConstraints(neutral.Chain);
AssetPhysicsEnvironment.AfterReset(neutral.Chain);
Check(neutralTip.m_Position.y == -.5f && !neutral.Environment.IsSettling, "all settings off preserves legacy simulation including reset");
var detached = Create(PhysicsEnvironmentSettings.NewImport()); detached.Chain.m_Root.parent = new GameObject().transform;
before = detached.Chain.m_Particles[1].m_Position; AssetPhysicsEnvironment.AfterIntegrate(detached.Chain);
Check(detached.Chain.m_Particles[1].m_Position.y == before.y, "reparented foreign chain loses ownership");

var settling = Create(PhysicsEnvironmentSettings.NewImport());
var colliders = settling.Chain.m_Colliders; var distribution = settling.Chain.m_DampingDistrib;
var restPosition = settling.Chain.m_Particles[1].m_InitLocalPosition;
for (int i = 0; i < 300 && settling.Environment.IsSettling; i++) Tick(settling.Environment);
Check(!settling.Environment.IsSettling && settling.Chain.Steps > 0 && settling.Chain.Steps <= AssetPhysicsEnvironment.DrapeSettleSteps, "settling has a finite work bound");
Check(settling.Chain.m_Particles[1].m_Position.y < .4999f && settling.Chain.m_Particles[1].m_Position.y >= 0, "resting fixture sags under downward force and respects ground");
Check(settling.Chain.m_Particles[1].m_InitLocalPosition.y == restPosition.y && ReferenceEquals(colliders, settling.Chain.m_Colliders) && ReferenceEquals(distribution, settling.Chain.m_DampingDistrib), "settling preserves rest pose and parameter references");
settling.Environment.RequestSettle(); int steps = settling.Chain.Steps; settling.Chain.m_Root.position += new Vector3(0, .1f, 0); Tick(settling.Environment);
Check(!settling.Environment.IsSettling && settling.Chain.Steps == steps, "root motion cancels extra simulation");
settling.Environment.RequestSettle(); settling.Root.rotation = Quaternion.Turn(.2f); Tick(settling.Environment);
Check(!settling.Environment.IsSettling && settling.Chain.Steps == steps, "parent animation cancels extra simulation");
settling.Environment.RequestSettle(); settling.Chain.enabled = false; Tick(settling.Environment);
Check(!settling.Environment.IsSettling && settling.Chain.Steps == steps, "disabling during settling cancels work");
settling.Chain.enabled = true; settling.Environment.RequestSettle(); settling.Chain.ThrowOnUpdate = true; Tick(settling.Environment);
Check(!settling.Environment.IsSettling, "native failure terminates the affected work"); settling.Chain.ThrowOnUpdate = false;
settling.Chain.m_Root.position += new Vector3(0, .1f, 0); AssetPhysicsEnvironment.AfterReset(settling.Chain);
Check(settling.Environment.IsSettling, "reset schedules automatic settling at the new anchor");
settling.Chain.m_Root.position += new Vector3(0, .1f, 0); AssetPhysicsEnvironment.AfterReset(settling.Chain);
Tick(settling.Environment);
Check(settling.Chain.Steps > steps, "repeated reset refreshes the queued anchor snapshot");
Lifecycle(settling.Environment, "OnDisable"); before = settling.Chain.m_Particles[1].m_Position;
AssetPhysicsEnvironment.AfterIntegrate(settling.Chain);
Check(settling.Chain.m_Particles[1].m_Position.y == before.y, "lifecycle unregisters owned chains");
Lifecycle(settling.Environment, "OnEnable"); AssetPhysicsEnvironment.AfterIntegrate(settling.Chain);
Check(settling.Chain.m_Particles[1].m_Position.y < before.y, "reactivating registers chains again");
var clone = Create(new()); clone.Environment.Settings = settling.Environment.Settings;
Lifecycle(clone.Environment, "OnEnable"); clone.Environment.Settings.DownwardForce = .005f;
Check(settling.Environment.Settings.DownwardForce == .001f, "cloned components own independent settings");
var stolen = new GameObject().AddComponent<AssetPhysicsEnvironment>();
stolen.Configure(settling.Root, [settling.Chain], PhysicsEnvironmentSettings.NewImport());
Check(stolen.RequestSettle() == 0, "foreign host cannot claim chains");

// Many chains share the same per-object step/time budget, rather than each
// receiving a full warmup in a single rendered frame.
var crowded = Create(new() { Settle = true });
var crowd = Enumerable.Range(0, 60).Select(_ =>
{
    var chain = crowded.Chain.gameObject.AddComponent<DynamicBone>(); chain.m_Root = crowded.Chain.m_Root;
    chain.m_Particles.Add(new() { m_Transform = chain.m_Root, m_Position = chain.m_Root.position });
    chain.m_Particles.Add(new() { m_Transform = crowded.Chain.m_Particles[1].m_Transform, m_ParentIndex = 0,
        m_Position = new(1, .5f, 0), m_PrevPosition = new(1, .5f, 0) });
    chain.SetupParticles();
    return chain;
}).ToArray();
crowded.Environment.Configure(crowded.Root, crowd, new() { Settle = true }); Tick(crowded.Environment);
Check(crowded.Environment.IsSettling && crowd.Sum(c => c.Steps) <= 128, "one frame cannot warm up every chain completely");
crowded.Environment.Apply(new()); Check(!crowded.Environment.IsSettling, "changing settings cancels pending warmup");
Console.WriteLine($"PASS: {checks} physics environment checks (production math/lifecycle with a Verlet fixture, KK/KKS API metadata). Native game behavior requires in-game testing.");
