using System.Reflection;
using AssetImport;
using UnityEngine;

int checks = 0;
void Check(bool condition, string reason)
{
    checks++;
    if (!condition) throw new Exception(reason);
}

var model = Model(("root", -1), ("head", 0), ("hair1", 1), ("hair2", 2), ("tip", 3),
    ("chain", 0), ("tip", 5), ("plain", 0), ("skirt_branch", 7), ("tip", 8),
    ("tail", 0), ("tail2", 10), ("headdress", 1), ("tip", 12), ("DressBone", 0), ("tip", 14));
model.RigidBodyData = [Body(2, "hair"), Body(3, "hair"), Body(5, "front_スカート"), Body(7, "chain"),
    Body(8, "branch"), Body(10, "tail"), Body(12, "headdress"), Body(14, "DressBone")];
var suggestions = PmxPhysicsRootSuggestions.Build(model);
Check(PmxPhysicsPresets.For(model, 2, suggestions).Name == "hair", "safe hair suggestion chooses hair baseline");
Check(PmxPhysicsPresets.For(model, 3, suggestions).Name == "hair", "manually selected child inherits suggested hair chain type");
Check(PmxPhysicsPresets.For(model, 5, suggestions).Name == "skirt", "dynamic rigid-body skirt label is sufficient");
Check(PmxPhysicsPresets.For(model, 7, suggestions).Name == "skirt", "nonfirst subtree branch can identify skirt");
Check(PmxPhysicsPresets.For(model, 10, suggestions).Name == "accessory", "unknown dynamic tail retains general baseline");
Check(PmxPhysicsPresets.For(model, 12, suggestions).Name == "accessory", "headdress does not match dress");
Check(PmxPhysicsPresets.For(model, 14, suggestions).Name == "accessory", "ambiguous DressBone does not match skirt");
Check(PmxPhysicsPresets.For(model, -1, suggestions) == null && PmxPhysicsPresets.For(null, 2, suggestions) == null,
    "invalid model/root has no preset");
Check(PmxPhysicsPresets.For(model, 999, suggestions) == null, "out-of-range root has no preset");
Check(PmxPhysicsPresets.For(model, 2, null).Name == "hair", "optional shared suggestions rebuild when omitted");
foreach (float scale in new[] { .001f, 1f, 1000f })
{
    foreach (var bone in model.Bones) bone.Position = new PmxModel.V3(scale, 5f * scale, -scale);
    Check(PmxPhysicsPresets.For(model, 2, suggestions).Name == "hair" && PmxPhysicsPresets.For(model, 7, suggestions).Name == "skirt",
        "name/hierarchy classification does not depend on arbitrary model scale: " + scale);
}
foreach (string name in new[] { "前裙_1", "裾_1", "Skirt_Back", "dress_01", "dress1", "side dress 3" })
{
    var named = Model(("root", -1), (name, 0), ("tip", 1));
    named.RigidBodyData = [Body(1, "body")];
    Check(PmxPhysicsPresets.For(named, 1, null).Name == "skirt", "explicit skirt label recognized: " + name);
}
var foreignBranch = Model(("root", -1), ("unnamed", 0), ("tip", 1), ("裙", 0), ("tip", 3));
foreignBranch.RigidBodyData = [Body(1, "body"), Body(3, "body")];
Check(PmxPhysicsPresets.For(foreignBranch, 1, null).Name == "accessory", "unrelated skirt branch does not contaminate classification");
var staticLabel = Model(("root", -1), ("chain", 0), ("tip", 1));
staticLabel.RigidBodyData = [new PmxModel.RigidBody { Bone = 1, Name = "skirt collider", Mode = 0 }];
Check(PmxPhysicsPresets.For(staticLabel, 1, null).Name == "accessory", "static collision-body label alone is not a skirt physics chain");
var cycle = Model(("chain1", 1), ("chain2", 0));
Check(PmxPhysicsPresets.For(cycle, 0, null).Name == "accessory", "cyclic ancestry terminates safely");

