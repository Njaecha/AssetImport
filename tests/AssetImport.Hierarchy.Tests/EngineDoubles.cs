// Inert engine doubles: execute production hierarchy/material assignment without
// loading Unity. These deliberately do not simulate rendering or game transforms.
namespace UnityEngine
{
    public class Material
    {
        public string name;
        public Material(Material basis) { name = basis?.name; }
    }
    public class Mesh { public string name; }
    public class GameObject
    {
        public string name;
        public int layer;
        public Transform transform;
        public GameObject(string name) { this.name = name; transform = new Transform(this); }
        public T AddComponent<T>() where T : Renderer, new() => new T { owner = this };
    }
    public class Transform
    {
        public GameObject gameObject;
        public List<Transform> children = new();
        public Transform(GameObject go) { gameObject = go; }
        public void SetParent(Transform parent, bool worldPositionStays) { parent.children.Add(this); }
    }
    public class Renderer
    {
        public GameObject owner;
        public string name { get => owner.name; set => owner.name = value; }
        public Material material;
    }
    public class MeshRenderer : Renderer { }
    public class MeshFilter : Renderer { public Mesh mesh; }
    public class SkinnedMeshRenderer : Renderer
    {
        public Mesh sharedMesh;
        public bool updateWhenOffscreen;
    }
}
namespace Assimp
{
    public class Mesh
    {
        public string Name;
        public int MaterialIndex;
        public bool HasBones, HasMeshAnimationAttachments;
    }
    public class Material { public string Name; }
    public class Node
    {
        public string Name;
        public object Transform;
        public List<int> MeshIndices = new();
        public List<Node> Children = new();
        public bool HasMeshes => MeshIndices.Count > 0;
        public bool HasChildren => Children.Count > 0;
    }
    public class Scene
    {
        public List<Mesh> Meshes = new();
        public List<Material> Materials = new();
    }
}
namespace AssetImport
{
    public partial class Import
    {
        private UnityEngine.Material _bMat = new(null);
        private Assimp.Scene _scene;
        private List<UnityEngine.Material> _materials = new();
        private List<List<ConvertedMesh>> _meshes = new();
        private List<PendingArmature> _processArmaturesLater = new();
        public List<UnityEngine.Renderer> Renderers = new();
        public bool PerRendererMaterials, ImportBones = true;
        private class ConvertedMesh { public UnityEngine.Mesh Mesh; public int[] SourceVertices; }
        private class PendingArmature
        {
            public Assimp.Mesh Source;
            public UnityEngine.SkinnedMeshRenderer Renderer;
            public int[] SourceVertices;
        }
        private static void ConvertTransform(object matrix, UnityEngine.Transform transform) { }
        internal Import(Assimp.Scene source, List<List<LegacyMeshGeometry.MeshPart>> parts, bool perRenderer)
        {
            _scene = source;
            PerRendererMaterials = perRenderer;
            _materials = source.Materials.Select(m => new UnityEngine.Material(null) { name = m.Name }).ToList();
            _meshes = parts.Select(group => group.Select(p => new ConvertedMesh
                { Mesh = new UnityEngine.Mesh(), SourceVertices = p.SourceVertices }).ToList()).ToList();
        }
        public void Build(Assimp.Node root) { BuildFromNode(root); }
        public int PendingArmatureCount => _processArmaturesLater.Count;
    }
}
