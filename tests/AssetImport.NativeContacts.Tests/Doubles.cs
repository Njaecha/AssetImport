// Lifecycle/API doubles only. Native collision response is deliberately injected,
// not emulated; these tests cannot establish the game's physical behavior.
using AssetImport;
using UnityEngine;

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static implicit operator bool(Object value) => value != null && !value.Destroyed;
        public static void Destroy(Object value) { if (value != null) value.Destroyed = true; }
    }
    public class GameObject : Object
    {
        private readonly List<MonoBehaviour> _components = new();
        public readonly Transform transform;
        public bool activeSelf = true;
        public bool activeInHierarchy => !Destroyed && activeSelf && (!transform.parent || transform.parent.gameObject.activeInHierarchy);
        public GameObject(string name = "") { transform = new Transform { gameObject = this, name = name }; }
        public void SetActive(bool active) { activeSelf = active; }
        public T AddComponent<T>() where T : MonoBehaviour, new() { var c = new T { gameObject = this }; _components.Add(c); return c; }
        public T GetComponent<T>() where T : MonoBehaviour => _components.OfType<T>().FirstOrDefault();
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : MonoBehaviour =>
            (!includeInactive && !activeInHierarchy ? [] : _components.OfType<T>().Concat(
                transform.Children.SelectMany(t => t.gameObject.GetComponentsInChildren<T>(includeInactive)))).ToArray();
    }
    public class MonoBehaviour : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
        public string name => transform.name;
        public bool enabled = true;
        public bool isActiveAndEnabled => enabled && gameObject.activeInHierarchy;
        public T GetComponent<T>() where T : MonoBehaviour => gameObject.GetComponent<T>();
    }
    public sealed class Transform : Object
    {
        public GameObject gameObject;
        public string name;
        public Transform parent;
        public readonly List<Transform> Children = new();
        public Vector3 localPosition, localScale = new(1, 1, 1);
        public Quaternion localRotation = Quaternion.identity;
        public Vector3 lossyScale => parent ? Vector3.Scale(parent.lossyScale, localScale) : localScale;
        public Vector3 position { get => parent ? parent.TransformPoint(localPosition) : localPosition; set => localPosition = parent ? parent.InverseTransformPoint(value) : value; }
        public Quaternion rotation { get => parent ? parent.rotation * localRotation : localRotation; set => localRotation = parent ? Quaternion.Inverse(parent.rotation) * value : value; }
        public void SetParent(Transform value, bool keep = true) { var p = position; var q = rotation; parent?.Children.Remove(this); parent = value; parent?.Children.Add(this); if (keep) { position = p; rotation = q; } }
        public bool IsChildOf(Transform value) => this == value || (parent && parent.IsChildOf(value));
        public Vector3 TransformPoint(Vector3 value) => position + rotation * Vector3.Scale(value, lossyScale);
        public Vector3 InverseTransformPoint(Vector3 value) { var v = Quaternion.Inverse(rotation) * (value - position); var s = lossyScale; return new(v.x / s.x, v.y / s.y, v.z / s.z); }
    }
    public struct Vector3(float x, float y, float z)
    {
        public float x = x, y = y, z = z;
        public static Vector3 zero => new();
        public float sqrMagnitude => x * x + y * y + z * z;
        public float magnitude => MathF.Sqrt(sqrMagnitude);
        public Vector3 normalized => magnitude > 0 ? this / magnitude : zero;
        public static Vector3 Scale(Vector3 a, Vector3 b) => new(a.x * b.x, a.y * b.y, a.z * b.z);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator *(Vector3 v, float n) => new(v.x * n, v.y * n, v.z * n);
        public static Vector3 operator /(Vector3 v, float n) => v * (1 / n);
    }
    public struct Quaternion
    {
        private System.Numerics.Quaternion _v;
        public Quaternion(float x, float y, float z, float w) { _v = new(x, y, z, w); }
        public static Quaternion identity => new(0, 0, 0, 1);
        public static Quaternion Euler(float x, float y, float z)
        {
            float k = MathF.PI / 360;
            return new Quaternion(0, MathF.Sin(y * k), 0, MathF.Cos(y * k)) *
                new Quaternion(MathF.Sin(x * k), 0, 0, MathF.Cos(x * k)) *
                new Quaternion(0, 0, MathF.Sin(z * k), MathF.Cos(z * k));
        }
        public static Quaternion Inverse(Quaternion q) => new() { _v = System.Numerics.Quaternion.Inverse(q._v) };
        public static Quaternion operator *(Quaternion a, Quaternion b) => new() { _v = a._v * b._v };
        public static Vector3 operator *(Quaternion q, Vector3 v) { var r = System.Numerics.Vector3.Transform(new(v.x, v.y, v.z), q._v); return new(r.X, r.Y, r.Z); }
    }
    public class Camera : Object
    {
        public static event Action<Camera> onPreRender;
        public static int Observers => onPreRender?.GetInvocationList().Length ?? 0;
        public static void Render() => onPreRender?.Invoke(new());
    }
    public class Mesh : Object { public string name; public int vertexCount; }
    public class SkinnedMeshRenderer : MonoBehaviour { public Transform[] bones = []; public Mesh sharedMesh; }
    public static class Time { public static int frameCount; public static float deltaTime = 1f / 60, realtimeSinceStartup; }
    public static class Application { public static string unityVersion = "test-double", dataPath = "test-game-data"; }
    public static class Resources
    {
        public static Object[] Items = [];
        public static bool FailEnumeration;
        public static T[] FindObjectsOfTypeAll<T>() where T : Object => FailEnumeration ? throw new TypeLoadException("injected type load failure") : Items.OfType<T>().Where(i => i).ToArray();
    }
    public static class Debug
    {
        public static readonly List<string> Lines = new();
        public static void Log(string text) => Lines.Add(text);
        public static void LogWarning(string text) => Lines.Add(text);
    }
    [AttributeUsage(AttributeTargets.Class)] public sealed class DefaultExecutionOrder(int order) : Attribute { public int Order = order; }
    [AttributeUsage(AttributeTargets.Field)] public sealed class SerializeField : Attribute { }
}
public sealed class DynamicBoneCollider : MonoBehaviour
{
    public enum Direction { X, Y, Z }
    public enum Bound { Outside, Inside }
    public float m_Radius, m_Height;
    public Direction m_Direction;
    public Bound m_Bound;
}
public sealed class DynamicBone : MonoBehaviour { public Transform m_Root; public float m_Weight = 1; }
public sealed class DynamicBone_Ver02 : MonoBehaviour
{
    public sealed class BoneParameter
    {
        public string Name; public Transform RefTransform;
        public float Damping, Elasticity, Stiffness, Inert, CollisionRadius, NextBoneLength, KeepLengthLimitMin, KeepLengthLimitMax;
        public bool IsRotationCalc, IsMoveLimit, IsCrush;
    }
    public sealed class ParticlePtn { }
    public sealed class BonePtn
    {
        public string Name; public Vector3 Gravity, EndOffset;
        public float EndOffsetDamping, EndOffsetElasticity, EndOffsetStiffness, EndOffsetInert;
        public List<BoneParameter> Params; public List<ParticlePtn> ParticlePtns;
    }
    public sealed class Particle { public Transform Transform; public int ParentIndex; public float Radius; }
    public string Comment; public Transform Root; public List<Transform> Bones;
    public List<DynamicBoneCollider> Colliders;
    public Vector3 Gravity, Force;
    public float UpdateRate, ReflectSpeed, Weight; public int HeavyLoopMaxCount, PtnNo;
    public List<BonePtn> Patterns; public List<Particle> Particles;
    public static Vector3 Output;
    public static bool AccumulateOutput;
    private Vector3 _injectedCarry;
    public static readonly List<DynamicBone_Ver02> Created = new();
    public Vector3 LastResetBodyPosition { get; private set; }
    public int ResetCalls { get; private set; }
    public static bool FailSetup, FailStep, FailCalibration;
    public static int Steps, Instances;
    public DynamicBone_Ver02() { Instances++; Created.Add(this); }
    private void InitNodeParticle() { }
    private void InitLocalPosition() { }
    private bool setPtn(int pattern, bool force) => true;
    private void SetupParticles()
    {
        if (FailSetup) return;
        Particles = Bones.Select((b, i) => new Particle { Transform = b, ParentIndex = i - 1, Radius = Patterns[0].Params[i].CollisionRadius }).ToList();
    }
    private void ResetParticlesPosition()
    {
        LastResetBodyPosition = Bones[1].position; ResetCalls++;
        _injectedCarry = Bones[1].position - Patterns[0].Params[1].RefTransform.position;
    }
    private void InitTransforms() { Bones[1].localPosition = Patterns[0].Params[1].RefTransform.localPosition; }
    private void UpdateDynamicBones(float elapsed)
    {
        if (enabled || gameObject.activeInHierarchy) throw new Exception("proxy must not have an automatic update loop");
        if (FailStep) throw new Exception("injected native failure");
        // Explicit test response, not a native physics implementation.
        bool calibration = Colliders.Count == 1 && Colliders[0].transform.name == "AI_ContactValidation";
        var output = Colliders.Count == 0 ? Vector3.zero : calibration ?
            new Vector3(FailCalibration ? 0 : Patterns[0].Params[1].CollisionRadius * .2f, 0, 0) : Output;
        if (AccumulateOutput && !calibration && Colliders.Count > 0)
        { output += _injectedCarry; _injectedCarry = output; }
        Bones[1].position += output; Steps++;
    }
    private void ApplyParticlesToTransforms() { }
}
namespace AssetImport
{
    internal sealed class AssetPhysicsEnvironment : MonoBehaviour
    {
        public PhysicsEnvironmentSettings Settings = new(); public DynamicBone[] Chains = [];
        internal PhysicsChainMode Mode(DynamicBone chain) => (PhysicsChainMode)(Settings.ForRoot(chain.m_Root.name)?.Mode ?? 1);
        internal static event Action<DynamicBone> ResetRequested;
        internal static void Reset(DynamicBone chain) => ResetRequested?.Invoke(chain);
    }
    internal static class AssetImport { internal static Log Logger = new(); internal const string PluginName = "AssetImport test"; }
    internal sealed class Log
    {
        internal readonly List<string> Lines = new();
        internal void LogInfo(string text) { Lines.Add(text); }
        internal void LogWarning(string text) { Lines.Add(text); }
    }
}
namespace BepInEx
{
    public static class Paths { public static string GameRootPath; }
}
namespace MessagePack
{
    [AttributeUsage(AttributeTargets.Class)] public sealed class MessagePackObjectAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)] public sealed class KeyAttribute(string key) : Attribute { public string Key = key; }
}
