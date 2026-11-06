using System.Reflection;
using AssetImport;
using Studio;

int checks = 0;
void Check(bool valid, string description)
{
    checks++;
    if (!valid) throw new Exception(description);
}

foreach (var fixture in new[] {
    (Game: "KK", Path: "Fixtures/KK/Assembly-CSharp.dll"),
    (Game: "KKS", Path: "Fixtures/KKS/Assembly-CSharp.dll") })
{
    using var game = new MetadataAssembly(Path.Combine(AppContext.BaseDirectory, fixture.Path));
    var switchHandle = game.RequireMethod("Studio.OCIItem", "ActiveDynamicBone", "System.Boolean");
    var method = game.Reader.GetMethodDefinition(switchHandle);
    Check((method.Attributes & MethodAttributes.Public) != 0 &&
        method.DecodeSignature(game.Names, null).ReturnType == "System.Void", fixture.Game + " native switch API");
    var info = game.Reader.GetTypeDefinition(game.FindType("Studio.OIItemInfo"));
    var state = info.GetFields().Select(h => game.Reader.GetFieldDefinition(h))
        .Single(f => game.Reader.GetString(f.Name) == "enableDynamicBone");
    Check(state.DecodeSignature(game.Names, null) == "System.Boolean" &&
        (state.Attributes & FieldAttributes.Public) != 0, fixture.Game + " native state is public Boolean");
    foreach (string operation in new[] { "Save", "Load" })
        Check(info.GetMethods().Any(h => game.Reader.GetString(game.Reader.GetMethodDefinition(h).Name) == operation),
            fixture.Game + " state belongs to native saved item info with " + operation);
    Console.WriteLine("PASS " + fixture.Game + ": native ActiveDynamicBone(bool), OIItemInfo.enableDynamicBone and Save/Load API metadata.");
}

var controller = new AssetSceneController();
var first = MakeItem(1, true);
var second = MakeItem(2, false);
var unrelated = MakeItem(3, false);
var noChains = MakeItem(4, true);
noChains.dynamicBones = [];
AssetSceneController.Register(first, second, noChains);
var studio = Studio.Studio.Instance;
studio.dicInfo["first"] = first;
studio.dicInfo["second"] = second;
studio.dicInfo["unrelated"] = unrelated;
studio.dicInfo["empty"] = noChains;
studio.dicInfo["character"] = new ObjectCtrlInfo();
Check(controller.GetSelectedPhysicsStatus().English.Contains("Select imported items"), "empty selection guidance");
studio.treeNodeCtrl.selectNodes = ["unrelated", "empty", "character", "bone-not-an-item"];
Check(controller.SetSelectedPhysicsEnabled(true).English.Contains("No selected AssetImport"), "unsupported selections rejected");
Check(!unrelated.itemInfo.enableDynamicBone && unrelated.SwitchCalls == 0 && noChains.SwitchCalls == 0,
    "unrelated items and chainless imports remain unchanged");

studio.treeNodeCtrl.selectNodes = ["first", "first", "second", "unrelated", "empty", "character"];
var component = first.dynamicBones[0];
var root = component.m_Root;
var colliders = component.Colliders;
var indices = first.MappingAndSavedRoots;
first.isFK = true;
string mixed = controller.GetSelectedPhysicsStatus();
Check(mixed.Contains("Physics MIXED") && mixed.Contains("2 item(s), 2 chain(s)"), "reports mixed multi-selection without double counting");
Check(first.SwitchCalls == 0 && second.SwitchCalls == 0, "status is read-only");
Check(mixed.Contains("FK is ON") && mixed.Contains("Skipped 3"), "reports FK conflict and skipped selection");
string disabled = controller.SetSelectedPhysicsEnabled(false);
Check(disabled.Contains("Physics OFF") && !first.itemInfo.enableDynamicBone && !second.itemInfo.enableDynamicBone,
    "multi-selection disables native persisted flags");
Check(first.SwitchCalls == 1 && second.SwitchCalls == 1, "each distinct imported item switches once");
Check(ReferenceEquals(component, first.dynamicBones[0]) && ReferenceEquals(root, component.m_Root) &&
    ReferenceEquals(colliders, component.Colliders) && ReferenceEquals(indices, first.MappingAndSavedRoots),
    "switch preserves components, collision lists, bone roots and mapping selection");
