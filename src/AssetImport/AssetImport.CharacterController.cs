using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using IllusionUtility.GetUtility;
using BepInEx.Logging;
using KKAPI;
using KKAPI.Chara;
using ExtensibleSaveFormat;
using MessagePack;
using System.IO;
using Main = AssetImport.AssetImport;
using System.Collections;
using System.Text.RegularExpressions;
using BepInEx.Bootstrap;
using KKABMX.Core;
using HSPE;

namespace AssetImport
{
    /// <summary>
    /// CharacterController for AssetImport.
    /// Handels save/load and all kinds of reload events for Accessories.
    /// </summary>
    class AssetCharaController : CharaCustomFunctionController
    {
        private Dictionary<int, Dictionary<int, Asset>> loadedObjects = new Dictionary<int, Dictionary<int, Asset>>();
        
        ManualLogSource Logger = Main.Logger;

        internal Dictionary<int, string> coordinateCardNames = new Dictionary<int, string>();
        internal string characterCardName { get => ChaControl.chaFile.charaFileName.IsNullOrEmpty() ? "MakerDefault" : ChaControl.chaFile.charaFileName; }

        private bool hasBeenLoadedAlready = false;

        // used to transfer plugin data from Coordinate Load Options temp character to the real characters.
        private static PluginData cloTransferPluginData;

        internal IEnumerator removeCloTransferPluginData()
        {
            yield return null;
            yield return null;
            cloTransferPluginData = null;
        }

        internal IEnumerator resetLoadedAlready()
        {
            yield return null;
            yield return null;
            yield return null;
            yield return null;
            yield return null;
            yield return null;
            yield return null;
            hasBeenLoadedAlready = false;
        }

        protected override void Start()
        {
            base.Start();
        }

        public string getDumpPath()
        {
            return getDumpPath(ChaControl.fileStatus.coordinateType);
        }

        public string getDumpPath(int clothingSlot)
        {
            string cha = Regex.Replace(characterCardName, @"[^a-zA-Z0-9\-_]", "",RegexOptions.Compiled);
            string c = $"{cha}/{clothingSlot}";
            if (coordinateCardNames.ContainsKey(clothingSlot)) c = coordinateCardNames[clothingSlot];

            if (!Directory.Exists($"./UserData/AssetImport/Cache/{c}"))
            {
                Directory.CreateDirectory($"./UserData/AssetImport/Cache/{c}");
            }
            return $"./UserData/AssetImport/Cache/{c}/";
        }

        // accessory events

        internal void accessoryChangeEvent(int slot)
        {
            if (loadedObjects.ContainsKey(ChaControl.fileStatus.coordinateType))
                if (loadedObjects[ChaControl.fileStatus.coordinateType].ContainsKey(slot))
                {
                    loadedObjects[ChaControl.fileStatus.coordinateType].Remove(slot);
                }
        }

        internal void accessoryTransferedEvent(int source, int destination)
        {
            int cSet = ChaControl.fileStatus.coordinateType;
            if (loadedObjects.ContainsKey(cSet))
            {
                Logger.LogDebug("Accessory Transfer Event");
                if (loadedObjects[cSet].ContainsKey(source))
                {
                    Asset asset = new Asset();
                    asset.sourceFile = loadedObjects[cSet][source].sourceFile;
                    asset.dynamicBoneIndices = loadedObjects[cSet][source].dynamicBoneIndices;
                    asset.identifier = destination;
                    asset.scale = loadedObjects[cSet][source].scale;
                    asset.hasBones = loadedObjects[cSet][source].hasBones;
                    asset.perRendererMaterials = loadedObjects[cSet][source].perRendererMaterials;
                    asset.doFbxTranslation = loadedObjects[cSet][source].doFbxTranslation;
                    loadedObjects[cSet][destination] = asset;
                    Logger.LogDebug($"Source slot {source} --> Destination slot {destination}");
                }
                else if (loadedObjects[cSet].ContainsKey(destination))
                {
                    loadedObjects[cSet].Remove(destination);
                }
            }
        }

