using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Assimp;
using BepInEx.Logging;
using UnityEngine;
using Unity.Collections;
using Material = UnityEngine.Material;
using Mesh = UnityEngine.Mesh;
using IllusionUtility.GetUtility;
// ReSharper disable RedundantNameQualifier

namespace AssetImport
{
    /// <summary>
    /// Representation of an imported object in Unity.
    /// Also handles importing using AssimpNet.
    /// </summary>
	public class Import
	{
		private readonly bool _importBones;
		private string _cPath;
		private readonly Material _bMat;
		private readonly List<TexturePath> _tPaths;
		private readonly ManualLogSource _logger;
		private Assimp.Scene _scene;

        private readonly List<Material> _materials;
        private readonly List<Mesh> _meshes;
        private readonly List<Tuple<GameObject, Assimp.Mesh, SkinnedMeshRenderer>> _processArmaturesLater = new List<Tuple<GameObject, Assimp.Mesh, SkinnedMeshRenderer>>();

		public string SourceIdentifier { get; }
        public string SourceFileName => RamCacheUtility.GetFileName(SourceIdentifier);
        public List<Transform> Bones { get; }
		public List<Renderer> Renderers { get; }
        public List<BoneNode> BoneNodes { get; }
		public Dictionary<Material, List<TexturePath>> MaterialTextures { get; }
		public GameObject GameObject { get; private set; }
		public string CommonPath { get => GetCommonPath(); set => SetCommonPath(value); }

        public bool HasBones => Bones.Count > 0;
        public bool HasTextures => _tPaths.Count > 0;
        public bool IsLoaded { get; private set; }

        public readonly bool DoFbxTranslation;
        public readonly bool PerRendererMaterials;

		public Import(string identifierHash, bool importArmature = true, Material baseMat = null, bool doFbxTranslation = true, bool perRendererMaterials = false)
		{
			_logger = AssetImport.Logger;

			_importBones = importArmature;
			SourceIdentifier = identifierHash;
			if (!baseMat) baseMat = new Material(Shader.Find("Standard"));
			_bMat = baseMat;

			Bones = new List<Transform>();
			Renderers = new List<Renderer>();
			MaterialTextures = new Dictionary<Material, List<TexturePath>>();
			_tPaths = new List<TexturePath>();
            _materials = new List<Material>();
            _meshes = new List<Mesh>();
            BoneNodes = new List<BoneNode>();

            this.DoFbxTranslation = doFbxTranslation;
            this.PerRendererMaterials = perRendererMaterials;
		}

		private string GetCommonPath()
		{
			if (!HasTextures) return null;
            if (_cPath != null) return _cPath;
            List<string> paths = _tPaths.Select(p => p.Path).ToList();
            // yoinked from https://stackoverflow.com/questions/24866683/find-common-parent-path-in-list-of-files-and-directories
            int k = paths[0].Length;
            for (int i = 1; i < paths.Count; i++)
            {
                k = Math.Min(k, paths[i].Length);
                for (int j = 0; j < k; j++)
                {
                    if (paths[i][j] != paths[0][j])
                    {
                        k = j;
                        break;
                    }
                }
            }
            string common = paths[0].Substring(0, k);
            if (!common.EndsWith("/"))
            {
                common = common.Substring(0, common.LastIndexOf("/") + 1);
            }
            _cPath = common;
            return _cPath;
		}

		private void SetCommonPath(string newPath)
		{
			if (!HasTextures) return;
            newPath = newPath.Replace("\\", "/");
            if (!newPath.EndsWith("/")) newPath += "/";
            string oldPath = _cPath;
            _cPath = newPath;
            foreach(TexturePath p in _tPaths)
			{
				p.Path = p.Path.Replace(oldPath, newPath);
			}
        }

