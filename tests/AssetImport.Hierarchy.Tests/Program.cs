using AssetImport;
using UnityEngine;

internal static class Program
{
    private static int checks;
    private static void Check(bool value, string message)
    { checks++; if (!value) throw new Exception(message); }

    private static List<LegacyMeshGeometry.MeshPart> Partition(int vertices) =>
        LegacyMeshGeometry.Partition(vertices, Enumerable.Range(0, vertices / 3 * 3).ToArray());

    private static void Main(string[] args)
    {
        // Numeric topology summary of the reported scene. No user geometry or images.
        int[] vertices = { 3594, 8328, 97848, 18096, 618, 954, 414, 570, 165, 462, 1050, 960, 828, 10692, 20382, 16590 };
        var parts = vertices.Select(Partition).ToList();
        if (args.Length > 0)
        {
            // Optional local verification on privately extracted real triangle indices.
            using var input = new BinaryReader(File.OpenRead(args[0]));
            Check(input.ReadInt32() == vertices.Length, "Scene mesh count changed");
            parts.Clear();
            for (int i = 0; i < vertices.Length; i++)
            {
                int count = input.ReadInt32(), indexCount = input.ReadInt32();
                Check(count == vertices[i], "Scene vertex count changed");
                int[] indices = Enumerable.Range(0, indexCount).Select(_ => input.ReadInt32()).ToArray();
                parts.Add(LegacyMeshGeometry.Partition(count, indices));
            }
            Check(input.BaseStream.Position == input.BaseStream.Length, "Unparsed geometry data");
        }
        ReportedSceneBindings(parts);
        SharedSourceMaterials();
        RepeatedNodeInstancesAndNames();
        AnimatedRendererBounds();
        BranchedDynamicBoneSelection();
        Console.WriteLine($"PASS: production BuildFromNode preserves split material/renderer identities, later mesh bindings, source materials, instanced nodes and animated bounds updates ({checks} checks; engine doubles, no Unity rendering).");
    }

    private static void AnimatedRendererBounds()
    {
        var scene = new Assimp.Scene();
        scene.Materials.Add(new Assimp.Material { Name = "m" });
        scene.Meshes.Add(new Assimp.Mesh { Name = "Body", HasBones = true });
        scene.Meshes.Add(new Assimp.Mesh { Name = "Face", HasMeshAnimationAttachments = true });
        scene.Meshes.Add(new Assimp.Mesh { Name = "Prop" });
        var root = new Assimp.Node { Name = "Root" };
        root.Children.Add(new Assimp.Node { Name = "Actor1", MeshIndices = new() { 0, 1, 2 } });
        root.Children.Add(new Assimp.Node { Name = "Actor2", MeshIndices = new() { 0, 1, 2 } });
        // Two chunks per source mesh and two instances: bounds must update on
        // every animated renderer, not just the first material or first actor.
        var parts = new List<List<LegacyMeshGeometry.MeshPart>>
            { Partition(70002), Partition(70002), Partition(70002) };
        for (int pass = 0; pass < 2; pass++)
        {
            var import = new Import(scene, parts, true);
            import.Build(root);
            var animated = import.Renderers.OfType<SkinnedMeshRenderer>().ToArray();
            Check(animated.Length == 8, "Bones and morph-only instances must retain all skinned chunks");
            foreach (var renderer in animated)
                Check(renderer.updateWhenOffscreen,
                    "Runtime animated renderer must recalculate its bounds even when offscreen: " + renderer.name);
            Check(import.Renderers.Count(r => r is MeshRenderer) == 4,
                "Static instances must keep normal MeshRenderer culling");
            Check(import.PendingArmatureCount == 4, "Bounds setup must not change bone/morph-only armature ownership");
        }
        // The second import uses the same path as source-cache reconstruction;
        // engine doubles assert setup only, not Unity's culling implementation.
    }

