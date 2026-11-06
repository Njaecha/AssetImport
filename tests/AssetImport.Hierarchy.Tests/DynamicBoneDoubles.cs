// Field-only doubles for testing selection rules, not physics simulation.
namespace UnityEngine
{
    public struct Vector3 { public static Vector3 zero => new(); }
    public struct Keyframe { public Keyframe(float time, float value) { } }
    public class AnimationCurve { public AnimationCurve(Keyframe[] keys) { } }
    public class RangeAttribute : Attribute { public RangeAttribute(float min, float max) { } }
}
public class DynamicBoneCollider { }
public class DynamicBone
{
    public UnityEngine.Transform m_Root;
    public List<UnityEngine.Transform> m_notRolls;
    public List<DynamicBoneCollider> m_Colliders;
    public float m_Elasticity, m_Damping, m_Stiffness, m_Inert, m_Radius;
    public UnityEngine.AnimationCurve m_ElasticityDistrib, m_DampingDistrib, m_StiffnessDistrib, m_InertDistrib, m_RadiusDistrib;
}
