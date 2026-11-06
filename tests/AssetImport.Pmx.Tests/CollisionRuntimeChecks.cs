using AssetImport;
using UnityEngine;

// Execute the production binding/lifecycle method against inert engine objects.
// This deliberately does NOT simulate DynamicBone's integration/contact solver.
internal static class CollisionRuntimeChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var model=new PmxModel { Bones=new[] {
            new PmxModel.Bone { Name="head", Parent=-1, Position=new PmxModel.V3(2,10,4) },
            new PmxModel.Bone { Name="hair", Parent=0 },
            new PmxModel.Bone { Name="tip", Parent=1 }
        }, RigidBodyData=new[] {
            new PmxModel.RigidBody { Name="head body", Bone=0, Mode=0, Group=0, CollisionMask=0xffff, Shape=0, Size=new PmxModel.V3(1,0,0), Position=new PmxModel.V3(3,12,7) },
            new PmxModel.RigidBody { Name="hair body", Bone=1, Mode=1, Group=1, CollisionMask=0xffff, Shape=2, Size=new PmxModel.V3(.2f,1,0) }
        }};
        var first=new Import(model); var chain=first.Chain(1);
        int initialTransforms=first.GameObject.GetComponentsInChildren<Transform>(true).Length;
        first.ApplyPmxBodyCollisions(new[]{chain},new Vector3(2,2,2),true);
        check(chain.m_Colliders.Count==1,"PMX runtime binding assigns the body's actual collider");
        var collider=chain.m_Colliders.Single();
        check(collider.transform.parent==first.Bone(0),"collider follows the animated body's bone, not the dynamic hair");
        check(Math.Abs(collider.transform.localPosition.x + .0832f)<1e-6 && Math.Abs(collider.transform.localPosition.y-.1664f)<1e-6,"rest-space body offset is converted once");
        check(collider.m_Bound==DynamicBoneCollider.Bound.Outside && collider.m_Center.x==0,"outside bound and centered on dedicated collider node");
        check(Math.Abs(chain.m_Radius-.2f*.0832f*2)<1e-6,"particle radius accounts for inner import scale on an outer DynamicBone component");
        var oldCollider=collider;
        first.ApplyPmxBodyCollisions(new[]{chain},new Vector3(2,2,2),true);
        check(!oldCollider && chain.m_Colliders.Count==1 && first.GameObject.GetComponentsInChildren<Transform>(true).Length==initialTransforms+1,"repeat binding removes owned nodes and references, no accumulation");
        var external=new GameObject("user collider").AddComponent<DynamicBoneCollider>(); chain.m_Colliders.Add(external);
        first.ApplyPmxBodyCollisions(new[]{chain},new Vector3(2,2,2),false);
        check(chain.m_Colliders.SequenceEqual(new[]{external}) && external,"disable removes only importer-owned collision objects");
        check(first.GameObject.GetComponentsInChildren<Transform>(true).Length==initialTransforms,"disable restores the original skeleton node count");
        first.ApplyPmxBodyCollisions(new[]{chain},Vector3.one,true);
        var second=new Import(model); var secondChain=second.Chain(1);
        second.ApplyPmxBodyCollisions(new[]{secondChain},Vector3.one,true);
        check(secondChain.m_Colliders.Single()!=chain.m_Colliders.Last() && secondChain.m_Colliders.Single().transform.parent==second.Bone(0),"same model in another item has its own collision objects");
        check(secondChain.m_Colliders.Single().m_Radius==chain.m_Colliders.Last().m_Radius,"reconstructed model yields the same body radius");
        var whole=second.Chain(-1);
        second.ApplyPmxBodyCollisions(new[]{whole},Vector3.one,true);
        check(whole.m_Colliders.Count==0 && second.GameObject.GetComponentsInChildren<Transform>(true).Length==initialTransforms,"whole-model root rejected before inserting collider children into its simulated tree");
        var otherFormat=new Import(model,false); var otherChain=otherFormat.Chain(1);
        otherFormat.ApplyPmxBodyCollisions(new[]{otherChain},Vector3.one,true);
        check(otherChain.m_Colliders.Count==0,"non-PMX import stays untouched");
        ScaleChecks(check, model);
        HairAssistChecks(check);
    }

    private static void HairAssistChecks(Action<bool,string> check)
    {
        var model = new PmxModel { Bones = new[] {
            new PmxModel.Bone { Name="root", Parent=-1 },
            new PmxModel.Bone { Name="head", Parent=0 },
            new PmxModel.Bone { Name="hair 2", Parent=1 },
            new PmxModel.Bone { Name="hair 3", Parent=2 }
        }, RigidBodyData = new[] {
            new PmxModel.RigidBody { Name="head", Bone=1, Mode=0, Group=0, CollisionMask=0, Shape=0, Size=new PmxModel.V3(1,0,0) },
            new PmxModel.RigidBody { Name="hair 2", Bone=2, Mode=1, Group=2, CollisionMask=0xffda, Shape=0, Size=new PmxModel.V3(.5f,0,0) }
        }};
        var import = new Import(model); var chain = import.Chain(2);
        import.ApplyPmxBodyCollisions(new[]{chain},Vector3.one,true,1);
        check(chain.m_Colliders.Count==0,"runtime collision assist is off unless explicitly requested");
        import.ApplyPmxBodyCollisions(new[]{chain},Vector3.one,true,1,true);
        check(chain.m_Colliders.Count==1 && chain.m_Colliders.Single().transform.parent==import.Bone(1),
            "runtime hair assist creates an actual collider on the animated body bone");
        var external = new GameObject("user collider").AddComponent<DynamicBoneCollider>();
        chain.m_Colliders.Add(external);
        var assisted = chain.m_Colliders.First();
        import.ApplyPmxBodyCollisions(new[]{chain},Vector3.one,true,1,false);
        check(!assisted && chain.m_Colliders.SequenceEqual(new[]{external}),
            "turning off assist reconstructs original PMX filtering without removing a user-added collider");
        import.ApplyPmxBodyCollisions(new[]{chain},Vector3.one,true,1,true);
        import.ApplyPmxBodyCollisions(new[]{chain},Vector3.one,true,1,true);
        check(chain.m_Colliders.Count==2 && chain.m_Colliders.Distinct().Count()==2,
            "repeat hair assist binding does not duplicate generated or external collider references");
    }

    private static void ScaleChecks(Action<bool,string> check, PmxModel model)
    {
        const float pmxRadius=.2f*.0832f;
        // Real Unity evaluates lossyScale after rotations as well. These inert
        // transforms cover uniform nested scales and mirrors only; they do not
        // execute the DynamicBone contact solver or test sheared hierarchies.
        (Import import, DynamicBone chain) Fixture(float hostScale, float moveScale, float modelScale)
        {
            var import=new Import(model); var chain=import.Chain(1);
            chain.transform.localScale=new Vector3(hostScale,hostScale,hostScale);
            var move=new GameObject("N_move"); move.transform.SetParent(chain.transform,false);
            move.transform.localScale=new Vector3(moveScale,moveScale,moveScale);
            import.GameObject.transform.SetParent(move.transform,false);
            import.GameObject.transform.localScale=new Vector3(modelScale,modelScale,modelScale);
            return (import,chain);
        }
        var direct=Fixture(1,1,2);
        direct.import.ApplyPmxBodyCollisions(new[]{direct.chain},new Vector3(2,2,2),true,1);
        check(Math.Abs(direct.chain.m_Radius-pmxRadius*2)<1e-6,
            "profile 1 counts the final inner import scale exactly once");

        var maker=Fixture(3,4,2);
        maker.import.ApplyPmxBodyCollisions(new[]{maker.chain},new Vector3(2,2,2),true,1);
        check(Math.Abs(maker.chain.m_Radius-pmxRadius*8)<1e-6,
            "profile 1 includes Maker's N_move scale between model and DynamicBone host");
        float worldParticle=maker.chain.m_Radius*Math.Abs(maker.chain.transform.lossyScale.x);
        check(Math.Abs(worldParticle-pmxRadius*Math.Abs(maker.import.GameObject.transform.lossyScale.x))<1e-6,
            "particle radius in host units matches the PMX model's world-space thickness");
        float worldObstacle=maker.chain.m_Colliders.Single().m_Radius*
            Math.Abs(maker.chain.m_Colliders.Single().transform.lossyScale.x);
        check(Math.Abs(worldParticle/worldObstacle-.2f)<1e-6,
            "particle-to-body thickness ratio is unchanged by outer and N_move scaling");

        var mirror=Fixture(-3,4,-2);
        mirror.import.ApplyPmxBodyCollisions(new[]{mirror.chain},new Vector3(-2,-2,-2),true,1);
        check(Math.Abs(mirror.chain.m_Radius-maker.chain.m_Radius)<1e-6,
            "mirrored transform scales do not produce negative collision thickness");
        var legacy=Fixture(3,4,2);
        legacy.import.ApplyPmxBodyCollisions(new[]{legacy.chain},new Vector3(2,2,2),true,0);
        check(Math.Abs(legacy.chain.m_Radius-pmxRadius*2)<1e-6,
            "profile 0 preserves the pre-4.2.6 radius conversion for saved scenes");
        var unknown=Fixture(3,4,2);
        unknown.import.ApplyPmxBodyCollisions(new[]{unknown.chain},new Vector3(2,2,2),true,99);
        check(Math.Abs(unknown.chain.m_Radius-legacy.chain.m_Radius)<1e-6,
            "unknown physics profile does not silently opt into profile 1 radius conversion");

        var manual=Fixture(3,4,2);
        var curve=new AnimationCurve(new[]{new Keyframe(0,.25f),new Keyframe(1,1)});
        manual.chain.m_Radius=.125f; manual.chain.m_RadiusDistrib=curve;
        manual.import.ApplyPmxBodyCollisions(new[]{manual.chain},new Vector3(2,2,2),true,1);
        check(manual.chain.m_Radius==.125f && ReferenceEquals(manual.chain.m_RadiusDistrib,curve),
            "profile 1 does not replace an explicitly configured positive radius or its distribution");
        check(manual.chain.m_Colliders.Count==1,
            "preserving the configured particle radius still binds permitted PMX body colliders");

        foreach (var scales in new[]{
            new[]{0f,1f,2f}, new[]{1f,1f,0f}, new[]{float.NaN,1f,2f},
            new[]{float.PositiveInfinity,1f,2f}, new[]{1f,1f,float.PositiveInfinity},
            new[]{1e-20f,1e38f,1e20f}
        })
        {
            var invalid=Fixture(scales[0],scales[1],scales[2]);
            invalid.import.ApplyPmxBodyCollisions(new[]{invalid.chain},Vector3.one,true,1);
            check(invalid.chain.m_Radius==0,
                "zero/nonfinite scale or conversion overflow never installs a nonfinite particle radius");
        }
    }
}

