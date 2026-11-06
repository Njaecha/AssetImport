using AssetImport;
using Studio;
using UnityEngine;

internal static class PhysicsImportOptionsChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var model = new PmxModel {
            Bones = new[] { ("root", -1), ("skirt", 0), ("tip", 1), ("pendant", 0), ("pendantTip", 3) }
                .Select(b => new PmxModel.Bone { Name = b.Item1, Parent = b.Item2 }).ToArray(),
            RigidBodyData = new[] { 1, 3 }.Select(i => new PmxModel.RigidBody { Bone = i, Mode = 1 }).ToArray()
        };
        BoneNode Node(Import import, int bone) => import.BoneNodes.Single(n => n.GameObject == import.Bone(bone).gameObject);
        LoadProcess Create(Import import, bool accessory, LoadProcess.LoadProcessKind kind)
            => accessory ? new LoadProcess(import.GameObject, new AccessoryHelper(), import, Vector3.one, kind)
                : new LoadProcess(import.GameObject, new OCIItem(), import, Vector3.one, kind);

        foreach (bool accessory in new[] { false, true })
        {
            AssetUI.AutoSelectPmxPhysics = true;
            AssetUI.EnablePhysicsAfterImport = false;
            AssetUI.UsePmxPhysicsPreset = true;
            AssetUI.PmxHairCollisionAssist = true;
            var fresh = new Import(model);
            var process = Create(fresh, accessory, LoadProcess.LoadProcessKind.Normal);
            check(ReferenceEquals(process.Import, fresh) && fresh.InitialPhysicsSelectionStatus.English.Contains("2 added"), "new import constructor auto-selects all usable chains for " + (accessory ? "accessory" : "item"));
            check(fresh.PmxHairCollisionAssist, "new import copies explicit hair collision assistance option");
            check(fresh.PhysicsEnvironment.Floor && fresh.PhysicsEnvironment.Settle && !fresh.PhysicsEnvironment.NaturalDrape && fresh.PhysicsEnvironment.DownwardForce == .001f,
                "new PMX import receives ground, force and settling defaults");
            check(!ReferenceEquals(fresh.PhysicsEnvironment, AssetUI.NewPhysicsEnvironment), "import options do not share mutable settings with UI");
            check(fresh.PhysicsProfile == 1, "new PMX import uses the selected versioned preset");
            check(!fresh.PhysicsEnabledAfterImport && Node(fresh, 1).IsDynamicRoot && Node(fresh, 3).IsDynamicRoot,
                "new import disabled option retains selected chains for later enabling");

            AssetUI.AutoSelectPmxPhysics = false;
            AssetUI.EnablePhysicsAfterImport = true;
            AssetUI.UsePmxPhysicsPreset = false;
            AssetUI.PmxHairCollisionAssist = false;
            var manual = new Import(model);
            Node(manual, 1).SetDynamic(true);
            Create(manual, accessory, LoadProcess.LoadProcessKind.Normal);
            check(!manual.PmxHairCollisionAssist, "new import preserves original PMX collision rules when assist is off");
            check(manual.PhysicsProfile == 0, "new PMX import can retain legacy defaults");
            check(manual.PhysicsEnabledAfterImport && Node(manual, 1).IsDynamicRoot && !Node(manual, 3).IsDynamicRoot && manual.InitialPhysicsSelectionStatus.English.Contains("off"),
                "new import opt-out preserves hand selection and sets requested enable flag");

            foreach (var kind in new[] { LoadProcess.LoadProcessKind.Load, LoadProcess.LoadProcessKind.Import, LoadProcess.LoadProcessKind.Copy })
            {
                AssetUI.AutoSelectPmxPhysics = true;
                AssetUI.EnablePhysicsAfterImport = true;
                AssetUI.UsePmxPhysicsPreset = true;
                AssetUI.PmxHairCollisionAssist = true;
                var restored = new Import(model) { PhysicsEnabledAfterImport = false, PhysicsProfile = 0 };
                Node(restored, 3).SetDynamic(true);
                var properties = Node(restored, 3).GetDynamicBoneProperties();
                properties.radius = .29f;
                Create(restored, accessory, kind);
                check(!restored.PmxHairCollisionAssist, kind + " does not add assist to an old asset because UI is on");
                check(!restored.PhysicsEnvironment.Floor && !restored.PhysicsEnvironment.Settle && !restored.PhysicsEnvironment.NaturalDrape && restored.PhysicsEnvironment.DownwardForce == 0,
                    kind + " does not silently enable environment on legacy records");
                check(restored.PhysicsProfile == 0, kind + " preserves the saved legacy profile even when new imports default to preset");
                check(!restored.PhysicsEnabledAfterImport && !Node(restored, 1).IsDynamicRoot && Node(restored, 3).IsDynamicRoot && restored.InitialPhysicsSelectionStatus == "",
                    kind + " ignores UI auto-selection and enable defaults for " + (accessory ? "accessory" : "item"));
                check(ReferenceEquals(properties, Node(restored, 3).GetDynamicBoneProperties()) && properties.radius == .29f,
                    kind + " preserves restored per-root settings");
                AssetUI.UsePmxPhysicsPreset = false;
                AssetUI.PmxHairCollisionAssist = false;
                var savedPreset = new Import(model) { PhysicsProfile = 1, PmxHairCollisionAssist = true,
                    PhysicsEnvironment = new PhysicsEnvironmentSettings { Floor = true, FloorOffset = -.3f, DownwardForce = .002f, Settle = true, NaturalDrape = true } };
                Create(savedPreset, accessory, kind);
                check(savedPreset.PmxHairCollisionAssist, kind + " retains saved collision assistance despite UI being off");
                check(savedPreset.PhysicsProfile == 1, kind + " retains saved preset when new-import UI disables it");
                check(savedPreset.PhysicsEnvironment.FloorOffset == -.3f && savedPreset.PhysicsEnvironment.DownwardForce == .002f && savedPreset.PhysicsEnvironment.NaturalDrape,
                    kind + " retains saved environment values");
            }

            AssetUI.AutoSelectPmxPhysics = true;
            AssetUI.EnablePhysicsAfterImport = false;
            var other = new Import(model, false);
            Create(other, accessory, LoadProcess.LoadProcessKind.Normal);
            check(other.PhysicsEnabledAfterImport && other.InitialPhysicsSelectionStatus == "" && other.BoneNodes.All(n => !n.IsDynamic), "non-PMX import keeps existing behavior");
            var noArmature = new Import(model) { ImportBones = false };
            Create(noArmature, accessory, LoadProcess.LoadProcessKind.Normal);
            check(noArmature.PhysicsEnabledAfterImport && noArmature.InitialPhysicsSelectionStatus == "" && noArmature.BoneNodes.All(n => !n.IsDynamic), "PMX without armature does not attempt physics selection");
        }
        AssetUI.AutoSelectPmxPhysics = true;
        AssetUI.EnablePhysicsAfterImport = true;
        AssetUI.UsePmxPhysicsPreset = true;
        AssetUI.PmxHairCollisionAssist = false;
    }
}

// The constructors and all selection decisions above are production code.
// These represent only the UI inputs and opaque destination component types.
namespace Studio { public class OCIItem { } }
namespace AssetImport
{
    public class AccessoryHelper { }
    internal static class AssetUI
    {
        internal static CubeLut ImportColorLut;
        internal static bool AutoSelectPmxPhysics = true;
        internal static bool EnablePhysicsAfterImport = true;
        internal static bool UsePmxPhysicsPreset = true;
        internal static bool PmxHairCollisionAssist;
        internal static PhysicsEnvironmentSettings NewPhysicsEnvironment = PhysicsEnvironmentSettings.NewImport();
    }
}
