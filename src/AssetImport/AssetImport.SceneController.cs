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

namespace AssetImport
{
    /// <summary>
    /// SceneController for AssetImport.
    /// Handles save/load and all kinds of reload events for StudioItems.
    /// </summary>
    public class AssetSceneController : SceneCustomFunctionController
    {
        private static readonly Dictionary<int, Asset> LoadedObjects = new Dictionary<int, Asset>();

        private static readonly ManualLogSource Logger = Main.Logger;

        internal string SceneName = "";

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
            if (!(LoadedObjects.Count > 0)) return;
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
            Logger.LogDebug("Scene Load started");
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
                            Logger.LogDebug("No sourceFiles found in extended data.");
                            return;
                        }
                        Logger.LogDebug($"{sourceFiles.Count} sourceFiles found, extracting to cache...");
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
                            Logger.LogDebug("No sourceFiles found in extended data.");
                            return;
                        }
                        Logger.LogDebug($"{sourceFiles.Count} sourceFiles found, extracting to cache...");
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
                    Logger.LogDebug("No assets found in extended data, aborting load...");
                    return;
                }

                foreach (Asset asset in assets)
                {
                    try
                    {
                        ObjectCtrlInfo newOCI = loadedItems[asset.Identifier];

                        asset.Identifier = ((OCIItem)newOCI).itemInfo.dicKey;

                        LoadedObjects[((OCIItem)newOCI).itemInfo.dicKey] = asset;

                        if (Main.dumpAssets.Value)
                        {
                            File.WriteAllBytes(Path.Combine(GetDumpPath(), RamCacheUtility.GetFileName(asset.SourceFile)), RamCacheUtility.GetFileBlob(asset.SourceFile));
                        }

                        Import import = new Import(
                            asset.SourceFile,
                            asset.HasBones,
                            Instantiate(((OCIItem)newOCI).objectItem.GetComponentInChildren<MeshRenderer>().material),
                            asset.DoFbxTranslation,
                            asset.PerRendererMaterials);

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
            
            ForceKKPEreload();
        }

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

                if (Main.dumpAssets.Value)
                {
                    File.WriteAllBytes(Path.Combine(GetDumpPath(), RamCacheUtility.GetFileName(oldAsset.SourceFile)), RamCacheUtility.GetFileBlob(oldAsset.SourceFile));
                }

                Import import = new Import(
                    oldAsset.SourceFile,
                    oldAsset.HasBones, 
                    Instantiate(newItem.objectItem.GetComponentInChildren<MeshRenderer>().material),
                    oldAsset.DoFbxTranslation,
                    oldAsset.PerRendererMaterials
                );
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
        /// <param name="doFbxTranslation"></param>
        /// <returns></returns>
        public void Import(string path, Vector3 scale, bool armature, bool perRendererMaterials, bool doFbxTranslation)
        {
            // unify path structure
            path = path.Replace("\"", "");
            path = path.Replace("\\", "/");

            if (!(File.Exists(path)))
            {
                Logger.LogMessage($"File {path} does not exist");
                return;
            }
            
            // add a sphere
            OCIItem ociitem = AddObjectItem.Add(0, 0, 0);
            ociitem.objectItem.SetActive(false);
            ociitem.itemInfo.bones.Clear();

            Logger.LogInfo($"Importing [{path}] started...");

            GameObject baseGameObject = ociitem.objectItem;

            // grab a copy of the sphere's material to create the materials for the imported objects from
            Material baseMaterial = Instantiate(baseGameObject.GetComponentInChildren<MeshRenderer>().material);

            string hash = RamCacheUtility.ToCache(path);

            if (Main.dumpAssets.Value)
            {
                File.WriteAllBytes(Path.Combine(GetDumpPath(), Path.GetFileName(path)), RamCacheUtility.GetFileBlob(hash));
            }

            Import import = new Import(
                hash,
                armature, 
                baseMaterial, 
                doFbxTranslation, 
                perRendererMaterials
            );
            import.Load();
            if (!import.IsLoaded) return;

            // preimport phase
            Main.currentLoadProcess = new LoadProcess(baseGameObject, ociitem, import, scale, LoadProcess.LoadProcessKind.Normal);
            AssetUI.ArmatureMode = Main.currentLoadProcess.Import.HasBones;
            AssetUI.CommonPathText = import.CommonPath;
            AssetUI.PreloadUI = true;

            DynamicBoneCollider[] colliders = Resources.FindObjectsOfTypeAll<DynamicBoneCollider>();
            foreach(DynamicBoneCollider collider in colliders)
            {
                Logger.LogInfo(collider.gameObject.name);
            }
        }

        /// <summary>
        /// Second part of the import, after preload option have been set.
        /// </summary>
        /// <param name="loadProcess"></param>
        /// <returns></returns>
        internal OCIItem FinishLoadProcess(LoadProcess loadProcess)
        {
            AssetUI.PreloadUI = false;
            Import import = loadProcess.Import;
            GameObject loadProcessBase = loadProcess.BaseGameObject;
            if (!(loadProcess.Component is OCIItem ociitem)) return null;

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
            importGameObject.transform.localScale = Vector3.one;
            if (!ociitem.visible) loadProcess.Import.Renderers.ForEach(rend => rend.enabled = false);

            // ==== OCIItem ====

            ociitem.arrayRender = import.Renderers.ToArray();

            ociitem.objectItem.SetActive(true);

            // ==== components ====

            // ItemComponent
            ItemComponent itemComponent = loadProcessBase.GetComponent<ItemComponent>();
            itemComponent.rendNormal = import.Renderers.ToArray();
            itemComponent.rendAlpha = Array.Empty<Renderer>(); // remove alpha renderer that is on the sphere by default

            // ItemFKCtrl
            ItemFKCtrl itemFKCtrl = loadProcessBase.GetComponent<ItemFKCtrl>();
            itemFKCtrl.listBones.Clear();
            if (!import.HasBones)
            {
                itemFKCtrl.count = 0;
                itemFKCtrl.listBones = new List<ItemFKCtrl.TargetInfo>();
                ociitem.dynamicBones = Array.Empty<DynamicBone>();
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
                            changeAmount =
                            {
                                defRot = bone.localEulerAngles,
                                isDefRot = true
                            }
                        };
                    itemFKCtrl.listBones.Add(info);
                }
                ociitem.dynamicBones = Array.Empty<DynamicBone>();
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
                    ociitem.dynamicBones = dBones.ToArray();
                    ociitem.ActiveDynamicBone(true);
                    DestroyImmediate(loadProcessBase.GetComponent<PoseController>());
                    loadProcessBase.AddComponent<PoseController>();

                    Logger.LogDebug($"Activated {ociitem.dynamicBones.Length} dynamic bone chains on {loadProcess.Import.SourceFileName}");
                }

            }

            // ==== finish ====

            if (loadProcess.Kind == LoadProcess.LoadProcessKind.Normal)
            {
                foreach (Material mat in import.MaterialTextures.Keys)
                {
                    foreach (TexturePath p in import.MaterialTextures[mat])
                    {
                        if (!p.Use || !p.PathOkay()) continue;
                        byte[] data = File.ReadAllBytes(p.Path);
                        var prop = "";
                        switch (p.Type)
                        {
                            case Assimp.TextureType.Diffuse:
                                prop = "MainTex";
                                break;
                            case Assimp.TextureType.Normals:
                                prop = "NormalMap";
                                break;
                            default:
                                break;
                        }
                        Singleton<KK_Plugins.MaterialEditor.SceneController>.Instance.SetMaterialTexture(ociitem.itemInfo.dicKey, p.Material, prop, data);
                        Logger.LogDebug($"Set Texture on {p.Material.name} - {prop}");
                    }
                }
            }

            Vector3 s = importGameObject.transform.localScale;
            importGameObject.transform.localScale = new Vector3(s.x * loadProcess.Scale.x, s.y * loadProcess.Scale.y, s.z * loadProcess.Scale.z);
            importGameObject.transform.localRotation *= Quaternion.Euler(new Vector3(0, 180, 0));

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
                asset.DoFbxTranslation = loadProcess.Import.DoFbxTranslation;

                LoadedObjects[ociitem.itemInfo.dicKey] = asset;
            }

            Logger.LogInfo($"Asset [{loadProcess.Import.SourceFileName}] was loaded successfully");

            return ociitem;
        }
    }
}