		public void Load()
		{
            _logger.LogDebug($"Loading of {RamCacheUtility.GetFileName(SourceIdentifier)} started");
			
            AssimpContext imp = new AssimpContext();
            imp.SetConfig(new Assimp.Configs.RemoveEmptyBonesConfig(false));
            
            List<string> extraFiles = RamCacheUtility.GetFileAdditionalFileHashes(SourceIdentifier);
            string temp = Path.GetTempPath();
            string folder = $"AssetImport_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            
            if (!extraFiles.IsNullOrEmpty())
            {
                try
                {
                    _logger.LogInfo($"File has additional files. Because of a limitation of the assimp wrapper the files have to be written to disk in order to be loaded!");
                    Directory.CreateDirectory(Path.Combine(temp, folder));
                    File.WriteAllBytes(Path.Combine(temp, folder, RamCacheUtility.GetFileName(SourceIdentifier)), RamCacheUtility.GetFileBlob(SourceIdentifier));
                    extraFiles.ForEach(file => File.WriteAllBytes(Path.Combine(temp, folder, RamCacheUtility.GetFileName(file)), RamCacheUtility.GetFileBlob(file)));

                    // load asset from file on disk to be able to load extra files.
                    _scene = imp.ImportFile(Path.Combine(temp, folder, RamCacheUtility.GetFileName(SourceIdentifier)), PostProcessSteps.MakeLeftHanded | PostProcessSteps.Triangulate);
                }
                catch(IOException e)
                {
                    _logger.LogError($"I/O error when trying to write the file: {e.Message}");
                    return;
                }
            }
            else
            {
                // load asset from stream
			    _scene = imp.ImportFileFromStream(RamCacheUtility.GetFileStream(SourceIdentifier), PostProcessSteps.MakeLeftHanded | PostProcessSteps.Triangulate);
            }
            
            if (_scene == null)
			{
				_logger.LogError("Assimp Import failed, aborting load process");
				return;
			}

            if (!PerRendererMaterials)
                ProcessMaterials(); // convert materials
            ProcessMeshes(); // convert meshes
			GameObject = BuildFromNode(_scene.RootNode); // convert SceneStructure & assign meshes and materials
            ProcessArmatures(); // convert armature for meshes with bones
            BuildBoneNodeTree(GameObject, 0, null);
            IsLoaded = true;

            if (extraFiles.IsNullOrEmpty()) return;
            {
                try
                {
                    File.Delete(Path.Combine(temp, folder, RamCacheUtility.GetFileName(SourceIdentifier)));
                    extraFiles.ForEach(file => File.Delete(Path.Combine(temp, folder, RamCacheUtility.GetFileName(file))));
                }
                catch (Exception e)
                {
                    _logger.LogError($"Cleanup failed: {e.Message}");
                }
            }
        }

		private static void ConvertTransform(Assimp.Matrix4x4 aTransform, Transform uTransform)
		{
            // Decompose Assimp transform into scale, rot and translation 
            aTransform.Decompose(out Assimp.Vector3D aScale, out Assimp.Quaternion aQuat, out Assimp.Vector3D aTranslation);

            // Convert Assimp transform into Unity transform and set transformation of game object 
            UnityEngine.Quaternion uQuat = new UnityEngine.Quaternion(aQuat.X, aQuat.Y, aQuat.Z, aQuat.W);
            Vector3 euler = uQuat.eulerAngles;
            uTransform.localScale = new UnityEngine.Vector3(aScale.X, aScale.Y, aScale.Z);
            uTransform.localPosition = new UnityEngine.Vector3(aTranslation.X, aTranslation.Y, aTranslation.Z);
            uTransform.localRotation = UnityEngine.Quaternion.Euler(euler.x, euler.y, euler.z);
        }

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

            if (!DoFbxTranslation || !(node.Name.Contains("$AssimpFbx$_Translation") && _scene.RootNode.Equals(node.Parent)))
			    ConvertTransform(node.Transform, nodeObject.transform);

			if (node.HasMeshes)
			{
				foreach(int meshIndex in node.MeshIndices)
				{
					Assimp.Mesh mesh = _scene.Meshes[meshIndex];
                    Mesh uMesh = _meshes[meshIndex];

                    string meshName = mesh.Name;
                    if (meshName.IsNullOrEmpty())
                    {
                        if (node.Name.IsNullOrEmpty())
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

                    // nameConvention to create unique name: meshName_materialName
                    GameObject subObjet = new GameObject(subobjectName);
					subObjet.transform.SetParent(nodeObject.transform, true);
					// set layer to 10 for koi
					subObjet.layer = 10;

                    Renderer rend;

					if (mesh.HasBones && _importBones)
					{
                        rend = subObjet.AddComponent<SkinnedMeshRenderer>();
                        ((SkinnedMeshRenderer)rend).sharedMesh = uMesh;

                        _processArmaturesLater.Add(new Tuple<GameObject, Assimp.Mesh, SkinnedMeshRenderer>(subObjet, mesh, (SkinnedMeshRenderer)rend));
					}
					else
					{
                        MeshFilter mFilter = subObjet.AddComponent<MeshFilter>();
                        mFilter.mesh = uMesh;
						rend = subObjet.AddComponent<MeshRenderer>();
					}

                    rend.name = subobjectName;
                    Material uMaterial = PerRendererMaterials ? GetNewMaterialWithName(subobjectName) : _materials[mesh.MaterialIndex];
                    rend.material = uMaterial;
                    Renderers.Add(rend);
				}
			}

            if (!node.HasChildren) return nodeObject;
            foreach (Node child in node.Children)
            {
                GameObject childObject = BuildFromNode(child);
                childObject.transform.SetParent(nodeObject.transform, false);
            }
            return nodeObject;
        }

