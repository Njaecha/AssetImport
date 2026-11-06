// Engine doubles with explicit rest attraction, stiffness, damping, lengths,
// collision and skipped-step constraints. This is not the native DynamicBone
// solver; the production IL rewrite and native API are checked separately.
using AssetImport;
using UnityEngine;

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static implicit operator bool(Object value) => value != null && !value.Destroyed;
    }
    public class GameObject : Object
    {
        public readonly Transform transform;
        public bool activeInHierarchy = true;
        private readonly List<MonoBehaviour> components = new();
        public GameObject() { transform = new Transform { gameObject = this }; }
        public T AddComponent<T>() where T : MonoBehaviour, new()
        {
            var c = new T { gameObject = this }; components.Add(c); return c;
        }
        public T GetComponent<T>() where T : MonoBehaviour => components.OfType<T>().FirstOrDefault();
    }
    public class MonoBehaviour : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
        public bool enabled = true;
        public bool isActiveAndEnabled => enabled && gameObject.activeInHierarchy;
    }
    public class Transform : Object
    {
        public GameObject gameObject;
        public string name = "cloth";
        public Transform parent;
        public Vector3 position;
        public Vector3 lossyScale = new(1, 1, 1);
        public Quaternion rotation = Quaternion.identity;
        public bool IsChildOf(Transform other) => this == other || (parent && parent.IsChildOf(other));
        public Vector3 TransformVector(Vector3 value) => Vector3.From(System.Numerics.Vector3.Transform(
            new System.Numerics.Vector3(value.x * lossyScale.x, value.y * lossyScale.y, value.z * lossyScale.z), rotation.Value));
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public float sqrMagnitude => x * x + y * y + z * z;
        public float magnitude => MathF.Sqrt(sqrMagnitude);
        public Vector3 normalized => magnitude > 0 ? this * (1f / magnitude) : new();
        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator *(Vector3 a, float f) => new(a.x * f, a.y * f, a.z * f);
        public static Vector3 operator /(Vector3 a, float f) => a * (1f / f);
        public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public static Vector3 Cross(Vector3 a, Vector3 b) => new(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        public static Vector3 From(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);
    }
    public struct Quaternion
    {
        public System.Numerics.Quaternion Value;
        public float x=>Value.X; public float y=>Value.Y; public float z=>Value.Z; public float w=>Value.W;
        public Quaternion(float x,float y,float z,float w) {Value=new(x,y,z,w);}
        public static Quaternion identity => new() { Value = System.Numerics.Quaternion.Identity };
        public static Quaternion Turn(float angle) => new() { Value = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitZ, angle) };
        public static Quaternion Inverse(Quaternion q) => new() { Value = System.Numerics.Quaternion.Inverse(q.Value) };
        public static Quaternion operator *(Quaternion a, Quaternion b) => new() { Value = System.Numerics.Quaternion.Multiply(a.Value, b.Value) };
        public static Vector3 operator *(Quaternion q, Vector3 v) => Vector3.From(System.Numerics.Vector3.Transform(new(v.x, v.y, v.z), q.Value));
        public static Quaternion FromTo(Vector3 from, Vector3 to)
        {
            from = from.normalized; to = to.normalized;
            float dot = Vector3.Dot(from, to);
            if (dot < -.99999f) return Turn(MathF.PI);
            var cross = Vector3.Cross(from, to);
            return new() { Value = System.Numerics.Quaternion.Normalize(new(cross.x, cross.y, cross.z, 1 + dot)) };
        }
        public static Quaternion FromToRotation(Vector3 from, Vector3 to)=>FromTo(from,to);
        public static Quaternion Slerp(Quaternion a,Quaternion b,float t)=>new(){Value=System.Numerics.Quaternion.Slerp(a.Value,b.Value,t)};
        public static float Angle(Quaternion a, Quaternion b) => 2 * MathF.Acos(Math.Min(1, Math.Abs(System.Numerics.Quaternion.Dot(a.Value, b.Value)))) * 180 / MathF.PI;
    }
}

public class DynamicBoneCollider : MonoBehaviour
{
    public Func<Vector3, float, Vector3> Project;
    public void Collide(ref Vector3 position, float radius) { if (Project != null) position = Project(position, radius); }
}

