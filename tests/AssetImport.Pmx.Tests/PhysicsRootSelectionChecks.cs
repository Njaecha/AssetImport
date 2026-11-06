using AssetImport;
using UnityEngine;

// Calls the production UI action and BoneNode selection rules. Engine doubles
// store the hierarchy only; they do not simulate DynamicBone or render the UI.
internal static class PhysicsRootSelectionChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var model = Fixture();
        BoneNode Node(Import import, int bone) => import.BoneNodes.Single(n => n.GameObject == import.Bone(bone).gameObject);
        var import = new Import(model);
        Node(import, 9).SetDynamic(true); // A manual skirt root on another branch.
        var skirtProperties = Node(import, 9).GetDynamicBoneProperties();
        skirtProperties.radius = .37f;
        foreach (var node in import.BoneNodes) node.SetNodeOpen(false);
        var originalNodes = import.BoneNodes.ToArray();
        var originalTransforms = import.GameObject.GetComponentsInChildren<Transform>(true);
        var originalParents = originalTransforms.Select(t => t.parent).ToArray();
        string status = import.SelectPmxHairRoots();
        check(status.Contains("2 added") && status.Contains("0 skipped"), "production auto-selection reports two added hair roots");
        check(import.BoneNodes.Where(n => n.IsDynamicRoot).Select(n => n.GameObject.name).Order().SequenceEqual(
            new[] { 3, 7, 9 }.Select(i => PmxModel.BoneName(i, model.Bones[i].Name)).Order()), "auto-selection adds hair roots while keeping another manual branch");
        check(ReferenceEquals(skirtProperties, Node(import, 9).GetDynamicBoneProperties()) && skirtProperties.radius == .37f, "auto-selection preserves manual root settings");
        check(!Node(import, 2).IsDynamic && !Node(import, 0).IsDynamic && !Node(import, 1).IsDynamic, "static hair anchor, head and full skeleton root remain unselected");
        check(Node(import, 3).IsDynamicRoot && Node(import, 4).IsDynamic && !Node(import, 4).IsDynamicRoot && Node(import, 5).IsDynamic, "production BoneNode marks descendants as one dynamic chain");
        check(new[] { 0, 1, 2, 3, 6, 7 }.All(i => Node(import, i).NodeOpen), "newly selected roots and their ancestor paths open in the bone tree");
        var hairProperties = Node(import, 3).GetDynamicBoneProperties();
        hairProperties.dampening = .61f;
        status = import.SelectPmxHairRoots();
        check(status.Contains("0 added") && status.Contains("2 already selected") && status.Contains("0 skipped"), "second click is idempotent and reports existing roots");
        check(ReferenceEquals(hairProperties, Node(import, 3).GetDynamicBoneProperties()) && hairProperties.dampening == .61f, "second click does not recreate or reset selected root properties");
        check(Node(import, 3).DynamicChainLength == 3, "repeated clicks do not inflate chain length");

        import.ClearPhysicsRoots();
        check(import.BoneNodes.All(n => !n.IsDynamicRoot && !n.IsDynamic), "clear removes all physics selection flags including manual roots");
        check(import.BoneNodes.SequenceEqual(originalNodes) && import.GameObject.GetComponentsInChildren<Transform>(true).SequenceEqual(originalTransforms), "clear preserves every bone node and game transform");
        check(originalTransforms.Select(t => t.parent).SequenceEqual(originalParents) && originalTransforms.All(t => t), "clear neither reparents nor destroys the full skeleton");
        import.ClearPhysicsRoots();
        check(import.SelectPmxHairRoots().English.Contains("2 added"), "clear is repeatable and auto-selection works again");

        var parentConflict = new Import(model);
        Node(parentConflict, 2).SetDynamic(true);
        Node(parentConflict, 9).SetDynamic(true);
        status = parentConflict.SelectPmxHairRoots();
        check(status.Contains("1 added") && status.Contains("1 skipped"), "selected parent causes one suggestion to be skipped while independent hair is added");
        check(Node(parentConflict, 2).IsDynamicRoot && !Node(parentConflict, 3).IsDynamicRoot && Node(parentConflict, 7).IsDynamicRoot && Node(parentConflict, 9).IsDynamicRoot, "parent conflict preserves all manual roots and does not create nested roots");

        var childConflict = new Import(model);
        Node(childConflict, 4).SetDynamic(true);
        var childProperties = Node(childConflict, 4).GetDynamicBoneProperties();
        childProperties.stiffness = .73f;
        status = childConflict.SelectPmxHairRoots();
        check(status.Contains("1 added") && status.Contains("1 skipped"), "selected descendant rejects its suggested ancestor without blocking another branch");
        check(!Node(childConflict, 3).IsDynamicRoot && Node(childConflict, 4).IsDynamicRoot && Node(childConflict, 7).IsDynamicRoot && childProperties.stiffness == .73f,
            "child conflict preserves the existing descendant root and its settings");

        var missingNode = new Import(model);
        missingNode.BoneNodes.Remove(Node(missingNode, 7));
        status = missingNode.SelectPmxHairRoots();
        check(status.Contains("1 added") && status.Contains("1 skipped") && missingNode.Bone(7), "missing UI bone entry is reported and does not destroy the skeleton");

        var disabled = new Import(model) { ImportBones = false };
        check(disabled.SelectPmxHairRoots().English.Contains("Enable Import Armature") && disabled.BoneNodes.All(n => !n.IsDynamic), "armature-disabled import is not modified");
        var otherFormat = new Import(model, false);
        Node(otherFormat, 9).SetDynamic(true);
        check(otherFormat.SelectPmxHairRoots().English.Contains("Enable Import Armature") && otherFormat.BoneNodes.Count(n => n.IsDynamicRoot) == 1,
            "non-PMX import keeps existing selection unchanged");

        var all = new Import(model);
        status = all.SelectPmxPhysicsRoots();
        check(status.Contains("3 added") && new[] { 3, 7, 9 }.All(i => Node(all, i).IsDynamicRoot), "all-chain action selects both hair and skirt roots");
        var allSkirtProperties = Node(all, 9).GetDynamicBoneProperties();
        allSkirtProperties.radius = .41f;
        check(all.SelectPmxPhysicsRoots().English.Contains("3 already selected") && ReferenceEquals(allSkirtProperties, Node(all, 9).GetDynamicBoneProperties()) && allSkirtProperties.radius == .41f,
            "repeated all-chain action preserves selected properties without duplicating roots");
        check(all.SelectPmxHairRoots().English.Contains("2 already selected") && Node(all, 9).IsDynamicRoot, "hair-only action after all-chain action keeps skirt selection");
        all.ClearPhysicsRoots();
        Node(all, 4).SetDynamic(true);
        status = all.SelectPmxPhysicsRoots();
        check(status.Contains("2 added") && status.Contains("1 skipped") && Node(all, 4).IsDynamicRoot && !Node(all, 3).IsDynamicRoot,
            "all-chain action preserves manual child root and adds the independent skirt and hair chains");
        check(disabled.SelectPmxPhysicsRoots().English.Contains("Enable Import Armature") && disabled.BoneNodes.All(n => !n.IsDynamic), "all-chain action respects armature opt-out");

        var generic = new PmxModel {
            Bones = new[] { ("root", -1), ("腰", 0), ("jewel", 1), ("pendantTip", 2), ("Leaf", 1), ("hairNoBody", 1), ("tip", 5) }
                .Select(b => new PmxModel.Bone { Name = b.Item1, Parent = b.Item2 }).ToArray(),
            RigidBodyData = new[] {
                new PmxModel.RigidBody { Bone = 1, Mode = 2 }, new PmxModel.RigidBody { Bone = 2, Mode = 2 },
                new PmxModel.RigidBody { Bone = 4, Mode = 1 }, new PmxModel.RigidBody { Bone = -1, Mode = 1 }
            }
        };
        var genericImport = new Import(generic);
        status = genericImport.SelectPmxPhysicsRoots();
        check(status.Contains("1 added") && status.Contains("2 unsupported roots") && status.Contains("1 unbound bodies"), "all-chain action reports usable accessory, unsafe core, uncovered leaf and unbound metadata separately");
        check(Node(genericImport, 2).IsDynamicRoot && !Node(genericImport, 1).IsDynamic && !Node(genericImport, 4).IsDynamic && !Node(genericImport, 5).IsDynamic,
            "all-chain action selects accessories while keeping action skeleton, leaves and no-body chains untouched");
        genericImport.ClearPhysicsRoots();
        Node(genericImport, 5).SetDynamic(true);
        genericImport.SelectPmxPhysicsRoots();
        check(Node(genericImport, 5).IsDynamicRoot && Node(genericImport, 2).IsDynamicRoot, "manual non-metadata root is preserved by automatic all-chain merge");
    }

    private static PmxModel Fixture()
    {
        var bones = new[] {
            ("全ての親", -1), ("頭", 0), ("hair 1", 1), ("hair 2", 2), ("hair 3", 3), ("hair tip", 4),
            ("hair right anchor", 1), ("hair right 1", 6), ("hair right tip", 7), ("skirt", 0), ("skirt tip", 9)
        };
        return new PmxModel {
            Bones = bones.Select(b => new PmxModel.Bone { Name = b.Item1, Parent = b.Item2 }).ToArray(),
            RigidBodyData = new[] { 3, 4, 7, 9 }.Select(i => new PmxModel.RigidBody { Bone = i, Name = bones[i].Item1, Mode = 1 }).ToArray()
        };
    }
}