        internal void accessoryCopiedEvent(int source, int destination, IEnumerable<int> slots)
        {
            if (!loadedObjects.ContainsKey(source))
            {
                if (loadedObjects.ContainsKey(destination))
                {
                    foreach(int slot in slots)
                    {
                        if (loadedObjects[destination].ContainsKey(slot))
                        {
                            loadedObjects[destination].Remove(slot);
                        }
                    }
                }
                return;
            }
            if (!loadedObjects.ContainsKey(destination))
            {
                loadedObjects[destination] = new Dictionary<int, Asset>();
            }
            foreach(int slot in slots)
            {
                if (loadedObjects[source].ContainsKey(slot))
                {
                    Asset asset = new Asset();
                    asset.sourceFile = loadedObjects[source][slot].sourceFile;
                    asset.dynamicBoneIndices = loadedObjects[source][slot].dynamicBoneIndices;
                    asset.identifier = slot;
                    asset.scale = loadedObjects[source][slot].scale;
                    asset.hasBones = loadedObjects[source][slot].hasBones;
                    asset.perRendererMaterials = loadedObjects[source][slot].perRendererMaterials;
                    asset.doFbxTranslation = loadedObjects[source][slot].doFbxTranslation;
                    loadedObjects[destination][slot] = asset;
                    Logger.LogDebug($"Source: Type {source}, Slot {slot} --> Destination: Type {destination}");
                }
                else if (loadedObjects[destination].ContainsKey(slot))
                {
                    loadedObjects[destination].Remove(slot);
                }
            }
        }

        protected override void OnCardBeingSaved(GameMode currentGameMode)
        {
            PluginData data = new PluginData();

            List<AssetFile> sourceFiles = new List<AssetFile>();
            List<string> alreadySavedFiles = new List<string>();

            foreach(int cSet in loadedObjects.Keys)
            {
                List<Asset> assets = new List<Asset>();

                if (!loadedObjects.ContainsKey(cSet)) continue;
                foreach (int slot in loadedObjects[cSet].Keys)
                {
                    Asset asset = loadedObjects[cSet][slot];
                    if (!alreadySavedFiles.Contains(asset.sourceFile))
                    {
                        // base file
                        AssetFile sFile = new AssetFile();
                        if (sFile.AutoFill(asset.sourceFile))
                        {
                            alreadySavedFiles.Add(asset.sourceFile);
                            sourceFiles.Add(sFile);
                        }
                        List<string> additionalFiles = RAMCacheUtility.GetFileAdditionalFileHashes(asset.sourceFile);

                        // additonal files (for .gltf)
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

                data.data.Add($"Assets{cSet}", MessagePackSerializer.Serialize(assets));
            }

            data.data.Add($"Files", MessagePackSerializer.Serialize(sourceFiles));
            data.data.Add("Version", (byte)3);

            SetExtendedData(data);
            Logger.LogDebug("Set Extended data");
        }

        internal void LoadCharacter(GameMode currentGameMode, bool MaintainState)
        {
            Logger.LogDebug($"Character Load Started {ChaControl.fileParam.fullname}");
            loadedObjects.Clear();
            if (currentGameMode == GameMode.Maker)
            {
                RAMCacheUtility.clearCache();
                GameObject toggleObject = GameObject.Find("CustomScene/CustomRoot/FrontUIGroup/CustomUIGroup/CvsMenuTree/06_SystemTop/charaFileControl/charaFileWindow/WinRect/CharaLoad/Select/tglItem05");
                if (toggleObject != null && toggleObject.GetComponent<Toggle>() != null)
                {
                    if (!toggleObject.GetComponent<Toggle>().isOn) return;
                }
            }
            PluginData data = GetExtendedData();
            if (data == null) return;
            byte version = 0;
            if (data.data.TryGetValue("Version", out var versionS) && versionS != null)
            {
                version = (byte)versionS;
            }


            if (version == 2 || version == 3)
            {
                for(int cSet = 0; cSet < ChaControl.chaFile.coordinate.Length; cSet++)
                {
                    // keeps track of the hashes for files from this load; only used for v2 -> v3 conversion
                    Dictionary<string, string> filenameToHash = new Dictionary<string, string>();
                    
                    if (version == 2)
                    {
                        List<AssetSource> sourceFiles;
                        if (data.data.TryGetValue($"Files{cSet}", out var filesSerialized) && filesSerialized != null)
                        {
                            sourceFiles = MessagePackSerializer.Deserialize<List<AssetSource>>((byte[])filesSerialized);
                        }
                        else
                        {
                            Logger.LogDebug($"No sourceFiles found in extended data for clothing slot {cSet}");
                            continue;
                        }
                        Logger.LogDebug($"{sourceFiles.Count} sourceFiles found, extracting to cache...");
                        foreach (AssetSource sourceFile in sourceFiles)
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

                            }
                            catch (Exception ex)
                            {
                                Logger.LogError("Error extracting file from scene: " + ex.Message);
                            }
                        }
                        sourceFiles.Clear(); // force garbage colleciton
                    }
                    else if (version == 3)
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
                    if (data.data.TryGetValue($"Assets{cSet}", out var assetsSerialized) && assetsSerialized != null)
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
                        Logger.LogDebug($"No assets found in extended data for clothing slot {cSet}");
                        continue;
                    }
                    foreach (Asset asset in assets)
                    {
                        if (!loadedObjects.ContainsKey(cSet)) loadedObjects[cSet] = new Dictionary<int, Asset>();
                        loadedObjects[cSet][asset.identifier] = asset;
                    }
                }
            }

        }

