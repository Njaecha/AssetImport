using System.Reflection;
using AssetImport;

int checks = 0;
void Check(bool condition, string reason)
{
    checks++;
    if (!condition) throw new Exception(reason);
}

foreach (var fixture in new[] {
    (Game: "KK", Path: "Fixtures/KK/Assembly-CSharp.dll"),
    (Game: "KKS", Path: "Fixtures/KKS/Assembly-CSharp.dll") })
{
    using var game = new MetadataAssembly(Path.Combine(AppContext.BaseDirectory, fixture.Path));
    var reset = game.Reader.GetMethodDefinition(game.RequireMethod("DynamicBone", "ResetParticlesPosition"));
    Check((reset.Attributes & MethodAttributes.Public) != 0, fixture.Game + " reset is public");
    Check((reset.Attributes & MethodAttributes.Static) == 0, fixture.Game + " reset is instance method");
    Check(reset.DecodeSignature(game.Names, null).ReturnType == "System.Void", fixture.Game + " reset returns void");
    Console.WriteLine("PASS " + fixture.Game + ": DynamicBone.ResetParticlesPosition() API metadata.");
}

Check(PhysicsReset.Reset(null).ResetCount == 0, "null input is harmless");
Check(PhysicsReset.Reset([]).ResetCount == 0, "empty input is harmless");
var first = new DynamicBone();
var second = new DynamicBone();
var off = new DynamicBone { enabled = false };
var hiddenHost = new DynamicBone(); hiddenHost.gameObject.activeInHierarchy = false;
var hiddenRoot = new DynamicBone(); hiddenRoot.m_Root.gameObject.activeInHierarchy = false;
var noRoot = new DynamicBone { m_Root = null };
var destroyed = new DynamicBone { Destroyed = true };
var broken = new DynamicBone { ThrowOnReset = true };
var sourceRoot = first.m_Root;
var sourceParticles = first.m_Particles;
var sourceColliders = first.m_Colliders;
var sourceCurve = first.m_DampingDistrib;
var sourcePosition = first.m_Root.Position;
var sourceRotation = first.m_Root.Rotation;
var result = PhysicsReset.Reset([first, first, off, hiddenHost, noRoot, destroyed, null, broken, second, hiddenRoot]);
Check(result.ResetCount == 2, "valid chains reset once each");
Check(first.ResetCalls == 1 && second.ResetCalls == 1, "duplicates excluded without losing later valid chains");
Check(result.InactiveCount == 3, "disabled/hidden chains are skipped");
Check(off.ResetCalls == 0 && hiddenHost.ResetCalls == 0 && hiddenRoot.ResetCalls == 0, "no native reset on inactive chains");
Check(result.InvalidCount == 3, "null/destroyed/missing-root chains counted");
Check(noRoot.ResetCalls == 0 && destroyed.ResetCalls == 0, "invalid chains never receive native reset");
Check(result.FailedCount == 1 && result.FailureMessages.Count == 1, "native failure isolated and reported");
Check(result.FailureMessages[0].Contains("PMX_hair") && result.FailureMessages[0].Contains("injected"), "failure identifies chain");
Check(first.enabled && !off.enabled, "reset never toggles enable state");
Check(first.m_Damping == .37f && first.m_Elasticity == .23f && first.m_Inert == .44f &&
    first.m_Stiffness == .61f && first.m_Radius == .012f && first.m_UpdateRate == 47, "editor parameters preserved");
Check(first.m_Force == 12 && first.m_Gravity == 34 && first.Weight == .65f, "Boop force, gravity and weight preserved");
Check(ReferenceEquals(first.m_Root, sourceRoot) && ReferenceEquals(first.m_Colliders, sourceColliders) &&
    ReferenceEquals(first.m_Particles, sourceParticles) && ReferenceEquals(first.m_DampingDistrib, sourceCurve),
    "root, colliders, particle table and distribution references preserved");
Check(first.SetupCalls == 0 && first.InitCalls == 0, "reset does not rebuild particles or overwrite rest transforms");
Check(first.m_Root.Position == sourcePosition && first.m_Root.Rotation == sourceRotation, "helper never writes bone pose");
Console.WriteLine("PASS: " + checks + " physics reset API/behavior checks. The native solver is a reference stub; motion stability and particle velocity reset require game testing.");

public class InertObject
{
    public bool Destroyed;
    public static implicit operator bool(InertObject value) => value != null && !value.Destroyed;
}
public class InertGameObject { public bool activeInHierarchy = true; }
public class InertTransform : InertObject
{
    public string name = "PMX_hair";
    public InertGameObject gameObject = new();
    public int Position = 17, Rotation = 23;
}
public class DynamicBone : InertObject
{
    public bool enabled = true, ThrowOnReset;
    public InertGameObject gameObject = new();
    public InertTransform m_Root = new();
    public object m_Particles = new(), m_Colliders = new(), m_DampingDistrib = new();
    public float m_Damping = .37f, m_Elasticity = .23f, m_Inert = .44f, m_Stiffness = .61f,
        m_Radius = .012f, m_UpdateRate = 47, Weight = .65f;
    public int m_Force = 12, m_Gravity = 34, ResetCalls, InitCalls, SetupCalls;
    public void ResetParticlesPosition()
    {
        ResetCalls++;
        if (ThrowOnReset) throw new InvalidOperationException("injected reset failure");
    }
    public void InitTransforms() => InitCalls++;
    public void SetupParticles() => SetupCalls++;
}