		private void ProcessMaterials()
		{
            _logger.LogDebug("Processing Materials");
			foreach(Assimp.Material material in _scene.Materials)
			{
                _logger.LogDebug($"Processing Material: {material.Name}");
				Material uMaterial = new Material(_bMat)
                {
                    name = material.Name
                };
                // Albedo
                if (material.HasColorDiffuse)
                {
                    Color color = new Color(
                        material.ColorDiffuse.R,
                        material.ColorDiffuse.G,
                        material.ColorDiffuse.B,
                        material.ColorDiffuse.A
                    );
                    uMaterial.color = color;
                }
                /* TODO: shader specific
                // Emission
                if (material.HasColorEmissive)
                {
                    Color color = new Color(
                        material.ColorEmissive.R,
                        material.ColorEmissive.G,
                        material.ColorEmissive.B,
                        material.ColorEmissive.A
                    );
                    uMaterial.SetColor("_EmissionColor", color);
                    uMaterial.EnableKeyword("_EMISSION");
                }

                // Reflectivity
                if (material.HasReflectivity)
                {
                    uMaterial.SetFloat("_Glossiness", material.Reflectivity);
                }
                */
                // Texture
                MaterialTextures[uMaterial] = new List<TexturePath>();
                if (material.HasTextureDiffuse)
                {
                    TexturePath tP = new TexturePath(uMaterial, TextureType.Diffuse, material.TextureDiffuse.FilePath);
                    MaterialTextures[uMaterial].Add(tP);
                    _tPaths.Add(tP);
                }
                if (material.HasTextureDisplacement)
                {
                    TexturePath tP = new TexturePath(uMaterial, TextureType.Displacement, material.TextureDisplacement.FilePath);
                    MaterialTextures[uMaterial].Add(tP);
                    _tPaths.Add(tP);
                }
                if (material.HasTextureEmissive)
                {
                    TexturePath tP = new TexturePath(uMaterial, TextureType.Emissive, material.TextureEmissive.FilePath);
                    MaterialTextures[uMaterial].Add(tP);
                    _tPaths.Add(tP);
                }
                if (material.HasTextureHeight)
                {
                    TexturePath tP = new TexturePath(uMaterial, TextureType.Height, material.TextureHeight.FilePath);
                    MaterialTextures[uMaterial].Add(tP);
                    _tPaths.Add(tP);
                }
                if (material.HasTextureLightMap)
                {
                    TexturePath tP = new TexturePath(uMaterial, TextureType.Lightmap, material.TextureLightMap.FilePath);
                    MaterialTextures[uMaterial].Add(tP);
                    _tPaths.Add(tP);
                }
                if (material.HasTextureNormal)
                {
                    TexturePath tP = new TexturePath(uMaterial, TextureType.Normals, material.TextureNormal.FilePath);
                    MaterialTextures[uMaterial].Add(tP);
                    _tPaths.Add(tP);
                }
                if (material.HasTextureOpacity)
                {
                    TexturePath tP = new TexturePath(uMaterial, TextureType.Opacity, material.TextureOpacity.FilePath);
                    MaterialTextures[uMaterial].Add(tP);
                    _tPaths.Add(tP);
                }
                if (material.HasTextureReflection)
                {
                    TexturePath tP = new TexturePath(uMaterial, TextureType.Reflection, material.TextureReflection.FilePath);
                    MaterialTextures[uMaterial].Add(tP);
                    _tPaths.Add(tP);
                }
                if (material.HasTextureSpecular)
                {
                    TexturePath tP = new TexturePath(uMaterial, TextureType.Specular, material.TextureSpecular.FilePath);
                    MaterialTextures[uMaterial].Add(tP);
                    _tPaths.Add(tP);
                }

                _materials.Add(uMaterial);
            }
        }

