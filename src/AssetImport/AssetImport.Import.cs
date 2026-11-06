using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Assimp;
using BepInEx.Logging;
using UnityEngine;
#if !KK
using Unity.Collections;
#endif
using Material = UnityEngine.Material;
using Mesh = UnityEngine.Mesh;
using IllusionUtility.GetUtility;
#if KK
using Matrix4x4 = Assimp.Matrix4x4;
using AssimpVector = Assimp.Vector3D;
#else
using Matrix4x4 = System.Numerics.Matrix4x4;
using AssimpVector = System.Numerics.Vector3;
#endif
using BoneInfluence = AssetImport.LegacyMeshGeometry.BoneInfluence;

// ReSharper disable RedundantNameQualifier

namespace AssetImport
{
    /// <summary>
    /// Representation of an imported object in Unity.
    /// Also handles importing using AssimpNet.
    /// </summary>
	public partial class Import
	{
		private string _cPath;
		private readonly Material _bMat;
		private readonly List<TexturePath> _tPaths;
		private static readonly ManualLogSource Logger = AssetImport.Logger;
		private Assimp.Scene _scene;

        private readonly List<Material> _materials;
        private sealed class ConvertedMesh
        {
            internal Mesh Mesh;
            // Null means that Unity and Assimp use the same vertex ordering.
            internal int[] SourceVertices;
        }

        private sealed class PendingArmature
        {
            internal Assimp.Mesh Source;
            internal SkinnedMeshRenderer Renderer;
            internal int[] SourceVertices;
        }

        private readonly List<List<ConvertedMesh>> _meshes;
        private readonly List<PendingArmature> _processArmaturesLater = new List<PendingArmature>();

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
        internal string LoadError { get; private set; }
        internal bool IsPmx { get; private set; }
        private PmxModel _pmx;

		public readonly bool ImportBones;
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

