using System.Collections.Generic;
using UnityEngine;
using Material = UnityEngine.Material;
using Mesh = UnityEngine.Mesh;

namespace AssetImport
{
    public partial class Import
    {
        private readonly List<string> _subobjectNameList = new List<string>();

        private Material GetNewMaterialWithName(string name)
        {
            Material material = new Material(_bMat)
            {
                name = name
            };
            return material;
        }

        private GameObject BuildFromNode(Assimp.Node node)
        {
            GameObject nodeObject = new GameObject(node.Name);

            // since the new assimp version doesn't spawn $AssimpFbx$_Translation nodes, the second operant will always be true, defeating the purpose of this if
            // lines kept for documentation purposes
            /*
            if (!DoFbxTranslation || !(node.Name.Contains("$AssimpFbx$_Translation") && _scene.RootNode.Equals(node.Parent)))
            {
                UnityEngine.Matrix4x4 unityMatrix = ConvertTransform(node.Transform, nodeObject.transform);
            }
            */
            ConvertTransform(node.Transform, nodeObject.transform);

            if (node.HasMeshes)
            {
                foreach(int meshIndex in node.MeshIndices)
                {
                    Assimp.Mesh mesh = _scene.Meshes[meshIndex];
                    string meshName = mesh.Name;
                    if (string.IsNullOrEmpty(meshName))
                    {
                        if (string.IsNullOrEmpty(node.Name))
                        {
                            meshName = node.MeshIndices.Count > 1 ? $"Unnamed_{meshIndex}" : $"Unnamed";
                        }
                        else
                        {
                            meshName = node.MeshIndices.Count > 1 ? $"{node.Name}_{meshIndex}" : node.Name;
                        }
                    }

                    string materialName = _scene.Materials[mesh.MaterialIndex].Name;
                    string subobjectName = !PerRendererMaterials ? $"{meshName}_{materialName}" : meshName;

                    if (_subobjectNameList.Contains(subobjectName))
                    {
                        var counter = 1;
                        while (_subobjectNameList.Contains($"{counter}_{subobjectName}"))
                        {
                            counter++;
                        }
                        subobjectName = $"{counter}_{subobjectName}";
                    }
                    _subobjectNameList.Add(subobjectName);

                    // MaterialEditor saves material and renderer edits by name.
                    // Allocate identity once per source-mesh instance, before the
                    // Unity 5.6 chunks: a split must not consume the next mesh's name.
                    Material uMaterial = PerRendererMaterials ? GetNewMaterialWithName(subobjectName) : _materials[mesh.MaterialIndex];
                    foreach (ConvertedMesh converted in _meshes[meshIndex])
                    {
                        Mesh uMesh = converted.Mesh;
                        // All chunks keep the same renderer name too, so saved
                        // renderer flags and material renames reach every chunk.
                        GameObject subObject = new GameObject(subobjectName);
#if KK
                        // Vertices are in the Assimp node's local space. Preserve
                        // the identity local transform when adding a renderer.
                        subObject.transform.SetParent(nodeObject.transform, false);
#else
                        subObject.transform.SetParent(nodeObject.transform, true);
#endif
                        // set layer to 10 for koi
                        subObject.layer = 10;

                        Renderer rend;

                        if (mesh.HasBones && ImportBones)
                        {
                            rend = subObject.AddComponent<SkinnedMeshRenderer>();
                            ((SkinnedMeshRenderer)rend).sharedMesh = uMesh;

                            _processArmaturesLater.Add(new PendingArmature
                            {
                                Source = mesh,
                                Renderer = (SkinnedMeshRenderer)rend,
                                SourceVertices = converted.SourceVertices
                            });
                        }
                        else if (mesh.HasMeshAnimationAttachments) // mesh doesn't have bones but has Blendshapes.
                        {
                            rend = subObject.AddComponent<SkinnedMeshRenderer>();
                            ((SkinnedMeshRenderer)rend).sharedMesh = uMesh;
                        }
                        else
                        {
                            MeshFilter mFilter = subObject.AddComponent<MeshFilter>();
                            mFilter.mesh = uMesh;
                            rend = subObject.AddComponent<MeshRenderer>();
                        }

                        // Runtime imports have no precomputed animation bounds.
                        // MMDD, FK and DynamicBone can move vertices beyond the
                        // bind-pose bounds, including while a chunk is offscreen.
                        // Let Unity update each skinned chunk's bounds every frame
                        // instead of culling it against an outdated pose.
                        var skinnedRenderer = rend as SkinnedMeshRenderer;
                        if (skinnedRenderer != null) skinnedRenderer.updateWhenOffscreen = true;

                        rend.name = subobjectName;
                        rend.material = uMaterial;
                        Renderers.Add(rend);
                    }
                }
            }

            if (!node.HasChildren) return nodeObject;
            foreach (Assimp.Node child in node.Children)
            {
                GameObject childObject = BuildFromNode(child);
                childObject.transform.SetParent(nodeObject.transform, false);
            }
            return nodeObject;
        }

    }
}
