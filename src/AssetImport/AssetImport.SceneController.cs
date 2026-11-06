using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using IllusionUtility.GetUtility;
using BepInEx.Logging;
using Studio;
using KKAPI.Studio.SaveLoad;
using KKAPI.Utilities;
using ExtensibleSaveFormat;
using MessagePack;
using System.IO;
using System.Xml;
using HSPE;
using MaterialEditorAPI;
using Main = AssetImport.AssetImport;
using System.Runtime.CompilerServices;

namespace AssetImport
{
    /// <summary>
    /// SceneController for AssetImport.
    /// Handles save/load and all kinds of reload events for StudioItems.
    /// </summary>
    public partial class AssetSceneController : SceneCustomFunctionController
    {
        private static readonly Dictionary<int, Asset> LoadedObjects = new Dictionary<int, Asset>();

        private static readonly ManualLogSource Logger = Main.Logger;

        internal string SceneName = "";

        internal LocalizedText RepairSelectedPmxAuto()
        {
            var studio = Studio.Studio.Instance;
            var selected = studio.treeNodeCtrl.selectNodes;
            if (selected == null || selected.Length != 1)
                return L.Message("Select one imported PMX item in the Studio workspace, then repair again.");
            ObjectCtrlInfo selectedObject;
            if (!studio.dicInfo.TryGetValue(selected[0], out selectedObject) || !(selectedObject is OCIItem))
                return L.Message("Select the imported PMX ITEM itself in the Studio workspace.");
            var item = (OCIItem)selectedObject;
            Asset asset;
            if (!LoadedObjects.TryGetValue(item.itemInfo.dicKey, out asset) || !asset.HasBones
                || !string.Equals(Path.GetExtension(RamCacheUtility.GetFileName(asset.SourceFile)), ".pmx", StringComparison.OrdinalIgnoreCase))
                return L.Message("The selected item is not an AssetImport PMX with bones.");
            var roots = (item.dynamicBones ?? new DynamicBone[0])
                .Where(b => b && b.m_Root).Select(b => b.m_Root.name);
            return global::AssetImport.Import.RepairMmddMapping(asset.SourceFile,
                (item.arrayRender ?? new Renderer[0]).OfType<SkinnedMeshRenderer>(), roots);
        }

        private System.Collections.IEnumerator LogDynamicBoneState(OCIItem item, DynamicBone[] bones)
        {
            // Allow Unity Start and the first physics update to initialize particles.
            yield return null;
            yield return new WaitForEndOfFrame();
            if (!AssetDebug.Enabled || !item.objectItem) yield break;
            var particlesField = HarmonyLib.AccessTools.Field(typeof(DynamicBone), "m_Particles");
            foreach (DynamicBone bone in bones)
            {
                if (!bone) continue;
                var particles = particlesField?.GetValue(bone) as System.Collections.ICollection;
                string root = bone.m_Root ? bone.m_Root.name : "<none>";
                Logger.LogInfo($"DynamicBone state: root={root}; enabled={bone.enabled}; active={bone.gameObject.activeInHierarchy}; particles={particles?.Count.ToString() ?? "unknown"}; FK={item.isFK}; colliders={bone.m_Colliders?.Count ?? 0}");
                if (bone.enabled && bone.gameObject.activeInHierarchy && particles != null && particles.Count < 2)
                    Logger.LogWarning($"DynamicBone {root} has fewer than two particles; select a root with child bones. This chain cannot show normal hair motion.");
            }
            if (item.isFK)
                Logger.LogInfo("Item FK remains enabled. Use this model's PMX Auto mapping so VMD does not also drive the selected physics chains.");
        }

        public string GetDumpPath()
        {
            string c = SceneName.Replace(".png", "");
            if (!Directory.Exists($"./UserData/AssetImport/{c}"))
            {
                Directory.CreateDirectory($"./UserData/AssetImport/{c}");
            }
            return $"./UserData/AssetImport/{c}/";
        }

        protected override void OnSceneLoad(SceneOperationKind operation, ReadOnlyDictionary<int, ObjectCtrlInfo> loadedItems)
        {
            // on scene load is not handeled here, because I hook Material Editor to guarantee loading before ME
        }

