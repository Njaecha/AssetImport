using System;
using System.Collections.Generic;
using System.IO;
using Assimp;
using BepInEx.Logging;
using UnityEngine;
using Unity.Collections;
using Material = UnityEngine.Material;
using Mesh = UnityEngine.Mesh;
using IllusionUtility.GetUtility;

namespace AssetImport
{
    /// <summary>
    /// Representation of a imported object in Unity.
    /// Also handels importing using AssimpNet.
    /// </summary>
	public class Import
	{
		private bool importBones;
		private string cPath = null;
		private Material bMat;
		private List<TexturePath> tPaths;
		private ManualLogSource Logger;
		private Assimp.Scene scene;

        private List<Material> materials;
        private List<Mesh> meshes;
        private List<Tuple<GameObject, Assimp.Mesh, SkinnedMeshRenderer>> processArmaturesLater = new List<Tuple<GameObject, Assimp.Mesh, SkinnedMeshRenderer>>();

		public string sourcePath { get; private set; }
        public string sourceFileName { get => Path.GetFileName(sourcePath); }
		public List<Transform> bones { get; private set; }
		public List<Renderer> renderers { get; private set; }
        public List<BoneNode> boneNodes { get; private set; }
		public Dictionary<Material, List<TexturePath>> materialTextures { get; private set; }
		public GameObject gameObject { get; private set; }
		public string commonPath { get => getCommonPath(); set => setCommonPath(value); }
        public string copiedPath { get; private set; }

		public bool hasBones { get => bones.Count > 0; }
		public bool hasTextures { get => tPaths.Count > 0; }
		public bool isLoaded { get; private set; } = false;

        public readonly bool doFbxTranslation;
        public readonly bool perRendererMaterials;

		public Import(string path, bool importArmature = true, Material baseMat = null, bool doFbxTranslation = true, bool perRendererMaterials = false)
		{
			Logger = AssetImport.Logger;

			importBones = importArmature;
			sourcePath = path.Replace("\\", "/");
			if (baseMat == null) baseMat = new Material(Shader.Find("Standard"));
			bMat = baseMat;

			bones = new List<Transform>();
			renderers = new List<Renderer>();
			materialTextures = new Dictionary<Material, List<TexturePath>>();
			tPaths = new List<TexturePath>();
            materials = new List<Material>();
            meshes = new List<Mesh>();
            boneNodes = new List<BoneNode>();

            this.doFbxTranslation = doFbxTranslation;
            this.perRendererMaterials = perRendererMaterials;
		}

		private string getCommonPath()
		{
			if (!hasTextures) return null;
			if (cPath == null)
			{
				List<string> paths = new List<string>();
				foreach(TexturePath p in tPaths)
				{
					paths.Add(p.path);
				}
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
				cPath = common;
            }
			return cPath;
		}

		private void setCommonPath(string newPath)
		{
			if (!hasTextures) return;
            newPath = newPath.Replace("\\", "/");
            if (!newPath.EndsWith("/")) newPath += "/";
            string oldPath = cPath;
            cPath = newPath;
            foreach(TexturePath p in tPaths)
			{
				p.path = p.path.Replace(oldPath, newPath);
			}
        }

		public void Load()
		{
            Logger.LogDebug($"Loading of {sourcePath} started");
			if (!File.Exists(sourcePath))
			{
				Logger.LogError($"File {sourcePath} does not exist");
				return;
			}

			AssimpContext imp = new AssimpContext();
            imp.SetConfig(new Assimp.Configs.RemoveEmptyBonesConfig(false));
			scene = imp.ImportFile(sourcePath, PostProcessSteps.MakeLeftHanded | PostProcessSteps.Triangulate);
			if (scene == null)
			{
				Logger.LogError("Assimp Import failed, aborting load process");
				return;
			}

            if (!perRendererMaterials)
                processMaterials(); // convert materials
            processMeshes(); // convert meshes
			gameObject = buildFromNode(scene.RootNode); // convert SceneStructure & assign meshes and materials
            processArmatures(); // convert armature for meshes with bones
            buildBoneNodeTree(gameObject, 0, null);
            isLoaded = true;
		}

