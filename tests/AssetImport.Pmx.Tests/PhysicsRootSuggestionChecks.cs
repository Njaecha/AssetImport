using AssetImport;

internal static class PhysicsRootSuggestionChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var model = Model(
            ("全ての親", -1), ("腰", 0), ("上半身2", 1), ("頭", 2),
            ("hair 1", 3), ("hair 2", 4), ("hair 3", 5), ("tip", 6),
            ("左胸上", 2), ("breastTip", 8), ("前スカート_0_0", 1), ("skirtTip", 10),
            ("Hair.phys", 3), ("HairTail.L.001", 12), ("HairTail.L.002", 13),
            ("左HZ1", 3), ("左HZ2", 15), ("左HZ3", 16), ("左HZ先", 17),
            ("右HZ1", 3), ("右HZ2", 19), ("右HZ3", 20), ("右HZ先", 21));
        Bodies(model, (0, "root", 1), (1, "腰", 1), (3, "頭", 2), (4, "hair anchor", 0),
            (5, "hair 2", 1), (6, "hair 3", 2), (8, "左胸", 1), (10, "skirt", 1),
            (13, "HairTail", 1), (14, "HairTail", 1), (16, "左HZ2", 1), (17, "左HZ3", 1),
            (20, "右HZ2", 2), (21, "右HZ3", 2));
        var result = PmxPhysicsRootSuggestions.Build(model);
        check(result.HairRoots.SequenceEqual(new[] { 5, 13, 16, 20 }), "hair roots use first dynamic bone, including HAKU-style static anchor and head-attached HZ chains");
        check(result.PhysicsRoots.SequenceEqual(new[] { 5, 8, 10, 13, 16, 20 }), "all physics roots retain explicit non-hair chains but omit global/waist/head skeleton roots");
        check(result.Candidates.All(c => c.DynamicBoneCount > 0 && c.Reason.Length > 0), "each suggestion has dynamic-body evidence and an explanation");
        check(result.Candidates.First(c => c.Bone == 5).Name == "PMX_0005_hair 2", "suggestion exposes exact unique imported bone name");
        check(result.Candidates.First(c => c.Bone == 5).DynamicBoneCount == 2, "all dynamic bodies in suggested chain counted");
        check(result.Diagnostics.Count > 0, "selection reports heuristic and ignored skeleton roots");
        check(PmxPhysicsRootSuggestions.Build(model).HairRoots.SequenceEqual(result.HairRoots), "suggestion order stable");
        check(model.PhysicsBones.Count == 0, "suggestions read full PMX body metadata without mutating source physics bone set");

        var ambiguous = Model(("root", -1), ("head", 0),
            ("chain1", 1), ("chain2", 2), ("chain3", 3),
            ("HairRibbon", 1), ("ribbon2", 5),
            ("chair", 1), ("chair2", 7),
            ("crosshair", 1), ("tip", 9),
            ("HZ1", 0), ("HZ2", 11),
            ("earring1", 1), ("earring2", 13),
            ("HairSkirt", 0), ("hair2", 15),
            ("前髪1", 1), ("先", 17),
            ("Unknown", 1), ("tip", 19),
            ("HairSolo", 1));
        Bodies(ambiguous, (2, "chain1", 1), (3, "chain2", 1), (4, "chain3", 1),
            (5, "HairRibbon", 1), (7, "chair", 1), (9, "crosshair", 1),
            (11, "HZ1", 1), (12, "HZ2", 1), (13, "earring", 1), (15, "HairSkirt", 1),
            (17, "front", 2), (19, "ponytail 1", 1), (21, "hair", 1));
        result = PmxPhysicsRootSuggestions.Build(ambiguous);
        check(result.HairRoots.SequenceEqual(new[] { 17, 19 }), "ambiguous head attachments, accessories, chair/crosshair and HZ outside head excluded; rigid-body hair name accepted");
        check(result.Candidates.First(c => c.Bone == 21).Reason.Contains("No descendant"), "single leaf does not promise a usable dynamic chain");
        check(!result.PhysicsRoots.Contains(21) && !result.Candidates.First(c => c.Bone == 21).IsUsable && result.SkippedRootCount == 1,
            "all-chain selection excludes and counts an uncovered leaf, but not the leaf covered by an ancestor chain");
        check(result.PhysicsRoots.SequenceEqual(new[] { 2, 5, 7, 9, 11, 13, 15, 17, 19 }),
            "all-chain selection includes unnamed chains, ribbons, earrings and skirts without relying on hair names");
        check(result.Candidates.First(c => c.Bone == 2).Reason.Contains("no confident"), "unknown head chain stays manual");

        var gaps = Model(("root", -1), ("頭", 0), ("前髪1", 1), ("helper", 2), ("前髪2", 3), ("tip", 4),
            ("Hair.phys", 1), ("strand1", 6), ("tip", 7),
            ("帽子", 1), ("hair1", 9), ("tip", 10));
        Bodies(gaps, (2, "前髪1", 1), (4, "前髪2", 2), (7, "strand1", 1), (10, "hair1", 1));
        result = PmxPhysicsRootSuggestions.Build(gaps);
        check(result.PhysicsRoots.SequenceEqual(new[] { 2, 7, 10 }), "non-dynamic helper inside chain does not produce overlapping roots");
        check(result.HairRoots.SequenceEqual(new[] { 2, 7 }), "static named hair parent is useful evidence but never selected as root; clothing ancestor excluded");
        check(result.Candidates.First(c => c.Bone == 2).DynamicBoneCount == 2, "dynamic count crosses static helper bones");

        var noPhysics = Model(("root", -1), ("head", 0), ("hair1", 1), ("hair2", 2));
        Bodies(noPhysics, (2, "hair1", 0), (3, "hair2", 0), (-1, "hair loose body", 1), (99, "bad reference", 1));
        result = PmxPhysicsRootSuggestions.Build(noPhysics);
        check(result.HairRoots.Length == 0 && result.PhysicsRoots.Length == 0, "names alone or unbound/invalid bodies never create physics roots");
        check(result.Diagnostics.Any(d => d.Contains("No confident")), "empty selection gives manual-selection guidance");
        check(PmxPhysicsRootSuggestions.Build(new PmxModel()).HairRoots.Length == 0, "empty metadata safely yields no suggestions");

        var cyclic = Model(("hair1", 1), ("hair2", 0));
        Bodies(cyclic, (0, "hair1", 1), (1, "hair2", 1));
        check(PmxPhysicsRootSuggestions.Build(cyclic).PhysicsRoots.Length == 0, "malformed cycle cannot loop or produce overlapping roots");

        var international = Model(("root", -1), ("head", 0), ("backHair01", 1), ("tip", 2),
            ("馬尾1", 1), ("先", 4), ("もみあげ１", 1), ("先", 6));
        Bodies(international, (2, "back hair", 1), (4, "pony", 1), (6, "sideburn", 2));
        check(PmxPhysicsRootSuggestions.Build(international).HairRoots.SequenceEqual(new[] { 2, 4, 6 }), "camel case, Japanese and Chinese hair names recognized without model indices");

        var unsafeRig = Model(("global", -1), ("unknown_body_physics", 0), ("左腕", 1), ("左ひじ", 2),
            ("sleeve", 2), ("sleeveTip", 4), ("skirt", 0), ("skirtTip", 6),
            ("hair1", 0), ("head", 8), ("earring", 9), ("earringTip", 10),
            ("左足IK", 0), ("unknownJoint", 0), ("unknownFoot", 13),
            ("hair_bad_parent", 999), ("badTip", 15));
        unsafeRig.Bones[12].Flags = 0x0020;
        unsafeRig.Bones[12].IkTarget = 14;
        unsafeRig.Bones[12].IkLinks.Add(new PmxModel.IkLink { Bone = 13 });
        Bodies(unsafeRig, (1, "body physics", 1), (2, "arm", 2), (4, "sleeve", 1), (6, "skirt", 1),
            (8, "hair misleading subtree", 1), (10, "jewel", 2), (12, "controller", 1),
            (13, "unknownJoint", 1), (15, "hair", 1));
        result = PmxPhysicsRootSuggestions.Build(unsafeRig);
        check(result.PhysicsRoots.SequenceEqual(new[] { 4, 6, 10 }), "unsafe all-character subtree and animation/IK core are excluded, safe independent sleeves/skirts/accessories remain selectable");
        check(result.Candidates.First(c => c.Bone == 1).Reason.Contains("Contains animation"), "unexpected main skeleton below a generic physics bone is explicitly diagnosed");
        check(result.Candidates.First(c => c.Bone == 8).Reason.Contains("Contains animation"), "hair name cannot override unsafe animation descendants");
        check(result.Candidates.First(c => c.Bone == 13).Reason.Contains("animation skeleton"), "IK-linked nonstandard bone is treated as animation skeleton");
        check(result.Candidates.First(c => c.Bone == 15).Reason.Contains("Invalid"), "invalid parent chain is reported and not selected");
        check(result.SkippedRootCount == 6 && result.Diagnostics.Count(d => d.StartsWith("Skipped ")) == 6, "all unsupported roots receive individual reasons and an accurate count");

        var accessoryIk = Model(("root", -1), ("01-0", 0), ("01-1", 1), ("01-2", 2), ("01-control", -1));
        Bodies(accessoryIk, (1, "accessory", 1), (2, "accessory", 1));
        accessoryIk.Bones[4].Flags = 0x0020;
        accessoryIk.Bones[4].IkTarget = 3;
        accessoryIk.Bones[4].IkLinks.Add(new PmxModel.IkLink { Bone = 1 });
        accessoryIk.Bones[4].IkLinks.Add(new PmxModel.IkLink { Bone = 2 });
        check(PmxPhysicsRootSuggestions.Build(accessoryIk).PhysicsRoots.SequenceEqual(new[] { 1 }), "accessory IK does not hide an explicitly declared dynamic chain without standard action bones");

        var actionNames = new[] { "左肩P", "右腕捩2", "左ひざD", "右人指3", "右足ＩＫ", "mixamorig:LeftForeArm", "Bip01_R_Hand", "upper_arm.L", "J_Bip_C_Head", "LeftHandMiddle1" };
        foreach (string name in actionNames)
        {
            var action = Model(("root", -1), (name, 0), ("tip", 1));
            Bodies(action, (1, "oddly configured rigid body", 1));
            check(PmxPhysicsRootSuggestions.Build(action).PhysicsRoots.Length == 0, "standard action bone protected: " + name);
        }
    }

    private static PmxModel Model(params (string Name, int Parent)[] bones)
    {
        return new PmxModel { Bones = bones.Select(b => new PmxModel.Bone { Name = b.Name, Parent = b.Parent }).ToArray() };
    }
    private static void Bodies(PmxModel model, params (int Bone, string Name, int Mode)[] bodies)
    {
        model.RigidBodyData = bodies.Select(b => new PmxModel.RigidBody { Bone = b.Bone, Name = b.Name, Mode = b.Mode }).ToArray();
    }
}