        protected override void OnSceneSave()
        {
            if (!(LoadedObjects.Count > 0))
            {
                SetExtendedData(null);
                return;
            }
            PluginData data = new PluginData();

            var assets = new List<Asset>();
            var sourceFiles = new List<AssetFile>();
            var alreadySavedFiles = new List<string>();

            foreach (Asset asset in LoadedObjects.Keys.Select(key => LoadedObjects[key]))
            {
                if (!alreadySavedFiles.Contains(asset.SourceFile))
                {
                    AssetFile sFile = new AssetFile();
                    if (sFile.AutoFill(asset.SourceFile))
                    {
                        alreadySavedFiles.Add(asset.SourceFile);
                        sourceFiles.Add(sFile);
                    }
                    List<string> additionalFiles = RamCacheUtility.GetFileAdditionalFileHashes(asset.SourceFile);
                    if (!additionalFiles.IsNullOrEmpty())
                    {
                        additionalFiles.ForEach(file =>
                        {
                            AssetFile sFile2 = new AssetFile();
                            if (sFile2.AutoFill(file))
                            {
                                alreadySavedFiles.Add(file);
                                sourceFiles.Add(sFile2);
                            }
                        });
                    }
                }
                assets.Add(asset);
            }

            data.data.Add("Version", (byte)3);
            data.data.Add("Files", MessagePackSerializer.Serialize(sourceFiles));
            data.data.Add("Assets", MessagePackSerializer.Serialize(assets));

            SetExtendedData(data);
        }

        

        private void Start()
        {
            Main.asc = this;
        }