foreach (int root in new[] { 2, 5, 10 })
{
    var preset = PmxPhysicsPresets.For(model, root, suggestions);
    var chain = new DynamicBone();
    var originalCurve = chain.m_DampingDistrib;
    foreach (int profile in new[] { -1, 0, 2, int.MaxValue })
        Check(!preset.Apply(chain, profile) && chain.m_UpdateRate == 31 && chain.m_Damping == .91f &&
            ReferenceEquals(chain.m_DampingDistrib, originalCurve), "unsupported/legacy profile leaves component untouched: " + profile);
    Check(!preset.Apply(null, 1), "null component is ignored");
    Check(preset.Apply(chain, 1) && chain.m_UpdateRate == 60f, "profile one applies explicit sample update rate");
    Check(chain.m_Damping >= 0 && chain.m_Damping <= 1 && chain.m_Elasticity >= 0 && chain.m_Elasticity <= 1 &&
        chain.m_Stiffness >= 0 && chain.m_Stiffness <= 1 && chain.m_Inert >= 0 && chain.m_Inert <= 1,
        "preset coefficients stay within native range");
    Check(chain.m_Radius == .012f && chain.m_EndLength == .75f && chain.m_Force == 13 && chain.m_Gravity == 19 &&
        chain.m_Root == 23 && chain.m_Colliders == 29 && chain.m_Exclusions == 37 && !chain.enabled && chain.m_Weight == .42f,
        "profile does not overwrite collision, pose, switch, force, gravity, exclusion or weight settings");
    Check(chain.m_InertDistrib == null, "inertia has no fabricated curve");
    foreach (var curve in new[] { chain.m_DampingDistrib, chain.m_ElasticityDistrib, chain.m_StiffnessDistrib }.Where(c => c != null))
    {
        Check(curve.keys.Length == 2 && curve.keys[0].time == 0 && curve.keys[1].time == 1,
            "curve spans normalized bone length");
        float slope = curve.keys[1].value - curve.keys[0].value;
        Check(curve.keys.All(k => k.inTangent == slope && k.outTangent == slope), "explicit equal tangents cannot overshoot");
        for (int i = 0; i <= 10; i++)
        {
            float value = curve.Evaluate(i / 10f);
            Check(value >= curve.keys[1].value - .00001f && value <= 1.00001f, "distribution stays within endpoint range");
        }
    }
    var other = new DynamicBone(); preset.Apply(other, 1);
    Check(!ReferenceEquals(chain.m_ElasticityDistrib, other.m_ElasticityDistrib) &&
        !ReferenceEquals(chain.m_StiffnessDistrib, other.m_StiffnessDistrib), "each component owns independent curves");
    if (chain.m_DampingDistrib != null)
        Check(!ReferenceEquals(chain.m_DampingDistrib, other.m_DampingDistrib), "damping curves are not shared");
    chain.m_ElasticityDistrib.keys[0] = new Keyframe(0, .5f, 0, 0);
    Check(other.m_ElasticityDistrib.keys[0].value == 1, "editing a component curve cannot alter another chain");
}

foreach (string path in new[] { "Fixtures/KK/Assembly-CSharp.dll",
    "Fixtures/KKS/Assembly-CSharp.dll" })
{
    using var game = new MetadataAssembly(Path.Combine(AppContext.BaseDirectory, path));
    var fields = game.Reader.GetTypeDefinition(game.FindType("DynamicBone")).GetFields()
        .Select(h => game.Reader.GetFieldDefinition(h)).ToDictionary(f => game.Reader.GetString(f.Name));
    foreach (string name in new[] { "m_UpdateRate", "m_Damping", "m_Elasticity", "m_Stiffness", "m_Inert" })
        Check(fields.TryGetValue(name, out var field) && (field.Attributes & FieldAttributes.Public) != 0 &&
            field.DecodeSignature(game.Names, null) == "System.Single", "native float field API: " + name);
    foreach (string name in new[] { "m_DampingDistrib", "m_ElasticityDistrib", "m_StiffnessDistrib", "m_InertDistrib" })
        Check(fields.TryGetValue(name, out var field) && (field.Attributes & FieldAttributes.Public) != 0 &&
            field.DecodeSignature(game.Names, null) == "UnityEngine.AnimationCurve", "native curve field API: " + name);
}
foreach (string path in args.Skip(1))
{
    var fixture = PmxReader.Read(File.ReadAllBytes(path));
    var roots = PmxPhysicsRootSuggestions.Build(fixture);
    var presets = roots.PhysicsRoots.Select(root => PmxPhysicsPresets.For(fixture, root, roots)).ToArray();
    Check(presets.All(preset => preset != null && preset.Apply(new DynamicBone(), 1)), "every usable fixture chain receives one applicable preset");
    Console.WriteLine("MODEL " + Path.GetFileName(path) + ": " + string.Join(", ", presets.GroupBy(preset => preset.Name)
        .OrderBy(group => group.Key).Select(group => group.Key + "=" + group.Count())) + "; chains=" + presets.Length);
}
Console.WriteLine("PASS: " + checks + " physics preset classification/curve/API checks. These are demo-derived initial settings; Unity simulation quality requires game testing.");

static PmxModel Model(params (string Name, int Parent)[] bones) => new()
{
    Bones = bones.Select(b => new PmxModel.Bone { Name = b.Name, Parent = b.Parent }).ToArray()
};
static PmxModel.RigidBody Body(int bone, string name) => new() { Bone = bone, Name = name, Mode = 1 };

public class DynamicBone
{
    public static implicit operator bool(DynamicBone value) => value != null;
    public float m_UpdateRate = 31, m_Damping = .91f, m_Elasticity = .92f, m_Stiffness = .93f, m_Inert = .94f;
    public AnimationCurve m_DampingDistrib = new(new Keyframe(0, 1, 0, 0)), m_ElasticityDistrib, m_StiffnessDistrib, m_InertDistrib;
    public float m_Radius = .012f, m_EndLength = .75f, m_Weight = .42f;
    public int m_Force = 13, m_Gravity = 19, m_Root = 23, m_Colliders = 29, m_Exclusions = 37;
    public bool enabled;
}
namespace UnityEngine
{
    public struct Keyframe
    {
        public float time, value, inTangent, outTangent;
        public Keyframe(float t, float v, float i, float o) { time = t; value = v; inTangent = i; outTangent = o; }
    }
    public class AnimationCurve
    {
        public Keyframe[] keys;
        public AnimationCurve(params Keyframe[] values) => keys = values;
        public float Evaluate(float t)
        {
            var a = keys[0]; var b = keys[1]; float u = (t - a.time) / (b.time - a.time);
            float u2 = u * u, u3 = u2 * u, width = b.time - a.time;
            return (2 * u3 - 3 * u2 + 1) * a.value + (u3 - 2 * u2 + u) * width * a.outTangent +
                (-2 * u3 + 3 * u2) * b.value + (u3 - u2) * width * b.inTangent;
        }
    }
}