        private void ProcessMeshes()
        {
            _logger.LogDebug("Processing Meshes");
            if (!_scene.HasMeshes) return;

            foreach(Assimp.Mesh mesh in _scene.Meshes)
            {
                _logger.LogDebug($"Converting Mesh: {mesh.Name}");
                Mesh uMesh = new Mesh();
                var uVertices = new List<Vector3>();
                var uNormals = new List<Vector3>();
                var uUv = new List<Vector2>();
                var uIndices = new List<int>();

                // Vertices
                if (mesh.HasVertices)
                {
                    uVertices.AddRange(mesh.Vertices.Select(v => new Vector3(v.X, v.Y, v.Z)));
                }

                // Normals
                if (mesh.HasNormals)
                {
                    uNormals.AddRange(mesh.Normals.Select(n => new Vector3(n.X, n.Y, n.Z)));
                }

                // Triangles
                if (mesh.HasFaces)
                {
                    foreach (Face f in mesh.Faces.Where(f => f.IndexCount != 1 && f.IndexCount != 2))
                    {
                        for (int i = 0; i < (f.IndexCount - 2); i++)
                        {
                            uIndices.Add(f.Indices[i + 2]);
                            uIndices.Add(f.Indices[i + 1]);
                            uIndices.Add(f.Indices[0]);
                        }
                    }
                }

                // Uv (texture coordinate) 
                if (mesh.HasTextureCoords(0))
                {
                    uUv.AddRange(mesh.TextureCoordinateChannels[0].Select(uv => new Vector2(uv.X, uv.Y)));
                }

                if (uVertices.Count > 65000) uMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                uMesh.name = mesh.Name;
                uMesh.vertices = uVertices.ToArray();
                uMesh.normals = uNormals.ToArray();
                uMesh.triangles = uIndices.ToArray();
                uMesh.uv = uUv.ToArray();

                if (mesh.HasMeshAnimationAttachments)
                {
                    ProcessBlendshapes(mesh, uMesh);
                }

                _meshes.Add(uMesh);
            }
        }

        private static void ProcessBlendshapes(Assimp.Mesh sourceMesh, Mesh targetMesh)
        {
            foreach (MeshAnimationAttachment meshAnimation in sourceMesh.MeshAnimationAttachments)
            {
                var vertDeltas = new Vector3[sourceMesh.VertexCount];

                for (var i = 0; i < sourceMesh.VertexCount; i++)
                {
                    Vector3D assimpVert = meshAnimation.Vertices[i];
                    Vector3 sourceVert = new Vector3(assimpVert.X, assimpVert.Y, assimpVert.Z);
                    vertDeltas[i] = sourceVert - targetMesh.vertices[i];
                }

                Vector3[] normalsDeltas;

                if (meshAnimation.HasNormals)
                {
                    normalsDeltas = new Vector3[sourceMesh.VertexCount];

                    for (var i = 0; i < sourceMesh.VertexCount; i++)
                    {
                        Vector3D assimpNorm = meshAnimation.Normals[i];
                        Vector3 sourceNorm = new Vector3(assimpNorm.X, assimpNorm.Y, assimpNorm.Z);
                        normalsDeltas[i] = sourceNorm - targetMesh.normals[i];
                    }
                }
                else
                {
                    normalsDeltas = null;
                }

                Vector3[] tangentsDeltas;

                if (meshAnimation.Tangents.Count > 0)
                {
                    tangentsDeltas = new Vector3[sourceMesh.VertexCount];

                    for (var i = 0; i < sourceMesh.VertexCount; i++)
                    {
                        Vector3D assimpTang = meshAnimation.Tangents[i];
                        Vector3 sourceTang = new Vector3(assimpTang.X, assimpTang.Y, assimpTang.Z);
                        tangentsDeltas[i] = sourceTang - (Vector3)targetMesh.tangents[i];
                    }
                }
                else
                {
                    tangentsDeltas = null;
                }

                targetMesh.AddBlendShapeFrame(meshAnimation.Name, meshAnimation.Weight * 100, vertDeltas, normalsDeltas, tangentsDeltas);
            }
        }

        private void ProcessArmatures()
        {
            if (_processArmaturesLater.IsNullOrEmpty()) return;
            foreach (Tuple<GameObject, Assimp.Mesh, SkinnedMeshRenderer> g in _processArmaturesLater)
            {
                ProcessArmature(g.Item2, g.Item3, g.Item1.name);
            }
        }