		private void convertTransfrom(Assimp.Matrix4x4 aTransform, Transform uTransform)
		{
            // Decompose Assimp transform into scale, rot and translation 
            Assimp.Vector3D aScale = new Assimp.Vector3D();
            Assimp.Quaternion aQuat = new Assimp.Quaternion();
            Assimp.Vector3D aTranslation = new Assimp.Vector3D();
            aTransform.Decompose(out aScale, out aQuat, out aTranslation);

            // Convert Assimp transfrom into Unity transform and set transformation of game object 
            UnityEngine.Quaternion uQuat = new UnityEngine.Quaternion(aQuat.X, aQuat.Y, aQuat.Z, aQuat.W);
            var euler = uQuat.eulerAngles;
            uTransform.localScale = new UnityEngine.Vector3(aScale.X, aScale.Y, aScale.Z);
            uTransform.localPosition = new UnityEngine.Vector3(aTranslation.X, aTranslation.Y, aTranslation.Z);
            uTransform.localRotation = UnityEngine.Quaternion.Euler(euler.x, euler.y, euler.z);
        }

        private List<string> subobjectNameList = new List<string>();

        private Material getNewMaterialWithName(string name)
        {
            Material material = new Material(bMat);
            material.name = name;
            return material;
        }

		private GameObject buildFromNode(Assimp.Node node)
		{
            GameObject nodeObject = new GameObject(node.Name);

            if (!doFbxTranslation || !(node.Name.Contains("$AssimpFbx$_Translation") && scene.RootNode.Equals(node.Parent)))
			    convertTransfrom(node.Transform, nodeObject.transform);

			if (node.HasMeshes)
			{
				foreach(int meshIndex in node.MeshIndices)
				{
					Assimp.Mesh mesh = scene.Meshes[meshIndex];
                    Mesh uMesh = meshes[meshIndex];

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
                    string subobjectName;
                    string materialName = scene.Materials[mesh.MaterialIndex].Name;
                    if (!perRendererMaterials)
                    {
                        subobjectName = $"{meshName}_{materialName}";
                    }
                    else
                    {
                        subobjectName = meshName;
                        materialName = meshName;
                    }

                    if (subobjectNameList.Contains(subobjectName))
                    {
                        int counter = 1;
                        while (subobjectNameList.Contains($"{counter}_{subobjectName}"))
                        {
                            counter++;
                        }
                        subobjectName = $"{counter}_{subobjectName}";
                    }
                    subobjectNameList.Add(subobjectName);

                    // nameConvention to create unique name: meshName_materialName
                    GameObject subObjet = new GameObject(subobjectName);
					subObjet.transform.SetParent(nodeObject.transform, true);
					// set layer to 10 for koi
					subObjet.layer = 10;

                    Renderer rend;

					if (mesh.HasBones && importBones)
					{
                        rend = subObjet.AddComponent<SkinnedMeshRenderer>();
                        ((SkinnedMeshRenderer)rend).sharedMesh = uMesh;

                        processArmaturesLater.Add(new Tuple<GameObject, Assimp.Mesh, SkinnedMeshRenderer>(subObjet, mesh, rend as SkinnedMeshRenderer));
					}
					else
					{
                        MeshFilter mFilter = subObjet.AddComponent<MeshFilter>();
                        mFilter.mesh = uMesh;
						rend = subObjet.AddComponent<MeshRenderer>();
					}

                    rend.name = subobjectName;
                    Material uMaterial = perRendererMaterials ? getNewMaterialWithName(subobjectName) : materials[mesh.MaterialIndex];
                    rend.material = uMaterial;
                    renderers.Add(rend);
				}
			}

			if (node.HasChildren)
			{
				foreach (Node child in node.Children)
				{
					GameObject childObject = buildFromNode(child);
					childObject.transform.SetParent(nodeObject.transform, false);
				}
			}
			return nodeObject;
        }

