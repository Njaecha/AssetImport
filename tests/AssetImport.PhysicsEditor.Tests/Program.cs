using System.Reflection;
using AssetImport;
int n=0;
void Check(bool b,string m){n++;if(!b)throw new Exception(m);}
var b=new DynamicBone {m_Root=new UnityEngine.Transform(),stiffness=.8f};
var e=new Editor(b);
var host=new UnityEngine.GameObject {value=new HSPE.PoseController {_dynamicBonesEditor=e}};
var item=new Studio.OCIItem {objectItem=host,dynamicBones=new[]{b,b,null,new DynamicBone()}};
Hooks.available=false;AssetSceneController.SyncPhysicsEditor(item,true);
Check(host.reads==0&&!b.enabled,"absent optional plugin skips component lookup");
Hooks.available=true;AssetSceneController.SyncPhysicsEditor(item,true);
Check(b.enabled&&e.sets==1,"live roots once, ignore duplicates/null/rootless");
Check(e.restores==0,"ordinary switch does not restore");
e.Change(b,.65f);e.Refresh();
Check(!b.enabled&&b.stiffness==.8f,"fixture restores originals while cache pending");
AssetSceneController.SyncPhysicsEditor(item,true);
Check(e.restores==1&&b.enabled&&b.stiffness==.65f,"pending restored before ON without losing stiffness");
e.Tick();Check(b.enabled&&b.stiffness==.65f,"next frame stable");
e.Refresh();e.Tick();Check(b.enabled&&b.stiffness==.65f,"next refresh stable");
AssetSceneController.SyncPhysicsEditor(item,false);e.Refresh();e.Tick();
Check(!b.enabled&&b.stiffness==.65f,"OFF caches without losing other setting");
var other=new DynamicBone {m_Root=new UnityEngine.Transform()};var e2=new Editor(other);e2.Change(other,.6f);e2.Refresh();
host.value=new HSPE.PoseController {_dynamicBonesEditor=e2};AssetSceneController.SyncPhysicsEditor(item,true);
Check(e2.restores==0,"unrelated pending root does not trigger replay");
host.value=new HSPE.PoseController {_dynamicBonesEditor=new object()};AssetSceneController.SyncPhysicsEditor(item,false);
Check(Hooks.warnings==1,"missing optional API warns without propagating");
var boundary=typeof(AssetSceneController).GetMethod("SyncPhysicsEditorLoaded",BindingFlags.Static|BindingFlags.NonPublic);
Check((boundary.GetMethodImplementationFlags()&MethodImplAttributes.NoInlining)!=0,"isolated non-inlined boundary");
Console.WriteLine($"PhysicsEditor bridge: {n} lifecycle-double checks passed; not a Unity runtime test.");

// Models only the audited editor cache transition; no Unity physics simulation.
class Editor {
 class Data { public bool original,current;public float originalStiff,currentStiff; }
 readonly Dictionary<DynamicBone,Data> _dirtyDynamicBones=new();
 readonly Dictionary<UnityEngine.Transform,Data> _headlessDirtyDynamicBones=new();
 readonly DynamicBone[] bones;public int sets,restores;
 public Editor(params DynamicBone[] b){bones=b;}
 Data Dirty(DynamicBone b){if(!_dirtyDynamicBones.TryGetValue(b,out var d))_dirtyDynamicBones.Add(b,d=new Data{original=b.enabled,current=b.enabled,originalStiff=b.stiffness,currentStiff=b.stiffness});return d;}
 void SetDynamicBoneEnabled(DynamicBone b,bool v){sets++;Dirty(b).current=v;b.enabled=v;}
 public void Change(DynamicBone b,float v){Dirty(b).currentStiff=v;b.stiffness=v;}
 public void Refresh(){foreach(var p in _dirtyDynamicBones){_headlessDirtyDynamicBones.Add(p.Key.m_Root,p.Value);p.Key.enabled=p.Value.original;p.Key.stiffness=p.Value.originalStiff;}_dirtyDynamicBones.Clear();}
 void LateUpdate(){restores++;foreach(var p in _headlessDirtyDynamicBones.ToArray())foreach(var b in bones)if(!_dirtyDynamicBones.ContainsKey(b)&&(b.m_Root==p.Key||b.transform==p.Key)){_dirtyDynamicBones.Add(b,p.Value);b.enabled=p.Value.current;b.stiffness=p.Value.currentStiff;_headlessDirtyDynamicBones.Remove(p.Key);break;}}
 public void Tick(){LateUpdate();}
}
public class DynamicBone:UnityEngine.Object {public UnityEngine.Transform m_Root;public UnityEngine.Transform transform=new();public bool enabled;public float stiffness;}
namespace UnityEngine { public class Object {public static implicit operator bool(Object o)=>o!=null;} public class Transform:Object{} public class GameObject:Object {public object value;public int reads;public T GetComponent<T>()where T:class{reads++;return value as T;}} }
namespace Studio {public class OCIItem{public UnityEngine.GameObject objectItem;public DynamicBone[] dynamicBones;}}
namespace HSPE {public class PoseController:UnityEngine.Object{internal object _dynamicBonesEditor;}}
namespace HarmonyLib {public static class AccessTools {public static FieldInfo Field(Type t,string n)=>t.GetField(n,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public);public static MethodInfo Method(Type t,string n,Type[] a)=>t.GetMethod(n,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public,null,a,null);}}
namespace AssetImport {internal static class Hooks {public const string PoseEditorAssembly="KKPE";public static bool available;public static int warnings;public static bool IsOptionalIntegrationAvailable(string s)=>available;public static void RunOptionalIntegration(string s,string op,Action a){if(available)try{a();}catch{warnings++;}}}}