Check(first.isFK, "physics switch never turns FK off implicitly");

// Native OIItemInfo is the stored state. Simulate an item reconstruction with its
// saved flag and the opposite new-import preference; no new plugin save field exists.
bool savedFlag = first.itemInfo.enableDynamicBone;
foreach (var kind in new[] { LoadProcess.LoadProcessKind.Load, LoadProcess.LoadProcessKind.Import, LoadProcess.LoadProcessKind.Copy })
{
    var reconstructed = MakeItem(10, savedFlag);
    var process = new LoadProcess { Kind = kind, Import = new Import { PhysicsEnabledAfterImport = true } };
    bool beforeRebuild = AssetSceneController.PhysicsEnabledForLoad(process, reconstructed);
    reconstructed.itemInfo.enableDynamicBone = true; // A later native rebuild must not override the captured preference.
    AssetSceneController.ApplyImportedPhysicsState(reconstructed, beforeRebuild);
    Check(!reconstructed.itemInfo.enableDynamicBone && !reconstructed.dynamicBones[0].Enabled, kind + " retains saved OFF preference");
    reconstructed.itemInfo.enableDynamicBone = true;
    process.Import.PhysicsEnabledAfterImport = false;
    Check(AssetSceneController.PhysicsEnabledForLoad(process, reconstructed), kind + " retains saved ON despite new-import default");
}
var normal = new LoadProcess { Kind = LoadProcess.LoadProcessKind.Normal, Import = new Import { PhysicsEnabledAfterImport = false } };
first.itemInfo.enableDynamicBone = true;
Check(!AssetSceneController.PhysicsEnabledForLoad(normal, first), "new import OFF overrides placeholder ON");
normal.Import.PhysicsEnabledAfterImport = true;
first.itemInfo.enableDynamicBone = false;
Check(AssetSceneController.PhysicsEnabledForLoad(normal, first), "new import ON overrides placeholder OFF");

// Regression: new chains start disabled while a placeholder already stores ON.
// The native fake models both FK suppression and an unchanged-flag no-op; the
// latter is defensive, not a claim about the unexamined game implementation.
foreach (bool fkEnabled in new[] { false, true })
{
    var fresh = MakeItem(20, true);
    fresh.isFK = fkEnabled;
    fresh.SkipSameNativeFlag = true;
    fresh.dynamicBones[0].Enabled = false;
    int editorCalls = AssetSceneController.EditorSyncCalls;
    AssetSceneController.ApplyImportedPhysicsState(fresh, true);
    Check(fresh.dynamicBones[0].Enabled && fresh.itemInfo.enableDynamicBone && fresh.isFK == fkEnabled,
        "fresh import enables actual chain despite native flag/FK state");
    Check(AssetSceneController.EditorSyncCalls == editorCalls, "initialization does not create KKPE dirty edits");
    AssetSceneController.ApplyImportedPhysicsState(fresh, false);
    Check(!fresh.dynamicBones[0].Enabled && !fresh.itemInfo.enableDynamicBone, "fresh OFF leaves roots but disables actual components");
}
int editorCallsBeforeButton = AssetSceneController.EditorSyncCalls;
string enabled = controller.SetSelectedPhysicsEnabled(true);
Check(enabled.Contains("Physics ON") && first.itemInfo.enableDynamicBone && second.itemInfo.enableDynamicBone,
    "disabled selected chains can be enabled again without reimport");
Check(AssetSceneController.EditorSyncCalls == editorCallsBeforeButton + 2, "explicit button synchronizes each item's editor cache once");
Check(first.isFK && first.dynamicBones[0].Enabled && second.dynamicBones[0].Enabled,
    "explicit ON enables imported chains even with FK while preserving the FK setting");
first.dynamicBones[0].Enabled = false; // KKPE per-chain edit without changing native preference.
Check(controller.GetSelectedPhysicsStatus().English.Contains("Physics MIXED"), "status reads actual components after KKPE edits");
first.dynamicBones[0].Enabled = true;
first.dynamicBones[0].gameObject.activeInHierarchy = false;
Check(controller.GetSelectedPhysicsStatus().English.Contains("1 enabled chain(s) are on inactive objects"), "enabled but hidden hosts are identified");
first.dynamicBones[0].gameObject.activeInHierarchy = true;
first.ThrowOnSwitch = true;
string partial = controller.SetSelectedPhysicsEnabled(false);
Check(partial.Contains("Failed to switch 1") && partial.Contains("Physics MIXED") && !second.itemInfo.enableDynamicBone,
    "one native switch failure does not prevent other selected items switching");