namespace AssetImport
{
    public partial class Import
    {
        private PmxModel _pmx;
        internal bool IsPmx;
        internal bool ImportBones=true;
        public GameObject GameObject {get;}
        public List<BoneNode> BoneNodes {get;} = new();
        private static readonly CollisionLogger Logger=new();
        internal string SourceFileName=>"original physics fixture.pmx";
        internal Import(PmxModel model,bool pmx=true)
        {
            _pmx=model;IsPmx=pmx;GameObject=new GameObject("PMX_Model");
            var bones=model.Bones.Select((b,i)=>new GameObject(PmxModel.BoneName(i,b.Name))).ToArray();
            for(int i=0;i<bones.Length;i++) bones[i].transform.SetParent(model.Bones[i].Parent<0?GameObject.transform:bones[model.Bones[i].Parent].transform,false);
            void AddNodes(Transform transform, BoneNode parent)
            {
                var node = new BoneNode(transform.gameObject, parent, parent == null ? 0 : parent.Depth + 1);
                BoneNodes.Add(node);
                foreach (var child in transform.children) AddNodes(child, node);
            }
            foreach (var root in GameObject.transform.children) AddNodes(root, null);
        }
        internal Transform Bone(int index)=>GameObject.GetComponentsInChildren<Transform>(true).Single(t=>t.name==PmxModel.BoneName(index,_pmx.Bones[index].Name));
        internal DynamicBone Chain(int index)
        {
            var chain=new GameObject("outer item").AddComponent<DynamicBone>();
            chain.m_Root=index<0?GameObject.transform:Bone(index);return chain;
        }
    }
    internal class CollisionLogger { public void LogInfo(string value){} public void LogDebug(string value){} public void LogWarning(string value){} }
}