public class DynamicBone : MonoBehaviour
{
    public enum FreezeAxis { None, X, Y, Z }
    public FreezeAxis m_FreezeAxis;
    public class Particle
    {
        public int m_ParentIndex = -1;
        public Transform m_Transform;
        public float m_Radius;
        public Vector3 m_Position, m_PrevPosition, m_EndOffset;
        public float m_Damping = .4f, m_Elasticity = .3f, m_Stiffness = .1f;
        public Vector3 m_InitLocalPosition;
        public Quaternion m_InitLocalRotation = Quaternion.identity;
    }
    public Transform m_Root;
    public List<Particle> m_Particles = new();
    public float m_UpdateRate = 60, m_Weight = 1;
    public bool m_DistantDisabled, ThrowOnUpdate;
    public Vector3 m_Force, m_Gravity;
    public List<DynamicBoneCollider> m_Colliders = new();
    public object m_DampingDistrib = new();
    public int Steps, InitCalls;
    public static Func<DynamicBone, Particle, float> ElasticityRead = (_, p) => p.m_Elasticity;
    public static Func<DynamicBone, Particle, float> StiffnessRead = (_, p) => p.m_Stiffness;
    public static Func<DynamicBone, Particle, float> SkipStiffnessRead = (_, p) => p.m_Stiffness;
    public Action<Particle> Contact;
    public void InitTransforms()
    {
        InitCalls++;
        foreach (var p in m_Particles)
        {
            if (!p.m_Transform) continue;
            p.m_Transform.rotation = p.m_Transform.parent.rotation * p.m_InitLocalRotation;
            if (p.m_ParentIndex >= 0)
                p.m_Transform.position = p.m_Transform.parent.position + p.m_Transform.parent.rotation * p.m_InitLocalPosition;
        }
    }
    public void UpdateDynamicBones(float time)
    {
        if (!AssetPhysicsEnvironment.ShouldSimulate(this)) return;
        if (ThrowOnUpdate) throw new InvalidOperationException("injected");
        Steps++;
        UpdateParticles1();
        AssetPhysicsEnvironment.AfterIntegrate(this);
        UpdateParticles2();
        AssetPhysicsEnvironment.AfterConstraints(this);
        ApplyParticlesToTransforms();
    }
    public void UpdateParticles1()
    {
        foreach (var p in m_Particles)
        {
            if (p.m_ParentIndex < 0)
            { p.m_PrevPosition = p.m_Position; p.m_Position = p.m_Transform.position; continue; }
            var last = p.m_Position;
            p.m_Position += (p.m_Position - p.m_PrevPosition) * (1 - p.m_Damping) + m_Force + m_Gravity;
            p.m_PrevPosition = last;
        }
    }
    private void Constraints(bool skip)
    {
        foreach (var p in m_Particles)
            if (p.m_ParentIndex >= 0)
            {
                var parent = m_Particles[p.m_ParentIndex];
                var rest = p.m_Transform ? p.m_Transform.position - parent.m_Transform.position : parent.m_Transform.TransformVector(p.m_EndOffset);
                float elasticity = skip ? 0 : ElasticityRead(this, p);
                float stiffness = 1 - m_Weight + (skip ? SkipStiffnessRead(this, p) : StiffnessRead(this, p)) * m_Weight;
                var target = parent.m_Position + rest;
                p.m_Position += (target - p.m_Position) * elasticity;
                var toRest = target - p.m_Position;
                float allowed = rest.magnitude * (1 - stiffness) * 2;
                if (toRest.magnitude > allowed) p.m_Position += toRest * (1 - allowed / toRest.magnitude);
                Contact?.Invoke(p);
                p.m_Position = parent.m_Position + (p.m_Position - parent.m_Position).normalized * rest.magnitude;
            }
    }
    public void UpdateParticles2() => Constraints(false);
    public void SkipUpdateParticles() { if (!AssetPhysicsEnvironment.ShouldSimulate(this)) return; Constraints(true); AssetPhysicsEnvironment.AfterConstraints(this,false); ApplyParticlesToTransforms(); }
    public void ApplyParticlesToTransforms()
    {
        foreach (var p in m_Particles)
        {
            if (p.m_ParentIndex < 0) continue;
            var parent = m_Particles[p.m_ParentIndex];
            var rest = parent.m_Transform.rotation * (p.m_Transform ? p.m_InitLocalPosition : p.m_EndOffset);
            parent.m_Transform.rotation = Quaternion.FromTo(rest, p.m_Position - parent.m_Position) * parent.m_Transform.rotation;
            if (p.m_Transform) p.m_Transform.position = p.m_Position;
        }
    }
    public void ResetParticlesPosition()
    {
        foreach (var p in m_Particles)
            p.m_Position = p.m_PrevPosition = p.m_Transform ? p.m_Transform.position : m_Particles[p.m_ParentIndex].m_Transform.position + p.m_EndOffset;
        AssetPhysicsEnvironment.AfterReset(this);
    }
    public void SetupParticles()
    {
        foreach (var p in m_Particles)
        {
            if (!p.m_Transform) continue;
            p.m_InitLocalPosition = Quaternion.Inverse(p.m_Transform.parent.rotation) * (p.m_Transform.position - p.m_Transform.parent.position);
            p.m_InitLocalRotation = Quaternion.Inverse(p.m_Transform.parent.rotation) * p.m_Transform.rotation;
        }
    }
    public void SetEnabled(bool value)
    {
        enabled = value;
        if (value) ResetParticlesPosition(); else InitTransforms();
    }
}

namespace AssetImport
{
    internal static class AssetImport { internal static Log Logger = new(); }
    internal sealed class Log { internal void LogWarning(string message) => Console.WriteLine(message); }
}
namespace MessagePack
{
    [AttributeUsage(AttributeTargets.Class)] public sealed class MessagePackObjectAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)] public sealed class KeyAttribute(string key) : Attribute { public string Key = key; }
}