        private static UnityEngine.Matrix4x4 ConvertBindpose(Assimp.Matrix4x4 offsetMatrix)
        {
            offsetMatrix.Decompose(out Vector3D aScl, out Assimp.Quaternion aQ, out Vector3D aPos);
            Vector3 pos = new Vector3(aPos.X, aPos.Y, aPos.Z);
            UnityEngine.Quaternion q = new UnityEngine.Quaternion(aQ.X, aQ.Y, aQ.Z, aQ.W);
            Vector3 s = new Vector3(aScl.X, aScl.Y, aScl.Z);

            UnityEngine.Matrix4x4 bindPose = UnityEngine.Matrix4x4.TRS(pos, q, s);
            return bindPose;
        }

        private void ProcessArmature(Assimp.Mesh mesh, SkinnedMeshRenderer renderer, string name)
        {
            _logger.LogDebug($"Processing Armature on Mesh: {name}");
            Mesh uMesh = renderer.sharedMesh;
            // helper Dict<vertexIndex, List<Tuple<boneIndex, weight>>>
            var helper = new Dictionary<int, List<Tuple<int, float>>>();
            var bindposes = new UnityEngine.Matrix4x4[mesh.BoneCount];
            var rendBones = new List<Transform>();

            for (int i = 0; i < mesh.BoneCount; i++) // for bone in mesh
            {
                // weights - fill helper
                Bone bone = mesh.Bones[i];
                foreach (VertexWeight vWeight in bone.VertexWeights)
                {
                    if (!helper.ContainsKey(vWeight.VertexID))
                        helper[vWeight.VertexID] = new List<Tuple<int, float>>();
                    helper[vWeight.VertexID].Add(new Tuple<int, float>(i, vWeight.Weight));
                }

                // bindpose
                bindposes[i] = ConvertBindpose(bone.OffsetMatrix);

                // bone
                Transform uBone = GameObject.transform.FindLoop(bone.Name).transform;
                if (!Bones.Contains(uBone)) Bones.Add(uBone);
                rendBones.Add(uBone);
            }

            // fill bones on renderer
            renderer.bones = rendBones.ToArray();

            // fill bindposes on mesh
            uMesh.bindposes = bindposes;

            // normalize vertex weights if necessary 
            foreach (int vertexID in helper.Keys)
            {
                float totalWeight = helper[vertexID].Sum(tu => tu.Item2);
                if (!(totalWeight > 1f)) continue;
                for (int i = 0; i < helper[vertexID].Count; i++)
                {
                    float newWeight = helper[vertexID][i].Item2 / totalWeight;
                    helper[vertexID][i] = new Tuple<int, float>(helper[vertexID][i].Item1, newWeight);
                }
            }

            var bonesPerVertex = new byte[mesh.VertexCount];
            var weights = new List<BoneWeight1>();

            // create unity boneWeights
            for (int i = 0; i < mesh.VertexCount; i++) // for vertex in mesh
            {
                var lweights = new List<BoneWeight1>();
                if (helper.ContainsKey(i))
                {
                    bonesPerVertex[i] = (byte)helper[i].Count;
                    foreach (BoneWeight1 w in helper[i].Select(wt => new BoneWeight1
                             {
                                 boneIndex = wt.Item1,
                                 weight = wt.Item2
                             }))
                    {
                        // add to list (sorted by weight)
                        if (lweights.Count == 0)
                            lweights.Add(w);
                        else
                        {
                            for (int x = 0; x < lweights.Count; x++)
                            {
                                if (w.weight >= lweights[x].weight)
                                {
                                    lweights.Insert(x, w);
                                    break;
                                }
                                else if (x == lweights.Count - 1)
                                {
                                    lweights.Add(w);
                                    break;
                                }
                            }
                        }
                    }
                }
                else // if vertex has no weight, give it a weight and set it to 0
                {
                    bonesPerVertex[i] = 1;
                    BoneWeight1 w = new BoneWeight1
                    {
                        boneIndex = 0,
                        weight = 0
                    };
                    lweights.Add(w);
                }
                weights.AddRange(lweights);
            }

            uMesh.SetBoneWeights(
                new NativeArray<byte>(bonesPerVertex, Allocator.Persistent),
                new NativeArray<BoneWeight1>(weights.ToArray(), Allocator.Persistent)
            );
        }

        private void BuildBoneNodeTree(GameObject go, int depth, BoneNode parent)
        {
            if (Bones.Contains(go.transform))
            {
                parent = new BoneNode(go, parent, depth);
                BoneNodes.Add(parent);
                depth++;
            }

            if (go.transform.childCount <= 0) return;
            for (int i = 0; i < go.transform.childCount; i++)
            {
                BuildBoneNodeTree(go.transform.GetChild(i).gameObject, depth, parent);
            }
        }
	}
}