		private void processMaterials()
		{
            Logger.LogDebug("Processing Materials");
			foreach(Assimp.Material material in scene.Materials)
			{
                Logger.LogDebug($"Processing Material: {material.Name}");
				Material uMaterial = new Material(bMat);
                uMaterial.name = material.Name;
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
                materialTextures[uMaterial] = new List<TexturePath>();
                if (material.HasTextureDiffuse)
                {
                    TexturePath tP = new TexturePath(uMaterial, TextureType.Diffuse, material.TextureDiffuse.FilePath);
                    materialTextures[uMaterial].Add(tP);
                    tPaths.Add(tP);
                }
                if (material.HasTextureDisplacement)
                {
                    TexturePath tP = new TexturePath(uMaterial, TextureType.Displacement, material.TextureDisplacement.FilePath);
                    materialTextures[uMaterial].Add(tP);
                    tPaths.Add(tP);
                }
                if (material.HasTextureEmissive)
                {
                    TexturePath tP = new TexturePath(uMaterial, TextureType.Emissive, material.TextureEmissive.FilePath);
                    materialTextures[uMaterial].Add(tP);
                    tPaths.Add(tP);
                }
                if (material.HasTextureHeight)
                {
                    TexturePath tP = new TexturePath(uMaterial, TextureType.Height, material.TextureHeight.FilePath);
                    materialTextures[uMaterial].Add(tP);
                    tPaths.Add(tP);
                }
                if (material.HasTextureLightMap)
                {
                    TexturePath tP = new TexturePath(uMaterial, TextureType.Lightmap, material.TextureLightMap.FilePath);
                    materialTextures[uMaterial].Add(tP);
                    tPaths.Add(tP);
                }
                if (material.HasTextureNormal)
                {
                    TexturePath tP = new TexturePath(uMaterial, TextureType.Normals, material.TextureNormal.FilePath);
                    materialTextures[uMaterial].Add(tP);
                    tPaths.Add(tP);
                }
                if (material.HasTextureOpacity)
                {
                    TexturePath tP = new TexturePath(uMaterial, TextureType.Opacity, material.TextureOpacity.FilePath);
                    materialTextures[uMaterial].Add(tP);
                    tPaths.Add(tP);
                }
                if (material.HasTextureReflection)
                {
                    TexturePath tP = new TexturePath(uMaterial, TextureType.Reflection, material.TextureReflection.FilePath);
                    materialTextures[uMaterial].Add(tP);
                    tPaths.Add(tP);
                }
                if (material.HasTextureSpecular)
                {
                    TexturePath tP = new TexturePath(uMaterial, TextureType.Specular, material.TextureSpecular.FilePath);
                    materialTextures[uMaterial].Add(tP);
                    tPaths.Add(tP);
                }

                materials.Add(uMaterial);
            }
        }