        protected override void OnCoordinateBeingSaved(ChaFileCoordinate coordinate)
        {
            PluginData data = new PluginData();

            List<Asset> assets = new List<Asset>();
            List<AssetFile> sourceFiles = new List<AssetFile>();
            List<String> alreadySavedFiles = new List<string>();

            if (!loadedObjects.ContainsKey(ChaControl.fileStatus.coordinateType)) return;
            foreach(int slot in loadedObjects[ChaControl.fileStatus.coordinateType].Keys)
            {
                Asset asset = loadedObjects[ChaControl.fileStatus.coordinateType][slot];
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

            SetCoordinateExtendedData(coordinate, data);
            Logger.LogDebug("Set Coordinate Extended data");
        }

        private void LoadCoordinateCompatibilityDynamicBoneEditor(int cSet)
        {
            foreach (KK_Plugins.DynamicBoneEditor.DynamicBoneData dboneData in (List<KK_Plugins.DynamicBoneEditor.DynamicBoneData>)DBoneBackup)
            {
                if (dboneData.CoordinateIndex == cSet)
                {
                    KK_Plugins.DynamicBoneEditor.CharaController dBoneController = ChaControl.gameObject.GetComponentInChildren<KK_Plugins.DynamicBoneEditor.CharaController>();
                    if (dBoneController != null && !dBoneController.AccessoryDynamicBoneData.Any(entry => entry.CoordinateIndex.Equals(dboneData.CoordinateIndex)
                        && entry.Slot.Equals(dboneData.Slot)
                        && entry.BoneName.Equals(dboneData.BoneName)))
                    {
                        Logger.LogDebug($"Compatibility Mode: Added back DynamicBoneEditor data for slot {dboneData.Slot}: {dboneData.BoneName}");
                        dBoneController.AccessoryDynamicBoneData.Add(dboneData);
                    }
                }
            }
        }

        internal void LoadCoordinate(ChaFileCoordinate coordinate)
        {
            int cSet = ChaControl.fileStatus.coordinateType;

            // CoordinateLoadOption compatibilty
            // check if Coordinate Load Option is installed
            bool cMode = false;
            List<int> cloImportAccessories = new List<int>(); // slots that are loaded new
            if (Chainloader.PluginInfos.ContainsKey("com.jim60105.kks.coordinateloadoption"))
            {
                Logger.LogDebug("Coordinate Load Option dedected");
                if (GameObject.Find("CoordinateTooglePanel")?.activeInHierarchy == true)
                {
                    Logger.LogDebug("Coordinate Load Option enabled");
                    bool? accEnabled = GameObject.Find("CoordinateTooglePanel/accessories")?.GetComponent<Toggle>()?.isOn;
                    if (accEnabled == true)
                    {
                        Logger.LogDebug("Coordinate Load Option accessory load enabled, entering compatibility mode");
                        cMode = true;

                        if (GameObject.Find("CoordinateTooglePanel/AccessoriesTooglePanel/BtnChangeAccLoadMode")?.GetComponentInChildren<Text>()?.text != "Replace Mode")
                        {
                            Logger.LogMessage("Asset Import WARNING: Add Mode is not supported! Stopping asset load");
                            return;
                        }

                        GameObject list = GameObject.Find("CoordinateTooglePanel/AccessoriesTooglePanel/scroll/Viewport/Content");
                        for(int i = 0; i < list.transform.childCount; i++)
                        {
                            GameObject item = list.transform.GetChild(i).gameObject;
                            bool? isOn = item.GetComponent<Toggle>()?.isOn;
                            bool isEmpty = item.transform.Find("Label")?.gameObject.GetComponent<Text>()?.text == "Empty";

                            if (isOn == true && !isEmpty)
                            {
                                cloImportAccessories.Add(i);
                            }
                        }

                    }
                    else if (accEnabled == false)
                    {
                        Logger.LogDebug("Coordinate Load Option accessory load disabled -> stopping asset load.");
                        if (Chainloader.PluginInfos.ContainsKey("com.deathweasel.bepinex.dynamicboneeditor")) LoadCoordinateCompatibilityDynamicBoneEditor(cSet);
                        return;
                    }
                }
            }

            // Maker partial coordinate load fix
            if (KKAPI.Maker.MakerAPI.InsideAndLoaded)
            {
                // return if no new accessories are being loaded
                if (GameObject.Find("cosFileControl")?.GetComponentInChildren<ChaCustom.CustomFileWindow>()?.tglCoordeLoadAcs.isOn == false) return;
            }

            coordinateCardNames[cSet] = coordinate.coordinateFileName.Replace(".png", "");

            Logger.LogDebug($"Coordinate Load Started {cSet} on {ChaControl.fileParam.fullname}");
            if (loadedObjects.ContainsKey(cSet) && !cMode)
            {
                loadedObjects.Remove(cSet);
            }
            // free slots for loaded accessories while keeping those that should persist
            else if (loadedObjects.ContainsKey(cSet))
            {
                foreach(int slot in cloImportAccessories)
                {
                    if (loadedObjects[cSet].ContainsKey(slot))
                    {
                        loadedObjects[cSet].Remove(slot);
                    }
                }
            }

            
            PluginData data = null;
            if (cMode) // grab transfer plugindata if exists
            {
                if (cloTransferPluginData != null) data = cloTransferPluginData;
                else
                {
                    data = cloTransferPluginData = GetCoordinateExtendedData(coordinate);
                    if (data != null)
                    {
                        // remove transfer plugindata after load is finished; Coroutine cannot be started on *this* as it's being destroyed by clo too early
                        Main.instance.StartCoroutine(removeCloTransferPluginData());
                    }
                }
            }
            else data = GetCoordinateExtendedData(coordinate);

            if (data == null) return;
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
                    foreach (AssetSource sourceFile in sourceFiles)
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

                        }
                        catch (Exception ex)
                        {
                            Logger.LogError("Error extracting file from scene: " + ex.Message);
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

                // hasnt changed in verion 3
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
                    if (!loadedObjects.ContainsKey(cSet)) loadedObjects[cSet] = new Dictionary<int, Asset>();

                    // ignore Asset Data of accessories that are not being loaded
                    if (cMode && !cloImportAccessories.Contains(asset.identifier))
                    {
                        Logger.LogDebug($"Compatibilty Mode: Accessory in slot {asset.identifier} is not being loaded -> discarding data.");
                        continue;
                    }

                    loadedObjects[cSet][asset.identifier] = asset;
                    Logger.LogDebug($"Loading dat of asset for accessory in slot {asset.identifier}");
                }
            }