        public Import(string identifierHash, bool importArmature = true, Material baseMat = null, bool perRendererMaterials = false, bool loadBlendshapes = true)
		{
			ImportBones = importArmature;
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
            _meshes = new List<List<ConvertedMesh>>();
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

        internal void ResolveTextureFiles(string modelPath)
        {
            // Only fresh disk imports call this. Card/scene textures are restored
            // by MaterialEditor and must not depend on the original author's files.
            if (PerRendererMaterials)
                Logger.LogInfo("Material per Renderer is enabled: source materials/textures are not imported. Disable it to use the model's original materials.");
            foreach (TexturePath texture in _tPaths)
            {
                texture.Path = TextureFileResolver.Resolve(texture.Path, modelPath);
                if (texture.Type != TextureType.Diffuse && texture.Type != TextureType.Normals) continue;
                if (texture.PathOkay())
                {
                    if (AssetDebug.Enabled) Logger.LogInfo($"Texture ready [{texture.Material.name}/{texture.Type}]: {texture.Path}");
                }
                else Logger.LogWarning($"Texture unavailable [{texture.Material.name}/{texture.Type}]: {texture.Path}. Select a PNG/JPG/JPEG/TGA/BMP/DDS file in the preload window.");
            }
            _cPath = null;
        }

		public void Load()
		{
            LoadError = null;
            if (AssetDebug.Enabled) Logger.LogInfo($"Loading of {RamCacheUtility.GetFileName(SourceIdentifier)} started");

            List<string> extraFiles = RamCacheUtility.GetFileAdditionalFileHashes(SourceIdentifier);
            string temp = Path.GetTempPath();
            string folder = "AssetImport_" + Guid.NewGuid().ToString("N");
            IsPmx = string.Equals(Path.GetExtension(SourceFileName), ".pmx", StringComparison.OrdinalIgnoreCase);
            if (IsPmx)
            {
                try
                {
                    _pmx = PmxReader.Read(RamCacheUtility.GetFileBlob(SourceIdentifier));
                    _scene = PmxSceneBuilder.Build(_pmx, LoadBlendshapes);
                    if (_pmx.MorphWarnings.Count > 0)
                        Logger.LogWarning("PMX optional morphs omitted (" + _pmx.MorphWarnings.Count +
                            "; first 16): " + string.Join("; ", _pmx.MorphWarnings.Take(16).ToArray()));
                    if (_pmx.UnusedNormalsRepaired > 0)
                        Logger.LogWarning($"PMX unused normals repaired: {_pmx.UnusedNormalsRepaired} invalid normals on vertices not referenced by any triangle. Rendered geometry and valid source normals are unchanged.");
                    if (_pmx.InvalidRigidBodyPositions > 0)
                        Logger.LogWarning($"PMX has {_pmx.InvalidRigidBodyPositions} rigid bodies with non-finite positions. Invalid collision obstacles are skipped; mesh and animation data are preserved.");
                    if (_pmx.InvalidIgnoredMetadataFields > 0)
                        Logger.LogWarning($"PMX ignored {_pmx.InvalidIgnoredMetadataFields} non-finite fields unused by this importer (first {_pmx.IgnoredMetadataWarnings.Count}): {string.Join("; ", _pmx.IgnoredMetadataWarnings.ToArray())}. Source bytes, geometry and skin weights are unchanged.");
                    Logger.LogInfo($"PMX 2.0: {_pmx.Vertices.Length} vertices, {_pmx.Bones.Length} bones, {_pmx.Materials.Length} materials; unit scale {PmxSceneBuilder.UnitScale}.");
                    Logger.LogWarning($"PMX preview: {_pmx.SdefVertices} SDEF vertices use linear skinning. IK ({_pmx.IkBones}) and append transforms ({_pmx.GrantBones}) require an MMDD mapping; rigid bodies ({_pmx.RigidBodies}) and joints ({_pmx.Joints}) are not simulated. Toon/sphere and bone/UV/material morphs are not imported.");
                }
                catch (Exception e)
                {
                    LoadError = "PMX import failed: " + e.Message;
                    Logger.LogError(LoadError);
                    return;
                }
            }
            else
            {
                AssimpContext imp = new AssimpContext();
                imp.SetConfig(new Assimp.Configs.RemoveEmptyBonesConfig(false));

                if (!extraFiles.IsNullOrEmpty())
                {
                    try
                    {
                        Logger.LogInfo($"File has additional files. Because of a limitation of the assimp wrapper the files have to be written to disk in order to be loaded!");
                        Directory.CreateDirectory(Path.Combine(temp, folder));
                        File.WriteAllBytes(Path.Combine(Path.Combine(temp, folder), RamCacheUtility.GetFileName(SourceIdentifier)), RamCacheUtility.GetFileBlob(SourceIdentifier));
                        extraFiles.ForEach(file => File.WriteAllBytes(Path.Combine(Path.Combine(temp, folder), RamCacheUtility.GetFileName(file)), RamCacheUtility.GetFileBlob(file)));

                        // load asset from file on disk to be able to load extra files.
                        _scene = imp.ImportFile(Path.Combine(Path.Combine(temp, folder), RamCacheUtility.GetFileName(SourceIdentifier)),
                            PostProcessSteps.MakeLeftHanded | PostProcessSteps.Triangulate);
                    }
                    catch(IOException e)
                    {
                        LoadError = $"I/O error when trying to write the file: {e.Message}";
                        Logger.LogError(LoadError);
                        return;
                    }
                }
                else
                {
                    // load asset from stream
                    string filename = RamCacheUtility.GetFileName(SourceIdentifier).ToLower();
                    string fileHint = filename.Substring(filename.LastIndexOf(".") + 1);
                    _scene = imp.ImportFileFromStream(RamCacheUtility.GetFileStream(SourceIdentifier),
                        PostProcessSteps.MakeLeftHanded | PostProcessSteps.Triangulate,
                        fileHint);
                }

            }

            if (_scene == null)
			{
                LoadError = "Assimp Import failed, aborting load process";
                Logger.LogError(LoadError);
				return;
			}

            if (!PerRendererMaterials)
                ProcessMaterials(); // convert materials
            ProcessMeshes(); // convert meshes
			GameObject = BuildFromNode(_scene.RootNode); // convert SceneStructure & assign meshes and materials
            ProcessArmatures(); // convert armature for meshes with bones
            if (IsPmx && ImportBones)
                for (int i = 0; i < _pmx.Bones.Length; i++)
                {
                    Transform bone = GameObject.transform.FindLoop(PmxModel.BoneName(i, _pmx.Bones[i].Name)).transform;
                    if (!Bones.Contains(bone)) Bones.Add(bone);
                }
            BuildBoneNodeTree(GameObject, 0, null);
            IsLoaded = true;

            if (extraFiles.IsNullOrEmpty()) return;
            {
                try
                {
                    File.Delete(Path.Combine(Path.Combine(temp, folder), RamCacheUtility.GetFileName(SourceIdentifier)));
                    extraFiles.ForEach(file => File.Delete(Path.Combine(Path.Combine(temp, folder), RamCacheUtility.GetFileName(file))));
                }
                catch (Exception e)
                {
                    Logger.LogError($"Cleanup failed: {e.Message}");
                }
            }
        }


        private static void RestartStopwatch()
        {
            Stopwatch.Reset();
            Stopwatch.Start();
        }

        // Preserve the source matrix's rows/columns when moving to Unity.
        private static UnityEngine.Matrix4x4 ToUnityMatrix(Matrix4x4 m) {
            UnityEngine.Matrix4x4 matrix = new UnityEngine.Matrix4x4();
#if KK
            matrix.SetColumn(0, new Vector4(m.A1, m.B1, m.C1, m.D1));
            matrix.SetColumn(1, new Vector4(m.A2, m.B2, m.C2, m.D2));
            matrix.SetColumn(2, new Vector4(m.A3, m.B3, m.C3, m.D3));
            matrix.SetColumn(3, new Vector4(m.A4, m.B4, m.C4, m.D4));
#else
            matrix.SetColumn(0, new Vector4(m.M11, m.M21, m.M31, m.M41));
            matrix.SetColumn(1, new Vector4(m.M12, m.M22, m.M32, m.M42));
            matrix.SetColumn(2, new Vector4(m.M13, m.M23, m.M33, m.M43));
            matrix.SetColumn(3, new Vector4(m.M14, m.M24, m.M34, m.M44));
#endif
            return matrix;
        }


		private static UnityEngine.Matrix4x4 ConvertTransform(Matrix4x4 aTransform, Transform uTransform)
		{
            UnityEngine.Matrix4x4 uMatrix = ToUnityMatrix(aTransform);

#if KK
            // Unity 5.6 has no Matrix4x4.rotation. Keep this decomposition in a
            // pure managed helper so reflected/non-uniform/zero scales can be tested.
            var basis = LegacyMeshGeometry.DecomposeBasis(uMatrix.m00, uMatrix.m01, uMatrix.m02,
                uMatrix.m10, uMatrix.m11, uMatrix.m12, uMatrix.m20, uMatrix.m21, uMatrix.m22);
            uTransform.localScale = new Vector3((float)basis.Scale.X, (float)basis.Scale.Y, (float)basis.Scale.Z);
            uTransform.localRotation = UnityEngine.Quaternion.LookRotation(
                new Vector3((float)basis.Forward.X, (float)basis.Forward.Y, (float)basis.Forward.Z),
                new Vector3((float)basis.Up.X, (float)basis.Up.Y, (float)basis.Up.Z));
            if (basis.HasShear)
                Logger.LogWarning($"Node {uTransform.name} contains local shear, which a Unity Transform cannot preserve; using a TRS approximation. Bake shear into the source mesh for an exact import.");
#else
            uTransform.localScale = new Vector3(
                uMatrix.GetColumn(0).magnitude,
                uMatrix.GetColumn(1).magnitude,
                uMatrix.GetColumn(2).magnitude * (uMatrix.determinant < 0 ? -1f : 1f));
            uTransform.localRotation = uMatrix.rotation;
#endif
            uTransform.localPosition = uMatrix.GetColumn(3);

            return uMatrix;
        }


		private void ProcessMaterials()
		{
            if (AssetDebug.Enabled) Logger.LogInfo("Processing Materials");
			foreach(Assimp.Material material in _scene.Materials)
			{
                if (AssetDebug.Enabled) Logger.LogInfo($"Processing Material: {material.Name}");
				Material uMaterial = new Material(_bMat)
                {
                    name = material.Name
                };
                if (IsPmx && material.IsTwoSided && uMaterial.HasProperty("_Cull"))
                    uMaterial.SetInt("_Cull", 0);
                // Albedo
                if (material.HasColorDiffuse)
                {
                    Color color = new Color(
#if KK
                        material.ColorDiffuse.R,
                        material.ColorDiffuse.G,
                        material.ColorDiffuse.B,
                        material.ColorDiffuse.A
#else
                        material.ColorDiffuse.X,
                        material.ColorDiffuse.Y,
                        material.ColorDiffuse.Z,
                        material.ColorDiffuse.W
#endif
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
            if (AssetDebug.Enabled) Logger.LogInfo("Processing Meshes");
            if (!_scene.HasMeshes) return;

            foreach(Assimp.Mesh mesh in _scene.Meshes)
            {
                if (AssetDebug.Enabled) Logger.LogInfo($"Converting Mesh: {mesh.Name}");
                var uVertices = new List<Vector3>();
                var uNormals = new List<Vector3>();
                var uTangents = new List<Vector4>();
                var uUv = new List<Vector2>();
                var uIndices = new List<int>();

                // Vertices
                if (mesh.HasVertices)
                {
                    RestartStopwatch();
                    uVertices.AddRange(mesh.Vertices.Select(v => new Vector3(v.X, v.Y, v.Z)));
                    Stopwatch.Stop();
                    if (AssetDebug.Enabled) Logger.LogInfo($"{mesh.VertexCount} Vertices Converted in {Stopwatch.Elapsed.TotalMilliseconds} ms");
                }

                // Normals
                if (mesh.HasNormals)
                {
                    RestartStopwatch();
                    uNormals.AddRange(mesh.Normals.Select(n => new Vector3(n.X, n.Y, n.Z)));
                    Stopwatch.Stop();
                    if (AssetDebug.Enabled) Logger.LogInfo($"{mesh.Normals.Count} Normals Converted in {Stopwatch.Elapsed.TotalMilliseconds} ms");
                }

                // Triangles
                if (mesh.HasFaces)
                {
                    RestartStopwatch();
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
                    if (AssetDebug.Enabled) Logger.LogInfo($"{mesh.FaceCount} Faces Converted in {Stopwatch.Elapsed.TotalMilliseconds} ms");
                }

                // Uv (texture coordinate)
                if (mesh.HasTextureCoords(0))
                {
                    RestartStopwatch();
                    uUv.AddRange(mesh.TextureCoordinateChannels[0].Select(uv => new Vector2(uv.X, uv.Y)));
                    Stopwatch.Stop();
                    if (AssetDebug.Enabled) Logger.LogInfo($"UV Converted in {Stopwatch.Elapsed.TotalMilliseconds} ms");
                }

                // Tangents
                if (mesh.HasTangentBasis)
                {
                    RestartStopwatch();
                    for (int i = 0; i < mesh.Tangents.Count; i++)
                    {
                        Vector3 tangent = new Vector3(mesh.Tangents[i].X, mesh.Tangents[i].Y, mesh.Tangents[i].Z);
                        Vector3 bitangent = new Vector3(mesh.BiTangents[i].X, mesh.BiTangents[i].Y, mesh.BiTangents[i].Z);
                        Vector3 normal = uNormals[i];
                        // handedness sign: reconstruct which way the bitangent points relative to N x T
                        float w = Vector3.Dot(Vector3.Cross(normal, tangent), bitangent) < 0f ? -1f : 1f;
                        uTangents.Add(new Vector4(tangent.x, tangent.y, tangent.z, w));
                    }
                    Stopwatch.Stop();
                    if (AssetDebug.Enabled) Logger.LogInfo($"{mesh.Tangents.Count} Tangents Converted in {Stopwatch.Elapsed.TotalMilliseconds} ms");
                }

#if KK
                List<LegacyMeshGeometry.MeshPart> parts = LegacyMeshGeometry.Partition(uVertices.Count, uIndices);
                var convertedMeshes = new List<ConvertedMesh>();
                for (int partIndex = 0; partIndex < parts.Count; partIndex++)
                {
                    LegacyMeshGeometry.MeshPart part = parts[partIndex];
                    var uMesh = new Mesh();
                    uMesh.name = parts.Count == 1 ? mesh.Name : mesh.Name + "_part" + partIndex;
                    uMesh.vertices = LegacyMeshGeometry.Remap(uVertices, part.SourceVertices);
                    if (uNormals.Count > 0) uMesh.normals = LegacyMeshGeometry.Remap(uNormals, part.SourceVertices);
                    uMesh.triangles = part.Triangles;
                    if (uUv.Count > 0) uMesh.uv = LegacyMeshGeometry.Remap(uUv, part.SourceVertices);
                    if (uTangents.Count > 0) uMesh.tangents = LegacyMeshGeometry.Remap(uTangents, part.SourceVertices);
                    // Preserve the additional UV channels and vertex colors supported by Unity 5.6.
                    if (mesh.HasTextureCoords(1)) uMesh.uv2 = RemapUv(mesh.TextureCoordinateChannels[1], part.SourceVertices);
                    if (mesh.HasTextureCoords(2)) uMesh.uv3 = RemapUv(mesh.TextureCoordinateChannels[2], part.SourceVertices);
                    if (mesh.HasTextureCoords(3)) uMesh.uv4 = RemapUv(mesh.TextureCoordinateChannels[3], part.SourceVertices);
                    if (mesh.HasVertexColors(0))
                        uMesh.colors = LegacyMeshGeometry.Remap(mesh.VertexColorChannels[0], part.SourceVertices)
                            .Select(c => new Color(c.R, c.G, c.B, c.A)).ToArray();
                    if (mesh.HasMeshAnimationAttachments && LoadBlendshapes)
                        ProcessBlendshapesCpu(mesh, uMesh, part.SourceVertices);
                    convertedMeshes.Add(new ConvertedMesh { Mesh = uMesh, SourceVertices = part.SourceVertices });
                }
                if (parts.Count > 1)
                    Logger.LogInfo($"Split {mesh.Name} ({mesh.VertexCount} vertices) into {parts.Count} meshes for Unity 5.6.");
                _meshes.Add(convertedMeshes);
#else
                Mesh uMesh = new Mesh();
                if (uVertices.Count > 65000) uMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                uMesh.name = mesh.Name;
                uMesh.vertices = uVertices.ToArray();
                uMesh.normals = uNormals.ToArray();
                uMesh.triangles = uIndices.ToArray();
                uMesh.uv = uUv.ToArray();
                uMesh.tangents = uTangents.ToArray();

                if (mesh.HasMeshAnimationAttachments && LoadBlendshapes)
                {
                    if (IsPmx) ProcessBlendshapesCpu(mesh, uMesh, Enumerable.Range(0, mesh.VertexCount).ToArray());
                    else ProcessBlendshapes(mesh, uMesh);
                }

                _meshes.Add(new List<ConvertedMesh> { new ConvertedMesh { Mesh = uMesh, SourceVertices = null } });
#endif
            }
        }

#if KK
        private static Vector2[] RemapUv(IList<Assimp.Vector3D> source, int[] sourceVertices)
        {
            return LegacyMeshGeometry.Remap(source, sourceVertices).Select(v => new Vector2(v.X, v.Y)).ToArray();
        }

#endif

        private static Vector3[] MorphDeltas(IList<AssimpVector> basis, IList<AssimpVector> target,
            int vertexCount, int[] sourceVertices)
        {
            return LegacyMeshGeometry.RemapDeltas(basis, target, vertexCount, sourceVertices,
                (value, original) => new Vector3(value.X - original.X, value.Y - original.Y, value.Z - original.Z));
        }

        private static void ProcessBlendshapesCpu(Assimp.Mesh sourceMesh, Mesh targetMesh, int[] sourceVertices)
        {
            var usedNames = new HashSet<string>();
            for (int i = 0; i < sourceMesh.MeshAnimationAttachmentCount; i++)
            {
                MeshAnimationAttachment attachment = sourceMesh.MeshAnimationAttachments[i];
                string shapeName = string.IsNullOrEmpty(attachment.Name) ? "BlendShape_" + i : attachment.Name;
                string baseName = shapeName;
                int duplicate = 1;
                while (!usedNames.Add(shapeName)) shapeName = baseName + "_" + duplicate++;
                // Some importers report the default (zero) morph influence here. A
                // Unity frame represents the full target and requires a positive weight.
                float frameWeight = (float)attachment.Weight * 100f;
                if (frameWeight <= 0 || float.IsNaN(frameWeight) || float.IsInfinity(frameWeight)) frameWeight = 100f;
                targetMesh.AddBlendShapeFrame(shapeName, frameWeight,
                    MorphDeltas(sourceMesh.Vertices, attachment.Vertices, sourceMesh.VertexCount, sourceVertices),
                    MorphDeltas(sourceMesh.Normals, attachment.Normals, sourceMesh.VertexCount, sourceVertices),
                    MorphDeltas(sourceMesh.Tangents, attachment.Tangents, sourceMesh.VertexCount, sourceVertices));
            }
        }
#if !KK
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

                RestartStopwatch();

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
                if (AssetDebug.Enabled) Logger.LogInfo($"  >>> Blendshape {index+1}/{sourceMesh.MeshAnimationAttachmentCount} Converted in {Stopwatch.Elapsed.TotalMilliseconds} ms");

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

            if (AssetDebug.Enabled) Logger.LogInfo($"Blendshape processing completed in {total} ms");
        }

#endif

        private void ProcessArmatures()
        {
            if (_processArmaturesLater.IsNullOrEmpty()) return;
            foreach (PendingArmature pending in _processArmaturesLater)
            {
                ProcessArmature(pending.Source, pending.Renderer, pending.Renderer.name, pending.SourceVertices);
            }
        }

        private static UnityEngine.Matrix4x4 ConvertBindpose(Matrix4x4 offsetMatrix)
        {
            UnityEngine.Matrix4x4 m = ToUnityMatrix(offsetMatrix);
            /*
            Vector4 c0 = m.GetColumn(0);
            Vector4 c1 = m.GetColumn(1);
            Vector4 c2 = m.GetColumn(2);
            float s0 = c0.magnitude, s1 = c1.magnitude, s2 = c2.magnitude;
            // Threshold to avoid stripping scale from columns that are essentially unit length (floating point noise).
            bool passedThreshold = Mathf.Abs(s0 - 1f) > 0.001f || Mathf.Abs(s1 - 1f) > 0.001f || Mathf.Abs(s2 - 1f) > 0.001f;
            if (passedThreshold)
            {
                Vector4 translation = m.GetColumn(3);
                m = UnityEngine.Matrix4x4.TRS(translation, UnityEngine.Quaternion.identity, Vector3.one);
                scaleStripped = true;
            }
            */
            return m;
        }

        private void ProcessArmature(Assimp.Mesh mesh, SkinnedMeshRenderer renderer, string name, int[] sourceVertices)
        {
            if (AssetDebug.Enabled) Logger.LogInfo($"Processing Armature on Mesh: {name}");
            Mesh uMesh = renderer.sharedMesh;
            var helper = new Dictionary<int, List<BoneInfluence>>();
            var bindposes = new UnityEngine.Matrix4x4[mesh.BoneCount];
            var rendBones = new List<Transform>();

            for (int i = 0; i < mesh.BoneCount; i++) // for bone in mesh
            {
                // weights - fill helper
                Bone bone = mesh.Bones[i];
                foreach (VertexWeight vWeight in bone.VertexWeights)
                {
                    if (!helper.ContainsKey(vWeight.VertexID))
                        helper[vWeight.VertexID] = new List<BoneInfluence>();
                    helper[vWeight.VertexID].Add(new BoneInfluence(i, vWeight.Weight));
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

#if KK
            var unityWeights = new BoneWeight[sourceVertices.Length];
            int reducedVertexCount = 0;
            int unweightedVertexCount = 0;
            for (int vertex = 0; vertex < sourceVertices.Length; vertex++)
            {
                List<BoneInfluence> sourceWeights;
                if (!helper.TryGetValue(sourceVertices[vertex], out sourceWeights))
                {
                    unweightedVertexCount++;
                    continue;
                }
                BoneInfluence[] limited = LegacyMeshGeometry.LimitBoneInfluences(sourceWeights);
                if (sourceWeights.Count > 4) reducedVertexCount++;
                if (limited.Length == 0) unweightedVertexCount++;
                var weight = new BoneWeight();
                if (limited.Length > 0) { weight.boneIndex0 = limited[0].BoneIndex; weight.weight0 = limited[0].Weight; }
                if (limited.Length > 1) { weight.boneIndex1 = limited[1].BoneIndex; weight.weight1 = limited[1].Weight; }
                if (limited.Length > 2) { weight.boneIndex2 = limited[2].BoneIndex; weight.weight2 = limited[2].Weight; }
                if (limited.Length > 3) { weight.boneIndex3 = limited[3].BoneIndex; weight.weight3 = limited[3].Weight; }
                unityWeights[vertex] = weight;
            }
            uMesh.boneWeights = unityWeights;
            renderer.quality = SkinQuality.Bone4;
            if (reducedVertexCount > 0)
                Logger.LogWarning($"{name}: kept and normalized the strongest 4 bone weights on {reducedVertexCount} vertices (Unity 5.6 limit).");
            if (unweightedVertexCount > 0)
                Logger.LogWarning($"{name}: source contains {unweightedVertexCount} vertices without bone weights.");
#else
            // normalize vertex weights if necessary
            foreach (int vertexID in helper.Keys)
            {
                float totalWeight = helper[vertexID].Sum(tu => tu.Weight);
                if (!(totalWeight > 1f)) continue;
                for (int i = 0; i < helper[vertexID].Count; i++)
                {
                    float newWeight = helper[vertexID][i].Weight / totalWeight;
                    helper[vertexID][i] = new BoneInfluence(helper[vertexID][i].BoneIndex, newWeight);
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
                                 boneIndex = wt.BoneIndex,
                                 weight = wt.Weight
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
#endif
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