        internal void LoadScene(SceneOperationKind operation, ReadOnlyDictionary<int, ObjectCtrlInfo> loadedItems)
        {
            if (AssetDebug.Enabled) Logger.LogInfo("Scene Load started");
            PluginData data = GetExtendedData();
            if (operation == SceneOperationKind.Clear || operation == SceneOperationKind.Load) LoadedObjects.Clear();
            if (data == null || operation == SceneOperationKind.Clear) return;

            byte version = 0;
            if (data.data.TryGetValue("Version", out object versionS) && versionS != null)
            {
                version = (byte)versionS;
            }

            // keeps track of the hashes for files from this load; only used for v2 -> v3 conversion
            var filenameToHash = new Dictionary<string, string>();

            if (version == 2 || version == 3) 
            {
                switch (version)
                {
                    // old AssetSource format
                    case 2:
                    {
                        List<AssetSource> sourceFiles;
                        if (data.data.TryGetValue("Files", out object filesSerialized) && filesSerialized != null)
                        {
                            sourceFiles = MessagePackSerializer.Deserialize<List<AssetSource>>((byte[])filesSerialized);
                        }
                        else
                        {
                            if (AssetDebug.Enabled) Logger.LogInfo("No sourceFiles found in extended data.");
                            return;
                        }
                        if (AssetDebug.Enabled) Logger.LogInfo($"{sourceFiles.Count} sourceFiles found, extracting to cache...");
                        foreach(AssetSource sourceFile in sourceFiles)
                        {
                            try 
                            { 
                                var extraFileHashes = new List<string>();
                                // the order is important here! First load all extra files to cache then the main file
                                for (int i = 0; i < sourceFile.ExtraFiles.Count; i++)
                                {
                                    byte[] extraFileData = sourceFile.ExtraFiles[i];
                                    string extraFileName = sourceFile.ExtraFileNames[i];
                                    string extraHash = RamCacheUtility.ToCache(extraFileData, extraFileName, null); // extra files do not have extra files themself
                                    extraFileHashes.Add(extraHash);
                                    filenameToHash.Add(extraFileName, extraHash);
                                }
                                // add to cache with reference to the extra files if there are any.
                                string hash = RamCacheUtility.ToCache(sourceFile.File, sourceFile.FileName, extraFileHashes.IsNullOrEmpty() ? null : extraFileHashes);
                                filenameToHash.Add(sourceFile.FileName, hash);

                            } catch (Exception ex)
                            {
                                Logger.LogError("Error extracting file from scene: "+ ex.Message);
                            }
                        }
                        sourceFiles.Clear(); // force garbage colleciton
                        break;
                    }
                    // new AssetFile format
                    case 3:
                    {
                        List<AssetFile> sourceFiles;
                        if (data.data.TryGetValue("Files", out object filesSerialized) && filesSerialized != null)
                        {
                            sourceFiles = MessagePackSerializer.Deserialize<List<AssetFile>>((byte[])filesSerialized);
                        }
                        else
                        {
                            if (AssetDebug.Enabled) Logger.LogInfo("No sourceFiles found in extended data.");
                            return;
                        }
                        if (AssetDebug.Enabled) Logger.LogInfo($"{sourceFiles.Count} sourceFiles found, extracting to cache...");
                        foreach (AssetFile sourceFile in sourceFiles)
                        {
                            try
                            {
                                RamCacheUtility.ToCache(sourceFile);
                            }
                            catch (Exception ex)
                            {
                                Logger.LogError("Error extracting file from scene: " + ex.Message);
                            }
                        }
                        sourceFiles.Clear(); // force garbage collection
                        break;
                    }
                }

                List<Asset> assets;
                if (data.data.TryGetValue("Assets", out object assetsSerialized) && assetsSerialized != null)
                {
                    assets = MessagePackSerializer.Deserialize<List<Asset>>((byte[])assetsSerialized);
                    if (version == 2) // replace asset.sourceFile with a Hash from the cache
                    {
                        var brokenAssets = new List<Asset>();
                        assets.ForEach(asset =>
                        {
                            
                            if (filenameToHash.TryGetValue(asset.SourceFile, out string hash))
                            {
                                asset.SourceFile = hash;
                            }
                            else
                            {
                                // should never happen since the file should have been in the loaded file
                                Logger.LogError($"Asset with filename {asset.SourceFile} could not be updated to version 3 because there was no File with the name found in the cache! This asset will not be loaded!");
                                brokenAssets.Add(asset);
                            }
                        });
                        brokenAssets.ForEach(asset => assets.Remove(asset));
                    }
                }
                else
                {
                    if (AssetDebug.Enabled) Logger.LogInfo("No assets found in extended data, aborting load...");
                    return;
                }

                foreach (Asset asset in assets)
                {
                    try
                    {
                        ObjectCtrlInfo newOCI = loadedItems[asset.Identifier];

                        asset.Identifier = ((OCIItem)newOCI).itemInfo.dicKey;

                        LoadedObjects[((OCIItem)newOCI).itemInfo.dicKey] = asset;

                        if (AssetDebug.Enabled)
                        {
                            File.WriteAllBytes(Path.Combine(GetDumpPath(), RamCacheUtility.GetFileName(asset.SourceFile)), RamCacheUtility.GetFileBlob(asset.SourceFile));
                        }

                        Import import = new Import(
                            asset.SourceFile,
                            asset.HasBones,
                            Instantiate(((OCIItem)newOCI).objectItem.GetComponentInChildren<MeshRenderer>().material),
                            asset.PerRendererMaterials);

                        import.PhysicsProfile = asset.PhysicsProfile;
                        import.PmxHairCollisionAssist = asset.PmxHairCollisionAssist;
                        import.PhysicsEnvironment = PhysicsEnvironmentPersistence.Read(asset);
                        import.Load();
                        if (import == null || !import.IsLoaded)
                        {
                            Logger.LogError($"Loading {RamCacheUtility.GetFileName(asset.SourceFile)} from cache failed");
                            continue;
                        }
                        foreach (int idx in asset.DynamicBoneIndices)
                        {
                            import.BoneNodes[idx].SetDynamic(true);
                        }
                        LoadProcess loadProcess = new LoadProcess(
                            ((OCIItem)newOCI).objectItem,
                            (OCIItem)newOCI,
                            import,
                            new Vector3(asset.Scale[0], asset.Scale[1], asset.Scale[2]),
                            operation == SceneOperationKind.Load ? LoadProcess.LoadProcessKind.Load : LoadProcess.LoadProcessKind.Import);

                        FinishLoadProcess(loadProcess);
                    }
                    catch (Exception e)
                    {
                        Logger.LogError("Error creating asset: " + e.Message);
                    }
                }
            }
            
            if (Hooks.IsOptionalIntegrationAvailable(Hooks.PoseEditorAssembly))
                Hooks.RunOptionalIntegration(Hooks.PoseEditorAssembly, "scene pose editor reload", ForceKKPEreload);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void ForceKKPEreload()
        {
            try
            {
                PluginData data = ExtendedSave.GetSceneExtendedDataById("kkpe");
                if (data == null)
                    return;
                XmlDocument doc = new XmlDocument();
                doc.LoadXml((string)data.data["sceneInfo"]);
                XmlNode node = doc.FirstChild;
                Singleton<HSPE.MainWindow>.Instance.ExternalLoadScene(node);
            }
            catch (Exception e)
            {
                Logger.LogWarning("Forcing KKPE reload failed: "+ e.Message);
            }
        }

        protected override void OnObjectDeleted(ObjectCtrlInfo oci)
        {
            if (!(oci is OCIItem item)) return;
            if (LoadedObjects.ContainsKey(item.itemInfo.dicKey))
            {
                LoadedObjects.Remove(item.itemInfo.dicKey);
            }
        }

        internal void ObjectsCopied(ReadOnlyDictionary<int, ObjectCtrlInfo> copiedItems)
        {
            Dictionary<int, ObjectCtrlInfo> sceneObjects = Studio.Studio.Instance.dicObjectCtrl;
            foreach(int id in copiedItems.Keys)
            {
                if (!(copiedItems[id] is OCIItem)) continue;
                OCIItem newItem = (OCIItem)copiedItems[id];
                OCIItem oldItem = (OCIItem)sceneObjects[id];
                if (!LoadedObjects.TryGetValue(oldItem.itemInfo.dicKey, out Asset oldAsset)) continue;

                if (AssetDebug.Enabled)
                {
                    File.WriteAllBytes(Path.Combine(GetDumpPath(), RamCacheUtility.GetFileName(oldAsset.SourceFile)), RamCacheUtility.GetFileBlob(oldAsset.SourceFile));
                }

                Import import = new Import(
                    oldAsset.SourceFile,
                    oldAsset.HasBones, 
                    Instantiate(newItem.objectItem.GetComponentInChildren<MeshRenderer>().material),
                    oldAsset.PerRendererMaterials
                );
                import.PhysicsProfile = oldAsset.PhysicsProfile;
                import.PmxHairCollisionAssist = oldAsset.PmxHairCollisionAssist;
                import.PhysicsEnvironment = PhysicsEnvironmentPersistence.Read(oldAsset);
                import.Load();
                if (!import.IsLoaded)
                {
                    Logger.LogError($"Loading {oldAsset.SourceFile} from cache failed");
                    continue;
                }
                foreach (int i in oldAsset.DynamicBoneIndices)
                {
                    import.BoneNodes[i].SetDynamic(true);
                }
                // Rebuild from the source item's native persisted physics state.
                newItem.itemInfo.enableDynamicBone = oldItem.itemInfo.enableDynamicBone;
                LoadProcess newLp = new LoadProcess(newItem.objectItem, newItem, import, new Vector3(oldAsset.Scale[0], oldAsset.Scale[1], oldAsset.Scale[2]), LoadProcess.LoadProcessKind.Copy);
                FinishLoadProcess(newLp);
            }
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="path"></param>
        /// <param name="scale"></param>
        /// <param name="armature"></param>
        /// <param name="perRendererMaterials"></param>
        /// <param name="loadBlendshapes"></param>
        /// <returns></returns>
        public void Import(string path, Vector3 scale, bool armature, bool perRendererMaterials, bool loadBlendshapes)
        {
            // unify path structure
            path = path.Replace("\"", "");
            path = path.Replace("\\", "/");

            if (!(File.Exists(path)))
            {
                Logger.LogMessage(L.Format("File {0} does not exist", path));
                return;
            }
            
            OCIItem ociitem = null;
            Material baseMaterial = null;
            Import import = null;
            bool prepared = false;
            try
            {
                // The sphere supplies the native Studio item and its base material.
                // Keep it temporary until parsing and texture resolution succeed.
                ociitem = AddObjectItem.Add(0, 0, 0);
                ociitem.objectItem.SetActive(false);
                ociitem.itemInfo.bones.Clear();

                Logger.LogInfo($"Importing [{path}] started...");
                baseMaterial = Instantiate(ociitem.objectItem.GetComponentInChildren<MeshRenderer>().material);
                string hash = RamCacheUtility.ToCache(path);
                if (AssetDebug.Enabled)
                    File.WriteAllBytes(Path.Combine(GetDumpPath(), Path.GetFileName(path)), RamCacheUtility.GetFileBlob(hash));

                import = new Import(hash, armature, baseMaterial, perRendererMaterials, loadBlendshapes);
                import.Load();
                if (!import.IsLoaded)
                {
                    Logger.LogMessage(L.Format("Import failed: {0}. {1}", Path.GetFileName(path), import.LoadError ?? L.Text("See the AssetImport error in the log.")));
                    return;
                }
                import.ResolveTextureFiles(path);
                prepared = true;
            }
            catch (Exception e)
            {
                Logger.LogError($"Importing [{path}] failed: {e}");
                Logger.LogMessage(L.Format("Import failed: {0}. {1}", Path.GetFileName(path), e.Message));
                return;
            }
            finally
            {
                // Only the new placeholder belongs to this attempt. Saved scene
                // objects, copied objects and an earlier preload remain untouched.
                if (!prepared) CleanupFailedNewImport(ociitem, import, baseMaterial);
            }
            GameObject baseGameObject = ociitem.objectItem;

            // preimport phase
            Main.currentLoadProcess = new LoadProcess(baseGameObject, ociitem, import, scale, LoadProcess.LoadProcessKind.Normal);
            AssetUI.ArmatureMode = Main.currentLoadProcess.Import.HasBones;
            AssetUI.CommonPathText = import.CommonPath;
            AssetUI.PreloadUI = true;

            if (AssetDebug.Enabled)
            {
                foreach (var collider in Resources.FindObjectsOfTypeAll<DynamicBoneCollider>())
                    Logger.LogInfo(collider.gameObject.name);
            }
        }

        private static void CleanupFailedNewImport(OCIItem item, Import import, Material baseMaterial)
        {
            // Use Studio's deletion path so the tree, object registry and plugin
            // callbacks are updated together; destroying objectItem alone is not enough.
            if (item != null && item.treeNodeObject != null)
            {
                try { Studio.Studio.DeleteNode(item.treeNodeObject); }
                catch (Exception e) { Logger.LogWarning("Failed to remove the temporary import item: " + e.Message); }
            }
            // Import.GameObject has not been parented under the native item yet.
            if (import != null && import.GameObject) Destroy(import.GameObject);
            if (baseMaterial) Destroy(baseMaterial);
        }

        /// <summary>
        /// Second part of the import, after preload option have been set.
        /// </summary>
        /// <param name="loadProcess"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void RefreshPoseController(GameObject target)
        {
            DestroyImmediate(target.GetComponent<PoseController>());
            target.AddComponent<PoseController>();
        }

        internal OCIItem FinishLoadProcess(LoadProcess loadProcess)
        {
            AssetUI.PreloadUI = false;
            Import import = loadProcess.Import;
            GameObject loadProcessBase = loadProcess.BaseGameObject;
            if (!(loadProcess.Component is OCIItem ociitem)) return null;

            // ActiveFK and component reconstruction can change runtime state; capture
            // the native saved preference before either runs.
            bool physicsEnabled = PhysicsEnabledForLoad(loadProcess, ociitem);

            // destroy normal sub-object
            DestroyImmediate(loadProcessBase.transform.Find("item_O_Sphere").gameObject);
            ociitem.listBones.ForEach(info => Destroy(info.guideObject.gameObject));
            ociitem.listBones.Clear();

            Vector3 ociScale = ociitem.guideObject.changeAmount.scale;

            // set imported gameObject as child of base
            GameObject importGameObject = import.GameObject;
            importGameObject.transform.parent = loadProcessBase.transform;
            importGameObject.transform.localPosition = Vector3.zero;
            importGameObject.transform.localRotation = Quaternion.identity;
            // DynamicBone must see the final scale when it initializes particles.
            importGameObject.transform.localScale = loadProcess.Scale;
            if (!ociitem.visible) loadProcess.Import.Renderers.ForEach(rend => rend.enabled = false);

            // ==== OCIItem ====

            ociitem.arrayRender = import.Renderers.ToArray();

            ociitem.objectItem.SetActive(true);

            // ==== components ====

            // ItemComponent
            ItemComponent itemComponent = loadProcessBase.GetComponent<ItemComponent>();
            itemComponent.rendNormal = import.Renderers.ToArray();
            itemComponent.rendAlpha = new Renderer[0]; // remove alpha renderer that is on the sphere by default

            // ItemFKCtrl
            ItemFKCtrl itemFKCtrl = loadProcessBase.GetComponent<ItemFKCtrl>();
            itemFKCtrl.listBones.Clear();
            AssetPhysicsEnvironment.Attach(loadProcessBase, importGameObject.transform, new DynamicBone[0], import.PhysicsEnvironment);
            if (!import.HasBones)
            {
                itemFKCtrl.count = 0;
                itemFKCtrl.listBones = new List<ItemFKCtrl.TargetInfo>();
                ociitem.dynamicBones = new DynamicBone[0];
                ociitem.listBones = new List<OCIChar.BoneInfo>();

            }
            else
            {
                itemFKCtrl.count = import.Bones.Count;
                ociitem.listBones = new List<OCIChar.BoneInfo>();
                foreach (Transform bone in import.Bones)
                {
                    GameObject go = importGameObject.transform.FindLoop(bone.name);
                    if (!go) continue;
                    var isNew = false;
                    if (!ociitem.itemInfo.bones.TryGetValue(bone.name, out OIBoneInfo oiboneInfo))
                    {
                        oiboneInfo = new OIBoneInfo(Studio.Studio.GetNewIndex());
                        ociitem.itemInfo.bones.Add(bone.name, oiboneInfo);
                        isNew = true;
                    }
                    GuideObject guideObject = Singleton<GuideObjectManager>.Instance.Add(go.transform, oiboneInfo.dicKey);
                    guideObject.enablePos = false;
                    guideObject.enableScale = false;
                    guideObject.enableMaluti = false;
                    guideObject.calcScale = false;
                    guideObject.scaleRate = 0.5f;
                    guideObject.scaleRot = 0.025f;
                    guideObject.scaleSelect = 0.05f;
                    guideObject.parentGuide = ociitem.guideObject;
                    ociitem.listBones.Add(new OCIChar.BoneInfo(guideObject, oiboneInfo, -1));
                    guideObject.SetActive(false, true);
                    ItemFKCtrl.TargetInfo info = new ItemFKCtrl.TargetInfo(go, oiboneInfo.changeAmount, isNew)
                        {
                            baseRot = bone.localEulerAngles,
#if !KK
                            changeAmount =
                            {
                                defRot = bone.localEulerAngles,
                                isDefRot = true
                            }
#endif
                        };
                    itemFKCtrl.listBones.Add(info);
                }
                ociitem.dynamicBones = new DynamicBone[0];
                ociitem.ActiveFK(ociitem.isFK);

                // dynamic bones
                var dBones = new List<DynamicBone>();
                foreach (BoneNode node in import.BoneNodes)
                {
                    if (!node.IsDynamicRoot) continue;
                    DynamicBone dBone = loadProcessBase.AddComponent<DynamicBone>();
                    dBone.enabled = false;
                    dBone.m_Root = node.GameObject.transform;
                    dBone.m_notRolls = new List<Transform>();
                    dBone.m_Colliders = new List<DynamicBoneCollider>();
                    dBones.Add(dBone);
                }
                if (dBones.Count > 0)
                {
                    import.ApplyPmxPhysicsDefaults(dBones);
                    import.PreparePmxJointPhysics(dBones);
                    import.ApplyPmxBodyCollisions(dBones, loadProcess.Scale, Main.pmxBodyCollisions.Value, import.PhysicsProfile, import.PmxHairCollisionAssist);
                    ociitem.dynamicBones = dBones.ToArray();
                    AssetPhysicsEnvironment.Attach(loadProcessBase, importGameObject.transform, ociitem.dynamicBones, import.PhysicsEnvironment, import.JointPhysicsBindings);
                    NativeContactController.Attach(loadProcessBase, import.BuildNativeContacts());
                    ApplyImportedPhysicsState(ociitem, physicsEnabled);
                    if (Hooks.IsOptionalIntegrationAvailable(Hooks.PoseEditorAssembly))
                        Hooks.RunOptionalIntegration(Hooks.PoseEditorAssembly, "studio pose controller refresh", () => RefreshPoseController(loadProcessBase));

                    if (AssetDebug.Enabled) Logger.LogInfo($"Created {ociitem.dynamicBones.Length} dynamic bone chains on {loadProcess.Import.SourceFileName}; physics enabled={physicsEnabled}");
                    if (AssetDebug.Enabled) StartCoroutine(LogDynamicBoneState(ociitem, ociitem.dynamicBones));
                }

            }

            // ==== finish ====

            if (!loadProcess.Import.IsPmx) importGameObject.transform.localRotation *= Quaternion.Euler(new Vector3(0, 180, 0));

            ociitem.treeNodeObject.textName = loadProcess.Import.SourceFileName;

            if (loadProcess.Kind == LoadProcess.LoadProcessKind.Normal || loadProcess.Kind == LoadProcess.LoadProcessKind.Copy)
            {
                // add to scene controller
                if (SceneName == null) SceneName = "";

                Asset asset = new Asset();
                asset.SourceFile = loadProcess.Import.SourceIdentifier; // use identifierHash
                asset.DynamicBoneIndices = new List<int>();
                for (int i = 0; i < import.BoneNodes.Count; i++)
                {
                    BoneNode node = import.BoneNodes[i];
                    if (node.IsDynamicRoot) asset.DynamicBoneIndices.Add(i);
                }
                asset.Identifier = ((OCIItem)loadProcess.Component).itemInfo.dicKey;
                asset.Scale = new float[] { loadProcess.Scale.x, loadProcess.Scale.y, loadProcess.Scale.z };
                asset.HasBones = loadProcess.Import.HasBones;
                asset.PerRendererMaterials = loadProcess.Import.PerRendererMaterials;
                asset.PhysicsProfile = import.PhysicsProfile;
                asset.PmxHairCollisionAssist = import.PmxHairCollisionAssist;
                PhysicsEnvironmentPersistence.Write(asset, import.PhysicsEnvironment);

                LoadedObjects[ociitem.itemInfo.dicKey] = asset;
            }

            import.ExportMmddMapping();
            if (loadProcess.Kind == LoadProcess.LoadProcessKind.Normal)
            {
                Logger.LogInfo("Importing textures in background; wait for completion before saving.");
                // The plugin survives scene/controller destruction so cancellation is polled.
                Main.instance.StartCoroutine(new ImportTextureData(loadProcess.ColorLut).ApplyAsync(
                    import.MaterialTextures.Values.SelectMany(paths => paths).ToArray(),
                    () => this && loadProcessBase && importGameObject,
                    (path, bytes) =>
                    {
                        if (!path.Material) return;
                        string property = path.Type == Assimp.TextureType.Normals ? "NormalMap" : "MainTex";
                        Singleton<KK_Plugins.MaterialEditor.SceneController>.Instance.SetMaterialTexture(
                            ociitem.itemInfo.dicKey, path.Material, property, bytes);
                    },
                    message => Logger.LogWarning(message),
                    () => Logger.LogInfo($"Asset [{import.SourceFileName}] loaded; texture processing finished.")));
            }
            else Logger.LogInfo($"Asset [{import.SourceFileName}] was loaded successfully");

            return ociitem;
        }
    }
}