            // dynamic bone editor 
            if (cMode && Chainloader.PluginInfos.ContainsKey("com.deathweasel.bepinex.dynamicboneeditor"))
            {
                CoordinateLoadOptionDynamicBoneEditor(cSet, cloImportAccessories);
            }
        }

        private void CoordinateLoadOptionDynamicBoneEditor(int cSet, List<int> cloImportAccessories)
        {
            foreach (KK_Plugins.DynamicBoneEditor.DynamicBoneData dboneData in (List<KK_Plugins.DynamicBoneEditor.DynamicBoneData>)DBoneBackup)
            {
                if (dboneData.CoordinateIndex == cSet && !cloImportAccessories.Contains(dboneData.Slot))
                {
                    KK_Plugins.DynamicBoneEditor.CharaController dBoneController = ChaControl.gameObject.GetComponentInChildren<KK_Plugins.DynamicBoneEditor.CharaController>();
                    if (dBoneController != null && !dBoneController.AccessoryDynamicBoneData.Contains(dboneData))
                    {
                        Logger.LogInfo($"Compatibility Mode: Added back DynamicBoneEditor data for {dboneData.Slot}");
                        dBoneController.AccessoryDynamicBoneData.Add(dboneData);
                    }
                }
            }
        }

        private object DBoneBackup;

        internal void LoadData()
        {
            if (hasBeenLoadedAlready) return;
            Logger.LogDebug("Reloading Assets");
            int cSet = ChaControl.fileStatus.coordinateType;
            if (!loadedObjects.ContainsKey(cSet)) return;
            foreach(int slot in loadedObjects[cSet].Keys)
            {
                Asset asset = loadedObjects[cSet][slot];
                // import asset
                AccessoryHelper accessory = new AccessoryHelper(ChaControl, ChaControl.GetAccessoryComponent(asset.identifier), asset.identifier);
                if (accessory.accessory == null)
                {
                    Logger.LogWarning($"Accessory in slot {asset.identifier} was null");
                    continue;
                }

                if (Main.dumpAssets.Value)
                {
                    File.WriteAllBytes(Path.Combine(getDumpPath(cSet), RAMCacheUtility.GetFileName(asset.sourceFile)), RAMCacheUtility.GetFileBlob(asset.sourceFile));
                }

                Import import = new Import(
                    asset.sourceFile,
                    1, // remove later
                    asset.hasBones,
                    Instantiate(accessory.accessory.gameObject.GetComponentInChildren<Renderer>().material),
                    asset.doFbxTranslation,
                    asset.perRendererMaterials);
                import.Load();
                if (import == null || !import.isLoaded)
                {
                    Logger.LogError($"Loading {asset.sourceFile} from cache failed");
                    continue;
                }
                foreach (int idx in asset.dynamicBoneIndices)
                {
                    import.boneNodes[idx].setDynamic(true);
                }
                LoadProcess loadProcess = new LoadProcess(
                    accessory.accessory.gameObject,
                    accessory,
                    import,
                    new Vector3(asset.scale[0], asset.scale[1], asset.scale[2]),
                    LoadProcess.loadProcessKind.LOAD);

                FinishLoadProcess(loadProcess);
            }

            Singleton<HSPE.MainWindow>.Instance?.OnCharaLoad(ChaControl.chaFile); // Force KKSPE update
            ChaControl.gameObject.GetComponentInChildren<PoseController>()?._dynamicBonesEditor?.RefreshDynamicBoneList(); // Force KKSPE update DynamicBoneList
            if (Chainloader.PluginInfos.ContainsKey("com.deathweasel.bepinex.dynamicboneeditor")) DynamicBoneEditorBackup();

            BoneController boneController = ChaControl.gameObject.GetComponentInChildren<BoneController>();
            if (boneController != null) boneController.NeedsFullRefresh = true;
            
            hasBeenLoadedAlready = true;
            this.StartCoroutine(resetLoadedAlready());
        }

        private void DynamicBoneEditorBackup()
        {
            if (DBoneBackup == null) DBoneBackup = new List<KK_Plugins.DynamicBoneEditor.DynamicBoneData>();
            if (ChaControl.gameObject.GetComponentInChildren<KK_Plugins.DynamicBoneEditor.CharaController>() != null && DBoneBackup is List<KK_Plugins.DynamicBoneEditor.DynamicBoneData> backup)
            {
                ChaControl.StartCoroutine(ChaControl.gameObject.GetComponentInChildren<KK_Plugins.DynamicBoneEditor.CharaController>()?.ApplyData());
                // backup current dynamic bone editor data for potential coordinate load option load
                backup.AddRange(ChaControl.gameObject.GetComponentInChildren<KK_Plugins.DynamicBoneEditor.CharaController>().AccessoryDynamicBoneData);
            }
        }

        private void renameAccessory(int slot, string name)
        {
            ListInfoComponent infoComponent = ChaControl.GetAccessoryComponent(slot).gameObject.GetComponent<ListInfoComponent>();
            if (infoComponent == null || ChaControl.infoAccessory.IsNullOrEmpty()) return;
            ListInfoBase listInfoBase = ChaControl.infoAccessory[slot].Clone();
            listInfoBase.dictInfo[41] = name;
            infoComponent.data = listInfoBase;
            ChaControl.infoAccessory[slot] = listInfoBase;
        }

        public void Import(int slot, int type, string parent, string path, Vector3 scale, bool armature, bool perRendererMaterials, bool doFbxTranslation)
        {
            // unify path structure
            path = path.Replace("\"", "");
            path = path.Replace("\\", "/");

            Logger.LogDebug($"Accessory Import started on slot {slot} with type [{type}] and parentNode [{parent}]");

            ChaAccessoryComponent accessory = ChaControl.GetAccessoryComponent(slot);
            if (accessory == null)
            {
                Logger.LogMessage("Please specify a Type and Parent by choosing a normal accessory first.");
                return;
            }

            Logger.LogInfo($"Importing [{path}] started...");

            GameObject _base = accessory.gameObject;
            Material _baseMaterial = Instantiate(_base.GetComponentInChildren<Renderer>().material);

            string identifierHash = RAMCacheUtility.ToCache(path);

            if (Main.dumpAssets.Value)
            {
                File.WriteAllBytes(Path.Combine(getDumpPath(), Path.GetFileName(path)), RAMCacheUtility.GetFileBlob(identifierHash));
            }

            Import import = new Import(
                identifierHash,
                1, // remove later
                armature, 
                _baseMaterial, 
                doFbxTranslation, 
                perRendererMaterials
            );
            import.Load();
            if (!import.isLoaded) return;

            AccessoryHelper helper = new AccessoryHelper(ChaControl, accessory, slot);
            Main.currentLoadProcess = new LoadProcess(_base, helper, import, scale, LoadProcess.loadProcessKind.NORMAL);
            AssetUI.armatureMode = Main.currentLoadProcess.import.hasBones;
            AssetUI.commonPathText = import.commonPath;
            AssetUI.preloadUI = true;
        }

        internal void FinishLoadProcess(LoadProcess loadProcess)
        {
            AssetUI.preloadUI = false;

            Import import = loadProcess.import;
            GameObject _base = loadProcess._base;
            if (!(loadProcess.component is AccessoryHelper accessory)) return;

            for (int i = _base.transform.FindLoop("N_move").transform.childCount; i > 0; i--)
            {
                DestroyImmediate(_base.transform.FindLoop("N_move").transform.GetChild(i - 1).gameObject);
            }

            // destory secondary accessory object if it exists
            int siblingIndex = _base.transform.FindLoop("N_move").transform.parent.GetSiblingIndex();
            for (int i = _base.transform.childCount-1; i >= 0; i--)
            {
                if (i == siblingIndex) continue;
                DestroyImmediate(_base.transform.GetChild(i).gameObject);
                Singleton<ChaCustom.CvsAccessory>.Instance?.UpdateCustomUI();
            }

            Vector3 scale = accessory.addMove[0, 2];

            // set imported gameObject as child of base
            GameObject _object = import.gameObject;
            _object.transform.parent = _base.transform.FindLoop("N_move").transform;
            _object.transform.localPosition = Vector3.zero;
            _object.transform.localRotation = Quaternion.identity;
            _object.transform.localScale = Vector3.one;

            // renderers
            accessory.rendNormal = import.renderers.ToArray();
            accessory.rendAlpha = new Renderer[0];

            // name (experimental, does not yet behave as intended)
            renameAccessory(accessory.slot, import.sourceFileName);
            Singleton<ChaCustom.CustomAcsChangeSlot>.Instance?.UpdateSlotNames();

            // remove dynamic bones that might exist
            foreach (DynamicBone bone in _base.GetComponents<DynamicBone>())
            {
                DestroyImmediate(bone);
            }

            // dynamic bones
            if (import.hasBones)
            {
                int dBones = 0;
                foreach (BoneNode node in import.boneNodes)
                {
                    if (node.isDynamicRoot)
                    {
                        DynamicBone dBone = _base.AddComponent<DynamicBone>();
                        dBone.enabled = false;
                        dBone.m_Root = node.gameObject.transform;
                        dBone.m_notRolls = new List<Transform>();
                        dBone.m_Colliders = new List<DynamicBoneCollider>();
                        dBone.enabled = true;
                        dBones++;
                    }
                }
                if (dBones > 0)
                {
                    Logger.LogDebug($"Activated {dBones} dynamic bone chains on {loadProcess.import.sourceFileName}");
                }
            }

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
                        Singleton<KK_Plugins.MaterialEditor.MaterialEditorCharaController>.Instance.SetMaterialTexture(
                            accessory.slot,
                            KK_Plugins.MaterialEditor.MaterialEditorCharaController.ObjectType.Accessory,
                            p.material,
                            prop,
                            data,
                            _object);
                        Logger.LogDebug($"Set Texture on {p.material.name} - {prop}");
                    }
                }
            }

            Vector3 s = _object.transform.localScale;
            _object.transform.localScale = new Vector3(s.x * loadProcess.scale.x, s.y * loadProcess.scale.y, s.z * loadProcess.scale.z);
            _object.transform.localRotation *= Quaternion.Euler(new Vector3(0, 180, 0));

            if (loadProcess.kind == LoadProcess.loadProcessKind.NORMAL)
            {
                Asset asset = new Asset();
                asset.sourceFile = loadProcess.import.sourceIdentifier;
                asset.dynamicBoneIndices = new List<int>();
                for (int i = 0; i < import.boneNodes.Count; i++)
                {
                    BoneNode node = import.boneNodes[i];
                    if (node.isDynamicRoot) asset.dynamicBoneIndices.Add(i);
                }
                asset.identifier = accessory.slot;
                asset.scale = new float[] { loadProcess.scale.x, loadProcess.scale.y, loadProcess.scale.z };
                asset.hasBones = loadProcess.import.hasBones;
                asset.perRendererMaterials = loadProcess.import.perRendererMaterials;
                asset.doFbxTranslation = loadProcess.import.doFbxTranslation;

                if (!loadedObjects.ContainsKey(ChaControl.fileStatus.coordinateType)) loadedObjects[ChaControl.fileStatus.coordinateType] = new Dictionary<int, Asset>();
                loadedObjects[ChaControl.fileStatus.coordinateType][asset.identifier] = asset;
            }

            ChaControl.ChangeAccessoryColor(accessory.slot);

            Logger.LogInfo($"Asset [{loadProcess.import.sourceFileName}] was loaded successfully");
        }
    }
}