namespace UnityEngine
{
    public class Object
    {
        internal bool destroyed;
        public static implicit operator bool(Object value)=>value is not null && !value.destroyed;
        public static void Destroy(Object obj) { DestroyImmediate(obj); }
        public static void DestroyImmediate(Object obj)
        {
            obj.destroyed=true;
            if(obj is GameObject go)
            {
                foreach(var component in go.components) component.destroyed=true;
                go.transform.SetParent(null,false);
            }
        }
    }
    public class Component:Object
    {
        public GameObject gameObject;
        public Transform transform=>gameObject.transform;
        public string name=>gameObject.name;
    }
    public class GameObject:Object
    {
        public string name;
        public Transform transform;
        internal readonly List<Component> components=new();
        public GameObject(string name){this.name=name;transform=new Transform{gameObject=this};components.Add(transform);}
        public T AddComponent<T>() where T:Component,new(){var c=new T{gameObject=this};components.Add(c);return c;}
        public T[] GetComponentsInChildren<T>(bool inactive) where T:Component=>components.OfType<T>().Concat(transform.children.SelectMany(c=>c.gameObject.GetComponentsInChildren<T>(inactive))).ToArray();
    }
    public class Transform:Component
    {
        public Transform parent;
        internal readonly List<Transform> children=new();
        public Vector3 localPosition,localScale=Vector3.one;
        public Vector3 lossyScale
        {
            get
            {
                var inherited=parent==null?Vector3.one:parent.lossyScale;
                return new Vector3(localScale.x*inherited.x,localScale.y*inherited.y,localScale.z*inherited.z);
            }
        }
        public Quaternion localRotation;
        public void SetParent(Transform next,bool worldPositionStays){parent?.children.Remove(this);parent=next;next?.children.Add(this);}
        public bool IsChildOf(Transform ancestor){for(var t=parent;t!=null;t=t.parent)if(t==ancestor)return true;return false;}
    }
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
        public static Vector3 zero=>new(0,0,0);
        public static Vector3 one=>new(1,1,1);
    }
    public struct Quaternion
    {
        public float x,y,z,w;
        public Quaternion(float x,float y,float z,float w){this.x=x;this.y=y;this.z=z;this.w=w;}
    }
    public struct Keyframe
    {
        public float time,value,inTangent,outTangent;
        public Keyframe(float time,float value):this(time,value,0,0){}
        public Keyframe(float time,float value,float input,float output){this.time=time;this.value=value;inTangent=input;outTangent=output;}
    }
    public class AnimationCurve { public AnimationCurve(params Keyframe[] keys) { } }
    public class RangeAttribute : Attribute { public RangeAttribute(float min, float max) { } }
}
public class DynamicBone:Component
{
    public Transform m_Root;
    public List<DynamicBoneCollider> m_Colliders=new();
    public List<Transform> m_notRolls;
    public float m_UpdateRate,m_Elasticity,m_Damping,m_Stiffness,m_Inert,m_Radius;
    public AnimationCurve m_ElasticityDistrib,m_DampingDistrib,m_StiffnessDistrib,m_InertDistrib,m_RadiusDistrib;
}
public class DynamicBoneCollider:Component
{
    public enum Direction {X,Y,Z}
    public enum Bound {Outside,Inside}
    public Direction m_Direction;
    public Bound m_Bound;
    public Vector3 m_Center;
    public float m_Radius,m_Height;
}