first.ThrowOnSwitch = false;
first.isFK = false;
first.ActiveDynamicBone(true);
second.ActiveDynamicBone(false);
Check(controller.ResetSelectedPhysics().English.Contains("Physics reset: 1 chain(s); 1 inactive"), "selected reset skips disabled chains and deduplicates items");
Check(first.dynamicBones[0].ResetCalls == 1 && second.dynamicBones[0].ResetCalls == 0 && unrelated.dynamicBones[0].ResetCalls == 0,
    "reset is scoped to active selected AssetImport chains");
Check(first.itemInfo.enableDynamicBone && !second.itemInfo.enableDynamicBone && first.dynamicBones[0].Colliders == colliders,
    "reset keeps switch state and collision settings");
studio.treeNodeCtrl.selectNodes = ["unrelated", "character"];
Check(controller.ResetSelectedPhysics().English.Contains("No selected AssetImport"), "reset rejects unrelated selections");
Console.WriteLine("PASS: " + checks + " physics switch/API checks. Runtime uses inert Studio objects; Unity physics and actual native serialization require game testing.");

static OCIItem MakeItem(int id, bool enabled) => new()
{
    itemInfo = new OIItemInfo { dicKey = id, enableDynamicBone = enabled },
    dynamicBones = [new DynamicBone { Enabled = enabled, m_Root = new InertObject() }]
};

public class InertObject
{
    public string name = "test root";
    public InertGameObject gameObject = new();
    public static implicit operator bool(InertObject value) => value != null;
}
public class InertGameObject { public bool activeInHierarchy = true; }
public class DynamicBone : InertObject
{
    public InertObject m_Root;
    public List<object> Colliders = [new object()];
    public bool Enabled;
    public bool enabled { get => Enabled; set => Enabled = value; }
    public int ResetCalls;
    public void ResetParticlesPosition() { ResetCalls++; }
}
namespace Studio
{
    public class ObjectCtrlInfo { }
    public class OIItemInfo { public int dicKey; public bool enableDynamicBone; }
    public class OCIItem : ObjectCtrlInfo
    {
        public OIItemInfo itemInfo;
        public DynamicBone[] dynamicBones;
        public bool isFK, ThrowOnSwitch, SkipSameNativeFlag;
        public int SwitchCalls;
        public object MappingAndSavedRoots = new();
        public void ActiveDynamicBone(bool enabled)
        {
            SwitchCalls++;
            if (ThrowOnSwitch) throw new InvalidOperationException("injected native error");
            if (SkipSameNativeFlag && itemInfo.enableDynamicBone == enabled) return;
            itemInfo.enableDynamicBone = enabled;
            foreach (var bone in dynamicBones) bone.Enabled = enabled && !isFK;
        }
    }
    public class TreeNodeCtrl { public string[] selectNodes; }
    public class Studio
    {
        public static Studio Instance = new();
        public TreeNodeCtrl treeNodeCtrl = new();
        public Dictionary<string, ObjectCtrlInfo> dicInfo = new();
    }
}
namespace AssetImport
{
    public class Import { public bool PhysicsEnabledAfterImport; }
    public class LoadProcess
    {
        public enum LoadProcessKind { Normal, Load, Import, Copy }
        public LoadProcessKind Kind;
        public Import Import;
    }
    public partial class AssetSceneController
    {
        private static readonly Dictionary<int, object> LoadedObjects = new();
        private static readonly InertLogger Logger = new();
        public static int EditorSyncCalls;
        internal static void SyncPhysicsEditor(OCIItem item, bool enabled) { EditorSyncCalls++; }
        public static void Register(params OCIItem[] items)
        {
            foreach (var item in items) LoadedObjects[item.itemInfo.dicKey] = new object();
        }
    }
    public class InertLogger { public void LogWarning(string message) { } }
}
