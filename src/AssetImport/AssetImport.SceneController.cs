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
    /// Handels save/load and all kinds of reload events for StudioItems.
    /// </summary>
    public class AssetSceneController : SceneCustomFunctionController
    {
        private static Dictionary<int, Asset> loadedObjects = new Dictionary<int, Asset>();

        internal static ManualLogSource Logger = Main.Logger;

        internal string sceneName = "";

        public string getDumpPath()
        {
            string c = sceneName.Replace(".png", "");
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
            if (!(loadedObjects.Count > 0)) return;
            PluginData data = new PluginData();

            List<Asset> assets = new List<Asset>();
            List<AssetFile> sourceFiles = new List<AssetFile>();
            List<String> alreadySavedFiles = new List<string>();

            foreach(int key in loadedObjects.Keys)
            {
                Asset asset = loadedObjects[key];
                if (!alreadySavedFiles.Contains(asset.sourceFile))
                {
                    AssetFile sFile = new AssetFile();
                    if (sFile.AutoFill(asset.sourceFile))
                    {
                        alreadySavedFiles.Add(asset.sourceFile);
                        sourceFiles.Add(sFile);
                    }
                    List<string> additionalFiles = RAMCacheUtility.GetFileAdditionalFileHashes(asset.sourceFile);
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
            AssetImport.asc = this;
        }


        internal void LoadScene(SceneOperationKind operation, ReadOnlyDictionary<int, ObjectCtrlInfo> loadedItems)
        {
            AssetImport.Logger.LogDebug("Scene Load started");
            PluginData data = GetExtendedData();
            if (operation == SceneOperationKind.Clear || operation == SceneOperationKind.Load) loadedObjects.Clear();
            if (data == null || operation == SceneOperationKind.Clear) return;

            byte version = 0;
            if (data.data.TryGetValue("Version", out var versionS) && versionS != null)
            {
                version = (byte)versionS;
            }

            // keeps track of the hashes for files from this load; only used for v2 -> v3 conversion
            Dictionary<string, string> filenameToHash = new Dictionary<string, string>();

            if (version == 2 || version == 3) 
            {
                if (version == 2) // old AssetSource format
                {
                    List<AssetSource> sourceFiles;
                    if (data.data.TryGetValue("Files", out var filesSerialized) && filesSerialized != null)
                    {
                        sourceFiles = MessagePackSerializer.Deserialize<List<AssetSource>>((byte[])filesSerialized);
                    }
                    else
                    {
                        AssetImport.Logger.LogDebug("No sourceFiles found in extended data.");
                        return;
                    }
                    Logger.LogDebug($"{sourceFiles.Count} sourceFiles found, extracting to cache...");
                    foreach(AssetSource sourceFile in sourceFiles)
                    {
                        try 
                        { 
                            List<string> extraFileHashes = new List<string>();
                            // the order is important here! First load all extra files to cache then the main file
                            for (int i = 0; i < sourceFile.extraFiles.Count; i++)
                            {
                                byte[] extraFileData = sourceFile.extraFiles[i];
                                string extraFileName = sourceFile.extraFileNames[i];
                                string extraHash = RAMCacheUtility.ToCache(extraFileData, extraFileName, null); // extra files do not have extra files themself
                                extraFileHashes.Add(extraHash);
                                filenameToHash.Add(extraFileName, extraHash);
                            }
                            // add to cache with reference to the extra files if there are any.
                            string hash = RAMCacheUtility.ToCache(sourceFile.file, sourceFile.fileName, extraFileHashes.IsNullOrEmpty() ? null : extraFileHashes);
                            filenameToHash.Add(sourceFile.fileName, hash);

                        } catch (Exception ex)
                        {
                            Logger.LogError("Error extracting file from scene: "+ ex.Message);
                        }
                    }
                    sourceFiles.Clear(); // force garbage colleciton
                }
                else if (version == 3) // new AssetFile format
                {
                    List<AssetFile> sourceFiles;
                    if (data.data.TryGetValue("Files", out var filesSerialized) && filesSerialized != null)
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
                            RAMCacheUtility.ToCache(sourceFile);
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError("Error extracting file from scene: " + ex.Message);
                        }
                    }
                    sourceFiles.Clear(); // force garbage collection
                }

                List<Asset> assets;
                if (data.data.TryGetValue("Assets", out var assetsSerialized) && assetsSerialized != null)
                {
                    assets = MessagePackSerializer.Deserialize<List<Asset>>((byte[])assetsSerialized);
                    if (version == 2) // replace asset.sourceFile with a Hash from the cache
                    {
                        List<Asset> brokenAssets = new List<Asset>();
                        assets.ForEach(asset =>
                        {
                            
                            if (filenameToHash.TryGetValue(asset.sourceFile, out string hash))
                            {
                                asset.sourceFile = hash;
                            }
                            else
                            {
                                // should never happen since the file should have been in the loaded file
                                Logger.LogError($"Asset with filename {asset.sourceFile} could not be updated to version 3 because there was no File with the name found in the cache! This asset will not be loaded!");
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
                        ObjectCtrlInfo newOCI = loadedItems[asset.identifier];

                        asset.identifier = ((OCIItem)newOCI).itemInfo.dicKey;

                        loadedObjects[((OCIItem)newOCI).itemInfo.dicKey] = asset;

                        if (Main.dumpAssets.Value)
                        {
                            File.WriteAllBytes(Path.Combine(getDumpPath(), RAMCacheUtility.GetFileName(asset.sourceFile)), RAMCacheUtility.GetFileBlob(asset.sourceFile));
                        }

                        Import import = new Import(
                            asset.sourceFile,
                            1, // remove later
                            asset.hasBones,
                            Instantiate(((OCIItem)newOCI).objectItem.GetComponentInChildren<MeshRenderer>().material),
                            asset.doFbxTranslation,
                            asset.perRendererMaterials);

                        import.Load();
                        if (import == null || !import.isLoaded)
                        {
                            AssetImport.Logger.LogError($"Loading {RAMCacheUtility.GetFileName(asset.sourceFile)} from cache failed");
                            continue;
                        }
                        foreach (int idx in asset.dynamicBoneIndices)
                        {
                            import.boneNodes[idx].setDynamic(true);
                        }
                        LoadProcess loadProcess = new LoadProcess(
                            ((OCIItem)newOCI).objectItem,
                            (OCIItem)newOCI,
                            import,
                            new Vector3(asset.scale[0], asset.scale[1], asset.scale[2]),
                            operation == SceneOperationKind.Load ? LoadProcess.loadProcessKind.LOAD : LoadProcess.loadProcessKind.IMPORT);

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
            PluginData data = ExtendedSave.GetSceneExtendedDataById("kkpe");
            if (data == null)
                return;
            XmlDocument doc = new XmlDocument();
            doc.LoadXml((string)data.data["sceneInfo"]);
            XmlNode node = doc.FirstChild;
            Singleton<HSPE.MainWindow>.Instance.ExternalLoadScene(node);
        }

        protected override void OnObjectDeleted(ObjectCtrlInfo oci)
        {
            if (oci is OCIItem)
            {
                if (loadedObjects.ContainsKey(((OCIItem)oci).itemInfo.dicKey))
                {
                    loadedObjects.Remove(((OCIItem)oci).itemInfo.dicKey);
                }
            }
        }

        internal void ObjectsCopied(ReadOnlyDictionary<Int32, ObjectCtrlInfo> copiedItems)
        {
            Dictionary<int, ObjectCtrlInfo> sceneObjects = Studio.Studio.Instance.dicObjectCtrl;
            foreach(int id in copiedItems.Keys)
            {
                if (!(copiedItems[id] is OCIItem)) continue;
                OCIItem newItem = (OCIItem)copiedItems[id];
                OCIItem oldItem = (OCIItem)sceneObjects[id];
                if (loadedObjects.ContainsKey(oldItem.itemInfo.dicKey))
                {
                    Asset oldAsset = loadedObjects[oldItem.itemInfo.dicKey];

                    if (Main.dumpAssets.Value)
                    {
                        File.WriteAllBytes(Path.Combine(getDumpPath(), RAMCacheUtility.GetFileName(oldAsset.sourceFile)), RAMCacheUtility.GetFileBlob(oldAsset.sourceFile));
                    }

                    Import import = new Import(
                        oldAsset.sourceFile,
                        1, // remove later
                        oldAsset.hasBones, 
                        Instantiate(newItem.objectItem.GetComponentInChildren<MeshRenderer>().material),
                        oldAsset.doFbxTranslation,
                        oldAsset.perRendererMaterials
                    );
                    import.Load();
                    if (import == null || !import.isLoaded)
                    {
                        Logger.LogError($"Loading {oldAsset.sourceFile} from cache failed");
                        continue;
                    }
                    foreach (int i in oldAsset.dynamicBoneIndices)
                    {
                        import.boneNodes[i].setDynamic(true);
                    }
                    LoadProcess newLP = new LoadProcess(newItem.objectItem, newItem, import, new Vector3(oldAsset.scale[0], oldAsset.scale[1], oldAsset.scale[2]), LoadProcess.loadProcessKind.COPY);
                    FinishLoadProcess(newLP);
                }
            }
        }

        

        /// <summary>
        /// 
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public void Import(string path, Vector3 scale, bool armature, bool perRendererMaterials, bool doFbxTranslation)
        {
            // unify path structure
            path = path.Replace("\"", "");
            path = path.Replace("\\", "/");

            // add a sphere
            OCIItem ociitem = AddObjectItem.Add(0, 0, 0);
            ociitem.itemInfo.bones.Clear();

            Logger.LogInfo($"Importing [{path}] started...");

            GameObject _base = ociitem.objectItem;

            // grab a copy of the sphere's material to create the materials for the imported objects from
            Material _baseMaterial = Instantiate(_base.GetComponentInChildren<MeshRenderer>().material);

            string hash = RAMCacheUtility.ToCache(path);

            if (Main.dumpAssets.Value)
            {
                File.WriteAllBytes(Path.Combine(getDumpPath(), Path.GetFileName(path)), RAMCacheUtility.GetFileBlob(hash));
            }

            Import import = new Import(
                hash,
                1, // remove later
                armature, 
                _baseMaterial, 
                doFbxTranslation, 
                perRendererMaterials
            );
            import.Load();
            if (!import.isLoaded) return;

            // preimport phase
            Main.currentLoadProcess = new LoadProcess(_base, ociitem, import, scale, LoadProcess.loadProcessKind.NORMAL);
            AssetUI.armatureMode = Main.currentLoadProcess.import.hasBones;
            AssetUI.commonPathText = import.commonPath;
            AssetUI.preloadUI = true;

            DynamicBoneCollider[] colliders = Resources.FindObjectsOfTypeAll<DynamicBoneCollider>();
            foreach(DynamicBoneCollider collider in colliders)
            {
                Logger.LogInfo(collider.gameObject.name);
            }
        }

        /// <summary>
        /// Second part of the import, after preload option have been set.
        /// </summary>
        /// <param name="import"></param>
        /// <returns></returns>
        internal OCIItem FinishLoadProcess(LoadProcess loadProcess)
        {
            AssetUI.preloadUI = false;
            Import import = loadProcess.import;
            GameObject _base = loadProcess._base;
            if (!(loadProcess.component is OCIItem ociitem)) return null;

            // destory normal subobject
            DestroyImmediate(_base.transform.Find("item_O_Sphere").gameObject);
            ociitem.listBones.ForEach(info => Destroy(info.guideObject.gameObject));
            ociitem.listBones.Clear();


            Vector3 ociScale = ociitem.guideObject.changeAmount.scale;

            // set imported gameObject as child of base
            GameObject _object = import.gameObject;
            _object.transform.parent = _base.transform;
            _object.transform.localPosition = Vector3.zero;
            _object.transform.localRotation = Quaternion.identity;
            _object.transform.localScale = Vector3.one;
            if (!ociitem.visible) loadProcess.import.renderers.ForEach(rend => rend.enabled = false);

            // ==== OCIItem ====

            ociitem.arrayRender = import.renderers.ToArray();

            // ==== components ====

            // ItemComponent
            ItemComponent itemComponent = _base.GetComponent<ItemComponent>();
            itemComponent.rendNormal = import.renderers.ToArray();
            itemComponent.rendAlpha = new Renderer[0]; // remove alpha renderer that is on the sphere by default

            // ItemFKCtrl
            ItemFKCtrl itemFKCtrl = _base.GetComponent<ItemFKCtrl>();
            itemFKCtrl.listBones.Clear();
            if (!import.hasBones)
            {
                itemFKCtrl.count = 0;
                itemFKCtrl.listBones = new List<ItemFKCtrl.TargetInfo>();
                ociitem.dynamicBones = new DynamicBone[0];
                ociitem.listBones = new List<OCIChar.BoneInfo>();

            }
            else
            {
                itemFKCtrl.count = import.bones.Count;
                ociitem.listBones = new List<OCIChar.BoneInfo>();
                foreach (Transform bone in import.bones)
                {
                    GameObject gameObject = _object.transform.FindLoop(bone.name);
                    if (!(gameObject == null))
                    {
                        OIBoneInfo oiboneInfo = null;
                        bool isNew = false;
                        if (!ociitem.itemInfo.bones.TryGetValue(bone.name, out oiboneInfo))
                        {
                            oiboneInfo = new OIBoneInfo(Studio.Studio.GetNewIndex());
                            ociitem.itemInfo.bones.Add(bone.name, oiboneInfo);
                            isNew = true;
                        }
                        GuideObject guideObject = Singleton<GuideObjectManager>.Instance.Add(gameObject.transform, oiboneInfo.dicKey);
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
                        ItemFKCtrl.TargetInfo info = new ItemFKCtrl.TargetInfo(gameObject, oiboneInfo.changeAmount, isNew);
                        info.baseRot = bone.localEulerAngles;
                        info.changeAmount.defRot = bone.localEulerAngles;
                        info.changeAmount.isDefRot = true;
                        itemFKCtrl.listBones.Add(info);
                    }
                }
                ociitem.dynamicBones = new DynamicBone[0];
                ociitem.ActiveFK(ociitem.isFK);

                // dynamic bones
                List<DynamicBone> dBones = new List<DynamicBone>();
                foreach (BoneNode node in import.boneNodes)
                {
                    if (node.isDynamicRoot)
                    {
                        DynamicBone dBone = _base.AddComponent<DynamicBone>();
                        dBone.enabled = false;
                        dBone.m_Root = node.gameObject.transform;
                        dBone.m_notRolls = new List<Transform>();
                        dBone.m_Colliders = new List<DynamicBoneCollider>();
                        dBones.Add(dBone);
                    }
                }
                if (dBones.Count > 0)
                {
                    ociitem.dynamicBones = dBones.ToArray();
                    ociitem.ActiveDynamicBone(true);
                    DestroyImmediate(_base.GetComponent<PoseController>());
                    _base.AddComponent<PoseController>();

                    Logger.LogDebug($"Activated {ociitem.dynamicBones.Length} dynamic bone chains on {loadProcess.import.sourceFileName}");
                }

            }

            // ==== finish ====

            if (loadProcess.kind == LoadProcess.loadProcessKind.NORMAL)
            {
                foreach (Material mat in import.materialTextures.Keys)
                {
                    foreach (TexturePath p in import.materialTextures[mat])
                    {
                        if (!p.use || !p.pathOkay()) continue;
                        byte[] data = File.ReadAllBytes(p.path);
                        string prop = "";
                        switch (p.type)
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
                        Singleton<KK_Plugins.MaterialEditor.SceneController>.Instance.SetMaterialTexture(ociitem.itemInfo.dicKey, p.material, prop, data);
                        Logger.LogDebug($"Set Texture on {p.material.name} - {prop}");
                    }
                }
            }

            Vector3 s = _object.transform.localScale;
            _object.transform.localScale = new Vector3(s.x * loadProcess.scale.x, s.y * loadProcess.scale.y, s.z * loadProcess.scale.z);
            _object.transform.localRotation *= Quaternion.Euler(new Vector3(0, 180, 0));

            ociitem.treeNodeObject.textName = loadProcess.import.sourceFileName;

            if (loadProcess.kind == LoadProcess.loadProcessKind.NORMAL || loadProcess.kind == LoadProcess.loadProcessKind.COPY)
            {
                // add to scene controller
                if (sceneName == null) sceneName = "";

                Asset asset = new Asset();
                asset.sourceFile = loadProcess.import.sourceIdentifier; // use identifierHash
                asset.dynamicBoneIndices = new List<int>();
                for (int i = 0; i < import.boneNodes.Count; i++)
                {
                    BoneNode node = import.boneNodes[i];
                    if (node.isDynamicRoot) asset.dynamicBoneIndices.Add(i);
                }
                asset.identifier = ((OCIItem)loadProcess.component).itemInfo.dicKey;
                asset.scale = new float[] { loadProcess.scale.x, loadProcess.scale.y, loadProcess.scale.z };
                asset.hasBones = loadProcess.import.hasBones;
                asset.perRendererMaterials = loadProcess.import.perRendererMaterials;
                asset.doFbxTranslation = loadProcess.import.doFbxTranslation;

                loadedObjects[ociitem.itemInfo.dicKey] = asset;
            }

            Logger.LogInfo($"Asset [{loadProcess.import.sourceFileName}] was loaded successfully");

            return ociitem;
        }
    }
}