        private void processMeshes()
        {
            Logger.LogDebug("Processing Meshes");
            if (!scene.HasMeshes) return;
            foreach(Assimp.Mesh mesh in scene.Meshes)
            {
                Logger.LogDebug($"Converting Mesh: {mesh.Name}");
                Mesh uMesh = new Mesh();
                List<Vector3> uVertices = new List<Vector3>();
                List<Vector3> uNormals = new List<Vector3>();
                List<Vector2> uUv = new List<Vector2>();
                List<int> uIndices = new List<int>();

                // Vertices
                if (mesh.HasVertices)
                {
                    foreach (var v in mesh.Vertices)
                    {
                        uVertices.Add(new Vector3(v.X, v.Y, v.Z));
                    }
                }

                // Normals
                if (mesh.HasNormals)
                {
                    foreach (var n in mesh.Normals)
                    {
                        uNormals.Add(new Vector3(n.X, n.Y, n.Z));
                    }
                }

                // Triangles
                if (mesh.HasFaces)
                {
                    foreach (var f in mesh.Faces)
                    {
                        // Ignore degenerate faces
                        if (f.IndexCount == 1 || f.IndexCount == 2)
                            continue;

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
                    foreach (var uv in mesh.TextureCoordinateChannels[0])
                    {
                        uUv.Add(new Vector2(uv.X, uv.Y));
                    }
                }

                if (uVertices.Count > 65000) uMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                uMesh.name = mesh.Name;
                uMesh.vertices = uVertices.ToArray();
                uMesh.normals = uNormals.ToArray();
                uMesh.triangles = uIndices.ToArray();
                uMesh.uv = uUv.ToArray();

                meshes.Add(uMesh);
            }
        }

        private void processArmatures()
        {
            if (!processArmaturesLater.IsNullOrEmpty())
            {
                foreach (var g in processArmaturesLater)
                {
                    processArmature(g.Item2, g.Item3, g.Item1.name);
                }
            }
        }

        private UnityEngine.Matrix4x4 convertBindpose(Assimp.Matrix4x4 offsetMatrix)
        {
            Vector3D aPos;
            Vector3D aScl;
            Assimp.Quaternion aQ;
            offsetMatrix.Decompose(out aScl, out aQ, out aPos);
            Vector3 pos = new Vector3(aPos.X, aPos.Y, aPos.Z);
            UnityEngine.Quaternion q = new UnityEngine.Quaternion(aQ.X, aQ.Y, aQ.Z, aQ.W);
            Vector3 s = new Vector3(aScl.X, aScl.Y, aScl.Z);

            UnityEngine.Matrix4x4 bindPose = UnityEngine.Matrix4x4.TRS(pos, q, s);
            return bindPose;
        }

        private void processArmature(Assimp.Mesh mesh, SkinnedMeshRenderer renderer, string name)
        {
            Logger.LogDebug($"Processing Armature on Mesh: {name}");
            Mesh uMesh = renderer.sharedMesh;
            // helper Dict<vertexIndex, List<Tuple<boneIndex, weight>>>
            Dictionary<int, List<Tuple<int, float>>> helper = new Dictionary<int, List<Tuple<int, float>>>();
            UnityEngine.Matrix4x4[] bindposes = new UnityEngine.Matrix4x4[mesh.BoneCount];
            List<Transform> rendBones = new List<Transform>();

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
                bindposes[i] = convertBindpose(bone.OffsetMatrix);

                // bone
                Transform uBone = gameObject.transform.FindLoop(bone.Name).transform;
                if (!bones.Contains(uBone)) bones.Add(uBone);
                rendBones.Add(uBone);
            }

            // fill bones on renderer
            renderer.bones = rendBones.ToArray();

            // fill bindposes on mesh
            uMesh.bindposes = bindposes;

            // normalize vertex weights if necessary 
            foreach (int vertexID in helper.Keys)
            {
                float totalWeight = 0;
                foreach (Tuple<int, float> tu in helper[vertexID])
                {
                    totalWeight += tu.Item2;
                }
                if (totalWeight > 1f)
                {
                    for (int i = 0; i < helper[vertexID].Count; i++)
                    {
                        float newWeight = helper[vertexID][i].Item2 / totalWeight;
                        helper[vertexID][i] = new Tuple<int, float>(helper[vertexID][i].Item1, newWeight);
                    }
                }
            }

            byte[] bonesPerVertex = new byte[mesh.VertexCount];
            List<BoneWeight1> weights = new List<BoneWeight1>();

            // create unity boneWeights
            for (int i = 0; i < mesh.VertexCount; i++) // for vertex in mesh
            {
                List<BoneWeight1> lweights = new List<BoneWeight1>();
                if (helper.ContainsKey(i))
                {
                    bonesPerVertex[i] = (byte)helper[i].Count;
                    foreach (Tuple<int, float> wt in helper[i]) // for boneWeight of vertex
                    {
                        BoneWeight1 w = new BoneWeight1();
                        w.boneIndex = wt.Item1;
                        w.weight = wt.Item2;

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
                    BoneWeight1 w = new BoneWeight1();
                    w.boneIndex = 0;
                    w.weight = 0;
                    lweights.Add(w);
                }
                weights.AddRange(lweights);
            }

            uMesh.SetBoneWeights(
                new NativeArray<byte>(bonesPerVertex, Allocator.Persistent),
                new NativeArray<BoneWeight1>(weights.ToArray(), Allocator.Persistent)
            );
        }

        private void buildBoneNodeTree(GameObject go, int depth, BoneNode parent)
        {
            if (bones.Contains(go.transform))
            {
                parent = new BoneNode(go, parent, depth);
                boneNodes.Add(parent);
                depth++;
            }

            if (go.transform.childCount > 0)
            {
                for (int i = 0; i < go.transform.childCount; i++)
                {
                    buildBoneNodeTree(go.transform.GetChild(i).gameObject, depth, parent);
                }
            }
        }
	}
}