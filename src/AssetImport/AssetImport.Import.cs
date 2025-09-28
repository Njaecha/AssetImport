using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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
		private string _cPath;
		private readonly Material _bMat;
		private readonly List<TexturePath> _tPaths;
		private static readonly ManualLogSource Logger = AssetImport.Logger;
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

		public readonly bool ImportBones;
        public readonly bool DoFbxTranslation;
        public readonly bool PerRendererMaterials;
        public readonly bool LoadBlendshapes;
        
        private static readonly Stopwatch Stopwatch = new Stopwatch();
        private static readonly int MeshAPositions = Shader.PropertyToID("meshA_Positions");
        private static readonly int MeshBPositions = Shader.PropertyToID("meshB_Positions");
        private static readonly int DeltaPositions = Shader.PropertyToID("delta_Positions");
        private static readonly int MeshANormals = Shader.PropertyToID("meshA_Normals");
        private static readonly int MeshBNormals = Shader.PropertyToID("meshB_Normals");
        private static readonly int DeltaNormals = Shader.PropertyToID("delta_Normals");
        private static readonly int MeshATangents = Shader.PropertyToID("meshA_Tangents");
        private static readonly int MeshBTangents = Shader.PropertyToID("meshB_Tangents");
        private static readonly int DeltaTangents = Shader.PropertyToID("delta_Tangents");

        public Import(string identifierHash, bool importArmature = true, Material baseMat = null, bool doFbxTranslation = true, bool perRendererMaterials = false, bool loadBlendshapes = true)
		{
			ImportBones = importArmature;
            DoFbxTranslation = doFbxTranslation;
            PerRendererMaterials = perRendererMaterials;
            LoadBlendshapes = loadBlendshapes;
            
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

        public void ReplacePathInAllTextures(string newPath)
        {
            if (!HasTextures) return;
            newPath = newPath.Replace("\\", "/");
            if (!newPath.EndsWith("/")) newPath += "/";
            foreach (TexturePath p in _tPaths)
            {
                p.Path = newPath+p.File;
            }
        }

		public void Load()
		{
            Logger.LogDebug($"Loading of {RamCacheUtility.GetFileName(SourceIdentifier)} started");
			
            AssimpContext imp = new AssimpContext();
            imp.SetConfig(new Assimp.Configs.RemoveEmptyBonesConfig(false));
            
            List<string> extraFiles = RamCacheUtility.GetFileAdditionalFileHashes(SourceIdentifier);
            string temp = Path.GetTempPath();
            string folder = $"AssetImport_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            
            if (!extraFiles.IsNullOrEmpty())
            {
                try
                {
                    Logger.LogInfo($"File has additional files. Because of a limitation of the assimp wrapper the files have to be written to disk in order to be loaded!");
                    Directory.CreateDirectory(Path.Combine(temp, folder));
                    File.WriteAllBytes(Path.Combine(temp, folder, RamCacheUtility.GetFileName(SourceIdentifier)), RamCacheUtility.GetFileBlob(SourceIdentifier));
                    extraFiles.ForEach(file => File.WriteAllBytes(Path.Combine(temp, folder, RamCacheUtility.GetFileName(file)), RamCacheUtility.GetFileBlob(file)));

                    // load asset from file on disk to be able to load extra files.
                    _scene = imp.ImportFile(Path.Combine(temp, folder, RamCacheUtility.GetFileName(SourceIdentifier)), PostProcessSteps.MakeLeftHanded | PostProcessSteps.Triangulate);
                }
                catch(IOException e)
                {
                    Logger.LogError($"I/O error when trying to write the file: {e.Message}");
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
				Logger.LogError("Assimp Import failed, aborting load process");
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
                    Logger.LogError($"Cleanup failed: {e.Message}");
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

					if (mesh.HasBones && ImportBones)
					{
                        rend = subObjet.AddComponent<SkinnedMeshRenderer>();
                        ((SkinnedMeshRenderer)rend).sharedMesh = uMesh;

                        _processArmaturesLater.Add(new Tuple<GameObject, Assimp.Mesh, SkinnedMeshRenderer>(subObjet, mesh, (SkinnedMeshRenderer)rend));
					}
                    else if (mesh.HasMeshAnimationAttachments) // mesh doesn't have bones but has Blendshapes.
                    {
                        rend = subObjet.AddComponent<SkinnedMeshRenderer>();
                        ((SkinnedMeshRenderer)rend).sharedMesh = uMesh;
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
            Logger.LogDebug("Processing Materials");
			foreach(Assimp.Material material in _scene.Materials)
			{
                Logger.LogDebug($"Processing Material: {material.Name}");
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
            Logger.LogDebug("Processing Meshes");
            if (!_scene.HasMeshes) return;

            foreach(Assimp.Mesh mesh in _scene.Meshes)
            {
                Logger.LogDebug($"Converting Mesh: {mesh.Name}");
                Mesh uMesh = new Mesh();
                var uVertices = new List<Vector3>();
                var uNormals = new List<Vector3>();
                var uUv = new List<Vector2>();
                var uIndices = new List<int>();

                // Vertices
                if (mesh.HasVertices)
                {
                    Stopwatch.Restart();
                    uVertices.AddRange(mesh.Vertices.Select(v => new Vector3(v.X, v.Y, v.Z)));
                    Stopwatch.Stop();
                    Logger.LogDebug($"{mesh.VertexCount} Vertices Converted in {Stopwatch.Elapsed.TotalMilliseconds} ms");
                }

                // Normals
                if (mesh.HasNormals)
                {
                    Stopwatch.Restart();
                    uNormals.AddRange(mesh.Normals.Select(n => new Vector3(n.X, n.Y, n.Z)));
                    Stopwatch.Stop();
                    Logger.LogDebug($"{mesh.Normals.Count} Normals Converted in {Stopwatch.Elapsed.TotalMilliseconds} ms");
                }

                // Triangles
                if (mesh.HasFaces)
                {
                    Stopwatch.Restart();
                    foreach (Face f in mesh.Faces.Where(f => f.IndexCount != 1 && f.IndexCount != 2))
                    {
                        for (int i = 0; i < (f.IndexCount - 2); i++)
                        {
                            uIndices.Add(f.Indices[i + 2]);
                            uIndices.Add(f.Indices[i + 1]);
                            uIndices.Add(f.Indices[0]);
                        }
                    }
                    Stopwatch.Stop();
                    Logger.LogDebug($"{mesh.FaceCount} Faces Converted in {Stopwatch.Elapsed.TotalMilliseconds} ms");
                }

                // Uv (texture coordinate) 
                if (mesh.HasTextureCoords(0))
                {
                    Stopwatch.Restart();
                    uUv.AddRange(mesh.TextureCoordinateChannels[0].Select(uv => new Vector2(uv.X, uv.Y)));
                    Stopwatch.Stop();
                    Logger.LogDebug($"UV Converted in {Stopwatch.Elapsed.TotalMilliseconds} ms");
                }

                if (uVertices.Count > 65000) uMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                uMesh.name = mesh.Name;
                uMesh.vertices = uVertices.ToArray();
                uMesh.normals = uNormals.ToArray();
                uMesh.triangles = uIndices.ToArray();
                uMesh.uv = uUv.ToArray();
                
                if (mesh.HasMeshAnimationAttachments && LoadBlendshapes)
                {
                    Logger.LogDebug("Converting Mesh Animation Attachments >>>");
                    ProcessBlendshapes(mesh, uMesh);
                }

                _meshes.Add(uMesh);
            }
        }

        [SuppressMessage("ReSharper", "InconsistentNaming")]
        private static void ProcessBlendshapes(Assimp.Mesh sourceMesh, Mesh targetMesh)
        {
            ComputeShader shader = AssetImport.vertexDeltaComputeShader;
            int vertexCount = sourceMesh.VertexCount;
            int threadGroups = Mathf.CeilToInt(vertexCount / 64f);

            bool AnyAll = sourceMesh.MeshAnimationAttachments.Any(m => m.Normals.Count > 0 && m.Tangents.Count > 0);
            bool AnyPosAndNorm = sourceMesh.MeshAnimationAttachments.Any(m => m.Normals.Count > 0 && m.Tangents.Count == 0);
            bool AnyPosAndTan = sourceMesh.MeshAnimationAttachments.Any(m => m.Normals.Count == 0 && m.Tangents.Count > 0);
            bool AnyPos = sourceMesh.MeshAnimationAttachments.Any(m => m.Normals.Count == 0 && m.Tangents.Count == 0);

            int kernelAll = shader.FindKernel("CSAll");
            int kernelPosAndNorm = shader.FindKernel("CSPosAndNorm");
            int kernelPosAndTan = shader.FindKernel("CSPosAndTan");
            int kernelPos = shader.FindKernel("CSPos");
            
            // set MeshA Position buffers
            ComputeBuffer meshA_Positions = new ComputeBuffer(vertexCount, sizeof(float) * 3);
            meshA_Positions.SetData(sourceMesh.Vertices.ToArray());
            ComputeBuffer meshA_Normals = new ComputeBuffer(vertexCount, sizeof(float) * 3);
            meshA_Normals.SetData(sourceMesh.Normals.ToArray());
            ComputeBuffer meshA_Tangents = new ComputeBuffer(vertexCount, sizeof(float) * 3);
            meshA_Tangents.SetData(sourceMesh.Tangents.ToArray());
            
            // Set MeshB buffers
            ComputeBuffer meshB_Positions = new ComputeBuffer(vertexCount, sizeof(float) * 3);
            ComputeBuffer meshB_Normals = new ComputeBuffer(vertexCount, sizeof(float) * 3);
            ComputeBuffer meshB_Tangents = new ComputeBuffer(vertexCount, sizeof(float) * 3);
            
            // Set Output buffers
            ComputeBuffer delta_Positions = new ComputeBuffer(vertexCount, sizeof(float) * 3);
            ComputeBuffer delta_Normals = new ComputeBuffer(vertexCount, sizeof(float) * 3);
            ComputeBuffer delta_Tangents = new ComputeBuffer(vertexCount, sizeof(float) * 3);

            if (AnyAll)
            {
                shader.SetBuffer(kernelAll, MeshAPositions, meshA_Positions);
                shader.SetBuffer(kernelAll, MeshANormals, meshA_Normals);
                shader.SetBuffer(kernelAll, MeshATangents, meshA_Tangents);
                shader.SetBuffer(kernelAll, MeshBPositions, meshB_Positions);
                shader.SetBuffer(kernelAll, MeshBNormals, meshB_Normals);
                shader.SetBuffer(kernelAll, MeshBTangents, meshB_Tangents);
                shader.SetBuffer(kernelAll, DeltaPositions, delta_Positions);
                shader.SetBuffer(kernelAll, DeltaNormals, delta_Normals);
                shader.SetBuffer(kernelAll, DeltaTangents, delta_Tangents);
            }
            if (AnyPosAndNorm)
            {
                shader.SetBuffer(kernelPosAndNorm, MeshAPositions, meshA_Positions);
                shader.SetBuffer(kernelPosAndNorm, MeshANormals, meshA_Normals);
                shader.SetBuffer(kernelPosAndNorm, MeshBPositions, meshB_Positions);
                shader.SetBuffer(kernelPosAndNorm, MeshBNormals, meshB_Normals);
                shader.SetBuffer(kernelPosAndNorm, DeltaPositions, delta_Positions);
                shader.SetBuffer(kernelPosAndNorm, DeltaNormals, delta_Normals);
            }
            if (AnyPosAndTan)
            {
                shader.SetBuffer(kernelPosAndTan, MeshAPositions, meshA_Positions);
                shader.SetBuffer(kernelPosAndTan, MeshATangents, meshA_Tangents);
                shader.SetBuffer(kernelPosAndTan, MeshBPositions, meshB_Positions);
                shader.SetBuffer(kernelPosAndTan, MeshBTangents, meshB_Tangents);
                shader.SetBuffer(kernelPosAndTan, DeltaPositions, delta_Positions);
                shader.SetBuffer(kernelPosAndTan, DeltaTangents, delta_Tangents);
            }
            if (AnyPos)
            {
                shader.SetBuffer(kernelPos, MeshAPositions, meshA_Positions);
                shader.SetBuffer(kernelPos, MeshBPositions, meshB_Positions);
                shader.SetBuffer(kernelPos, DeltaPositions, delta_Positions);
            }
            
            double total = 0;
            for(int index = 0; index < sourceMesh.MeshAnimationAttachmentCount; index++)
            {
                MeshAnimationAttachment meshAnimation = sourceMesh.MeshAnimationAttachments[index];
                
                Stopwatch.Restart();
                
                bool HasTangents = meshAnimation.Tangents.Count > 0;
                bool HasNormals = meshAnimation.Normals.Count > 0;
                
                var vertDeltas = new Vector3[vertexCount];
                var normalsDeltas = new Vector3[vertexCount];
                var tangentsDeltas = new Vector3[vertexCount];

                int kernelIndex = meshAnimation.HasNormals && HasTangents ? kernelAll : HasTangents ? kernelPosAndTan : meshAnimation.HasNormals ? kernelPosAndNorm : kernelPos;
                
                // Set Data
                // Positions
                meshB_Positions.SetData(meshAnimation.Vertices.ToArray());

                // Normals
                if (HasNormals) meshB_Normals.SetData(meshAnimation.Normals.ToArray());

                // Tangents
                if (HasTangents) meshB_Tangents.SetData(meshAnimation.Tangents.ToArray());
                
                // Dispatch
                shader.Dispatch(kernelIndex, threadGroups, 1, 1);
                
                delta_Positions.GetData(vertDeltas);
                if (HasNormals) delta_Normals.GetData(normalsDeltas);
                if (HasTangents) delta_Tangents.GetData(tangentsDeltas);
                
                targetMesh.AddBlendShapeFrame(meshAnimation.Name, meshAnimation.Weight * 100, vertDeltas, normalsDeltas, tangentsDeltas);
                
                Stopwatch.Stop();
                total += Stopwatch.Elapsed.TotalMilliseconds;
                Logger.LogDebug($"  >>> Blendshape {index+1}/{sourceMesh.MeshAnimationAttachmentCount} Converted in {Stopwatch.Elapsed.TotalMilliseconds} ms");
                
            }
            // Cleanup
            meshA_Positions.Release();
            meshA_Positions.Release();
            delta_Positions.Release();
            meshA_Normals.Release();
            meshB_Normals.Release();
            delta_Normals.Release();
            meshA_Tangents.Release();
            meshB_Tangents.Release();
            delta_Tangents.Release();
            
            Logger.LogDebug($"Blendshape processing completed in {total} ms");
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
            Logger.LogDebug($"Processing Armature on Mesh: {name}");
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