    private static void BranchedDynamicBoneSelection()
    {
        var root = new BoneNode(new GameObject("root"));
        var left = new BoneNode(new GameObject("left"), root);
        var right = new BoneNode(new GameObject("right"), root);
        var tip = new BoneNode(new GameObject("tip"), right);
        right.SetDynamic(true);
        Check(root.DynamicBoneInChildren(), "Dynamic roots on the second branch must be found");
        root.SetDynamic(true);
        Check(!root.IsDynamicRoot && right.IsDynamicRoot, "Parent must not overlap an existing root on any branch");
        left.SetDynamic(true);
        Check(left.IsDynamicRoot && right.IsDynamicRoot, "Independent branches may both simulate");
        left.SetDynamic(false);
        right.SetDynamic(false);
        Check(!tip.IsDynamic && !root.DynamicBoneInChildren(), "Clearing child roots resets descendants");
        root.SetDynamic(true);
        Check(root.IsDynamicRoot && left.IsDynamic && right.IsDynamic && tip.IsDynamic, "Parent root covers every branch");
        Check(root.DynamicChainLength == 4, "Chain count must cover the full subtree");
        tip.SetDynamic(true);
        Check(!tip.IsDynamicRoot, "A descendant must not become a competing root");
        root.SetDynamic(false);
        Check(!root.IsDynamic && !left.IsDynamic && !right.IsDynamic && !tip.IsDynamic, "Disabling parent clears every branch");
        right.SetDynamic(true);
        Check(right.IsDynamicRoot && tip.IsDynamic, "Subchain can be selected after its parent is disabled");
    }

    private static void ReportedSceneBindings(List<List<LegacyMeshGeometry.MeshPart>> parts)
    {
        var scene = new Assimp.Scene();
        var root = new Assimp.Node { Name = "Body" };
        for (int i = 0; i < parts.Count; i++)
        {
            scene.Meshes.Add(new Assimp.Mesh { Name = "Body", MaterialIndex = i, HasBones = true });
            scene.Materials.Add(new Assimp.Material { Name = "source" + i });
            root.MeshIndices.Add(i);
        }
        var import = new Import(scene, parts, true);
        import.Build(root);
        Check(import.Renderers.Count == 17, "Expected 16 source meshes and 17 renderer chunks");
        // MaterialEditor's saved texture/shader/copy records match by material name.
        int[] mainTextures = { 72, 72, 74, 76, 78, 81, 82, 85, 86, 89, 90, 92, 93, 102, 97, 97 };
        var saved = Enumerable.Range(0, 16).ToDictionary(i => Name(i), i => mainTextures[i]);
        int renderer = 0;
        for (int mesh = 0; mesh < parts.Count; mesh++)
        {
            for (int chunk = 0; chunk < parts[mesh].Count; chunk++)
            {
                var r = import.Renderers[renderer++];
                Check(r.name == Name(mesh), $"source mesh {mesh} chunk {chunk}: renderer became {r.name}, expected {Name(mesh)}");
                Check(r.material.name == Name(mesh), $"source mesh {mesh} chunk {chunk}: material identity shifted");
                Check(saved[r.material.name] == mainTextures[mesh], "MaterialEditor would restore another source mesh's texture");
            }
        }
        Check(import.Renderers[2].material.name == import.Renderers[3].material.name, "Both tree chunks must receive the tree material");
        Check(import.Renderers[4].material.name == "3_Body", "Ribbon must keep its original material key");
        Check(import.Renderers.Count(r => r.material.name == "13_Body") == 1, "Saved material-copy source must still identify the bell");
        Check(import.PendingArmatureCount == import.Renderers.Count, "Every skinned chunk needs an armature");
    }

    private static string Name(int i) => i == 0 ? "Body" : i + "_Body";

    private static void SharedSourceMaterials()
    {
        var scene = new Assimp.Scene();
        scene.Materials.Add(new Assimp.Material { Name = "paint" });
        scene.Meshes.Add(new Assimp.Mesh { Name = "Panel", HasMeshAnimationAttachments = true });
        var root = new Assimp.Node { Name = "Root", MeshIndices = new() { 0 } };
        var import = new Import(scene, new() { Partition(70002) }, false);
        import.Build(root);
        Check(import.Renderers.All(r => r.name == "Panel_paint" && r.material.name == "paint"), "Source-material mode must keep material and renderer identities");
        Check(import.Renderers.All(r => r is SkinnedMeshRenderer), "BlendShape chunks must remain skinned renderers");
    }

    private static void RepeatedNodeInstancesAndNames()
    {
        var scene = new Assimp.Scene();
        scene.Materials.Add(new Assimp.Material { Name = "m" });
        scene.Meshes.Add(new Assimp.Mesh { Name = "" });
        var root = new Assimp.Node { Name = "Root" };
        root.Children.Add(new Assimp.Node { Name = "Panel", MeshIndices = new() { 0 } });
        root.Children.Add(new Assimp.Node { Name = "Panel", MeshIndices = new() { 0 } });
        var import = new Import(scene, new() { Partition(70002) }, true);
        import.Build(root);
        Check(import.Renderers.Select(r => r.name).SequenceEqual(new[] { "Panel", "Panel", "1_Panel", "1_Panel" }), "Repeated nodes need distinct logical identities unaffected by split count");
        Check(import.Renderers.All(r => r is MeshRenderer), "Static chunks must remain static renderers");
    }
}
