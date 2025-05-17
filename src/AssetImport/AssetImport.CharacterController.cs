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
    /// Handles save/load and all kinds of reload events for Accessories.
    /// </summary>
    class AssetCharaController : CharaCustomFunctionController
    {
        private readonly Dictionary<int, Dictionary<int, Asset>> loadedObjects = new Dictionary<int, Dictionary<int, Asset>>();
        
        private readonly ManualLogSource Logger = Main.Logger;

        internal readonly Dictionary<int, string> CoordinateCardNames = new Dictionary<int, string>();
        internal string CharacterCardName { get => ChaControl.chaFile.charaFileName.IsNullOrEmpty() ? "MakerDefault" : ChaControl.chaFile.charaFileName; }

        private bool _hasBeenLoadedAlready;

        // used to transfer plugin data from Coordinate Load Options temp character to the real characters.
        private static PluginData cloTransferPluginData;

        private static IEnumerator RemoveCloTransferPluginData()
        {
            yield return null;
            yield return null;
            cloTransferPluginData = null;
        }

        private IEnumerator ResetLoadedAlready()
        {
            yield return null;
            yield return null;
            yield return null;
            yield return null;
            yield return null;
            yield return null;
            yield return null;
            _hasBeenLoadedAlready = false;
        }

        protected override void Start()
        {
            base.Start();
        }

        public string GetDumpPath()
        {
            return GetDumpPath(ChaControl.fileStatus.coordinateType);
        }

        public string GetDumpPath(int clothingSlot)
        {
            string cha = Regex.Replace(CharacterCardName, @"[^a-zA-Z0-9\-_]", "",RegexOptions.Compiled);
            var c = $"{cha}/{clothingSlot}";
            if (CoordinateCardNames.TryGetValue(clothingSlot, out string cardName)) c = cardName;

            if (!Directory.Exists($"./UserData/AssetImport/Cache/{c}"))
            {
                Directory.CreateDirectory($"./UserData/AssetImport/Cache/{c}");
            }
            return $"./UserData/AssetImport/Cache/{c}/";
        }

        // accessory events

        internal void AccessoryChangeEvent(int slot)
        {
            if (!loadedObjects.ContainsKey(ChaControl.fileStatus.coordinateType)) return;
            if (loadedObjects[ChaControl.fileStatus.coordinateType].ContainsKey(slot))
            {
                loadedObjects[ChaControl.fileStatus.coordinateType].Remove(slot);
            }
        }

        internal void AccessoryTransferedEvent(int source, int destination)
        {
            int cSet = ChaControl.fileStatus.coordinateType;
            if (!loadedObjects.ContainsKey(cSet)) return;
            Logger.LogDebug("Accessory Transfer Event");
            if (loadedObjects[cSet].ContainsKey(source))
            {
                Asset asset = new Asset
                {
                    SourceFile = loadedObjects[cSet][source].SourceFile,
                    DynamicBoneIndices = loadedObjects[cSet][source].DynamicBoneIndices,
                    Identifier = destination,
                    Scale = loadedObjects[cSet][source].Scale,
                    HasBones = loadedObjects[cSet][source].HasBones,
                    PerRendererMaterials = loadedObjects[cSet][source].PerRendererMaterials,
                    DoFbxTranslation = loadedObjects[cSet][source].DoFbxTranslation
                };
                loadedObjects[cSet][destination] = asset;
                Logger.LogDebug($"Source slot {source} --> Destination slot {destination}");
            }
            else if (loadedObjects[cSet].ContainsKey(destination))
            {
                loadedObjects[cSet].Remove(destination);
            }
        }

        internal void AccessoryCopiedEvent(int source, int destination, IEnumerable<int> slots)
        {
            if (!loadedObjects.ContainsKey(source))
            {
                if (!loadedObjects.ContainsKey(destination)) return;
                foreach(int slot in slots)
                {
                    if (loadedObjects[destination].ContainsKey(slot))
                    {
                        loadedObjects[destination].Remove(slot);
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
                    asset.SourceFile = loadedObjects[source][slot].SourceFile;
                    asset.DynamicBoneIndices = loadedObjects[source][slot].DynamicBoneIndices;
                    asset.Identifier = slot;
                    asset.Scale = loadedObjects[source][slot].Scale;
                    asset.HasBones = loadedObjects[source][slot].HasBones;
                    asset.PerRendererMaterials = loadedObjects[source][slot].PerRendererMaterials;
                    asset.DoFbxTranslation = loadedObjects[source][slot].DoFbxTranslation;
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

            var sourceFiles = new List<AssetFile>();
            var alreadySavedFiles = new List<string>();

            foreach(int cSet in loadedObjects.Keys)
            {
                var assets = new List<Asset>();

                if (!loadedObjects.ContainsKey(cSet)) continue;
                foreach (Asset asset in loadedObjects[cSet].Keys.Select(slot => loadedObjects[cSet][slot]))
                {
                    if (!alreadySavedFiles.Contains(asset.SourceFile))
                    {
                        // base file
                        AssetFile sFile = new AssetFile();
                        if (sFile.AutoFill(asset.SourceFile))
                        {
                            alreadySavedFiles.Add(asset.SourceFile);
                            sourceFiles.Add(sFile);
                        }
                        List<string> additionalFiles = RamCacheUtility.GetFileAdditionalFileHashes(asset.SourceFile);

                        // additonal files (for .gltf)
                        if (!additionalFiles.IsNullOrEmpty())
                        {
                            additionalFiles.ForEach(file =>
                            {
                                AssetFile sFile2 = new AssetFile();
                                if (!sFile2.AutoFill(file)) return;
                                alreadySavedFiles.Add(file);
                                sourceFiles.Add(sFile2);
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
                RamCacheUtility.ClearCache();
                GameObject toggleObject = GameObject.Find("CustomScene/CustomRoot/FrontUIGroup/CustomUIGroup/CvsMenuTree/06_SystemTop/charaFileControl/charaFileWindow/WinRect/CharaLoad/Select/tglItem05");
                if (toggleObject && toggleObject.GetComponent<Toggle>())
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
                    var filenameToHash = new Dictionary<string, string>();
                    
                    switch (version)
                    {
                        case 2:
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

                                }
                                catch (Exception ex)
                                {
                                    Logger.LogError("Error extracting file from scene: " + ex.Message);
                                }
                            }
                            sourceFiles.Clear(); // force garbage colleciton
                            break;
                        }
                        case 3:
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
                    if (data.data.TryGetValue($"Assets{cSet}", out object assetsSerialized) && assetsSerialized != null)
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
                        Logger.LogDebug($"No assets found in extended data for clothing slot {cSet}");
                        continue;
                    }
                    foreach (Asset asset in assets)
                    {
                        if (!loadedObjects.ContainsKey(cSet)) loadedObjects[cSet] = new Dictionary<int, Asset>();
                        loadedObjects[cSet][asset.Identifier] = asset;
                    }
                }
            }

        }

        protected override void OnCoordinateBeingSaved(ChaFileCoordinate coordinate)
        {
            PluginData data = new PluginData();

            var assets = new List<Asset>();
            var sourceFiles = new List<AssetFile>();
            var alreadySavedFiles = new List<string>();

            if (!loadedObjects.ContainsKey(ChaControl.fileStatus.coordinateType)) return;
            foreach (Asset asset in loadedObjects[ChaControl.fileStatus.coordinateType].Keys.Select(slot => loadedObjects[ChaControl.fileStatus.coordinateType][slot]))
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

            SetCoordinateExtendedData(coordinate, data);
            Logger.LogDebug("Set Coordinate Extended data");
        }

        private void LoadCoordinateCompatibilityDynamicBoneEditor(int cSet)
        {
            foreach (KK_Plugins.DynamicBoneEditor.DynamicBoneData dboneData in (List<KK_Plugins.DynamicBoneEditor.DynamicBoneData>)_dBoneBackup)
            {
                if (dboneData.CoordinateIndex != cSet) continue;
                KK_Plugins.DynamicBoneEditor.CharaController dBoneController = ChaControl.gameObject.GetComponentInChildren<KK_Plugins.DynamicBoneEditor.CharaController>();
                if (!dBoneController || dBoneController.AccessoryDynamicBoneData.Any(entry =>
                        entry.CoordinateIndex.Equals(dboneData.CoordinateIndex)
                        && entry.Slot.Equals(dboneData.Slot)
                        && entry.BoneName.Equals(dboneData.BoneName))) continue;
                Logger.LogDebug($"Compatibility Mode: Added back DynamicBoneEditor data for slot {dboneData.Slot}: {dboneData.BoneName}");
                dBoneController.AccessoryDynamicBoneData.Add(dboneData);
            }
        }

        internal void LoadCoordinate(ChaFileCoordinate coordinate)
        {
            int cSet = ChaControl.fileStatus.coordinateType;

            // CoordinateLoadOption compatibilty
            // check if Coordinate Load Option is installed
            var cMode = false;
            var cloImportAccessories = new List<int>(); // slots that are loaded new
            if (Chainloader.PluginInfos.ContainsKey("com.jim60105.kks.coordinateloadoption"))
            {
                Logger.LogDebug("Coordinate Load Option deducted");
                if (GameObject.Find("CoordinateTooglePanel")?.activeInHierarchy == true)
                {
                    Logger.LogDebug("Coordinate Load Option enabled");
                    bool? accEnabled = GameObject.Find("CoordinateTooglePanel/accessories")?.GetComponent<Toggle>()?.isOn;
                    switch (accEnabled)
                    {
                        case true:
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

                            break;
                        }
                        case false:
                        {
                            Logger.LogDebug("Coordinate Load Option accessory load disabled -> stopping asset load.");
                            if (Chainloader.PluginInfos.ContainsKey("com.deathweasel.bepinex.dynamicboneeditor")) LoadCoordinateCompatibilityDynamicBoneEditor(cSet);
                            return;
                        }
                    }
                }
            }

            // Maker partial coordinate load fix
            if (KKAPI.Maker.MakerAPI.InsideAndLoaded)
            {
                // return if no new accessories are being loaded
                if (GameObject.Find("cosFileControl")?.GetComponentInChildren<ChaCustom.CustomFileWindow>()?.tglCoordeLoadAcs.isOn == false) return;
            }

            CoordinateCardNames[cSet] = coordinate.coordinateFileName.Replace(".png", "");

            Logger.LogDebug($"Coordinate Load Started {cSet} on {ChaControl.fileParam.fullname}");
            if (loadedObjects.ContainsKey(cSet) && !cMode)
            {
                loadedObjects.Remove(cSet);
            }
            // free slots for loaded accessories while keeping those that should persist
            else if (loadedObjects.ContainsKey(cSet))
            {
                foreach (int slot in cloImportAccessories.Where(slot => loadedObjects[cSet].ContainsKey(slot)))
                {
                    loadedObjects[cSet].Remove(slot);
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
                        Main.instance.StartCoroutine(RemoveCloTransferPluginData());
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
            var filenameToHash = new Dictionary<string, string>();

            if (version == 2 || version == 3)
            {
                switch (version)
                {
                    // old AssetSource format
                    case 2:
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

                            }
                            catch (Exception ex)
                            {
                                Logger.LogError("Error extracting file from scene: " + ex.Message);
                            }
                        }
                        sourceFiles.Clear(); // force garbage colleciton
                        break;
                    }
                    // new AssetFile format
                    case 3:
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
                    if (!loadedObjects.ContainsKey(cSet)) loadedObjects[cSet] = new Dictionary<int, Asset>();

                    // ignore Asset Data of accessories that are not being loaded
                    if (cMode && !cloImportAccessories.Contains(asset.Identifier))
                    {
                        Logger.LogDebug($"Compatibilty Mode: Accessory in slot {asset.Identifier} is not being loaded -> discarding data.");
                        continue;
                    }

                    loadedObjects[cSet][asset.Identifier] = asset;
                    Logger.LogDebug($"Loading dat of asset for accessory in slot {asset.Identifier}");
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
            foreach (KK_Plugins.DynamicBoneEditor.DynamicBoneData dboneData in (List<KK_Plugins.DynamicBoneEditor.DynamicBoneData>)_dBoneBackup)
            {
                if (dboneData.CoordinateIndex != cSet || cloImportAccessories.Contains(dboneData.Slot)) continue;
                KK_Plugins.DynamicBoneEditor.CharaController dBoneController = ChaControl.gameObject.GetComponentInChildren<KK_Plugins.DynamicBoneEditor.CharaController>();
                if (dBoneController == null || dBoneController.AccessoryDynamicBoneData.Contains(dboneData)) continue;
                Logger.LogInfo($"Compatibility Mode: Added back DynamicBoneEditor data for {dboneData.Slot}");
                dBoneController.AccessoryDynamicBoneData.Add(dboneData);
            }
        }

        private object _dBoneBackup;

        internal void LoadData()
        {
            if (_hasBeenLoadedAlready) return;
            Logger.LogDebug("Reloading Assets");
            int cSet = ChaControl.fileStatus.coordinateType;
            if (!loadedObjects.ContainsKey(cSet)) return;
            foreach(int slot in loadedObjects[cSet].Keys)
            {
                Asset asset = loadedObjects[cSet][slot];
                // import asset
                AccessoryHelper accessory = new AccessoryHelper(ChaControl, ChaControl.GetAccessoryComponent(asset.Identifier), asset.Identifier);
                if (accessory.Accessory == null)
                {
                    Logger.LogWarning($"Accessory in slot {asset.Identifier} was null");
                    continue;
                }

                if (Main.dumpAssets.Value)
                {
                    File.WriteAllBytes(Path.Combine(GetDumpPath(cSet), RamCacheUtility.GetFileName(asset.SourceFile)), RamCacheUtility.GetFileBlob(asset.SourceFile));
                }

                Import import = new Import(
                    asset.SourceFile,
                    asset.HasBones,
                    Instantiate(accessory.Accessory.gameObject.GetComponentInChildren<Renderer>().material),
                    asset.DoFbxTranslation,
                    asset.PerRendererMaterials);
                import.Load();
                if (!import.IsLoaded)
                {
                    Logger.LogError($"Loading {asset.SourceFile} from cache failed");
                    continue;
                }
                foreach (int idx in asset.DynamicBoneIndices)
                {
                    import.BoneNodes[idx].SetDynamic(true);
                }
                LoadProcess loadProcess = new LoadProcess(
                    accessory.Accessory.gameObject,
                    accessory,
                    import,
                    new Vector3(asset.Scale[0], asset.Scale[1], asset.Scale[2]),
                    LoadProcess.LoadProcessKind.Load);

                FinishLoadProcess(loadProcess);
            }

            Singleton<HSPE.MainWindow>.Instance?.OnCharaLoad(ChaControl.chaFile); // Force KKSPE update
            ChaControl.gameObject.GetComponentInChildren<PoseController>()?._dynamicBonesEditor?.RefreshDynamicBoneList(); // Force KKSPE update DynamicBoneList
            if (Chainloader.PluginInfos.ContainsKey("com.deathweasel.bepinex.dynamicboneeditor")) DynamicBoneEditorBackup();

            BoneController boneController = ChaControl.gameObject.GetComponentInChildren<BoneController>();
            if (boneController) boneController.NeedsFullRefresh = true;
            
            _hasBeenLoadedAlready = true;
            this.StartCoroutine(ResetLoadedAlready());
        }

        private void DynamicBoneEditorBackup()
        {
            if (_dBoneBackup == null) _dBoneBackup = new List<KK_Plugins.DynamicBoneEditor.DynamicBoneData>();
            if (!ChaControl.gameObject.GetComponentInChildren<KK_Plugins.DynamicBoneEditor.CharaController>() ||
                !(_dBoneBackup is List<KK_Plugins.DynamicBoneEditor.DynamicBoneData> backup)) return;
            ChaControl.StartCoroutine(ChaControl.gameObject.GetComponentInChildren<KK_Plugins.DynamicBoneEditor.CharaController>()?.ApplyData());
            // backup current dynamic bone editor data for potential coordinate load option load
            backup.AddRange(ChaControl.gameObject.GetComponentInChildren<KK_Plugins.DynamicBoneEditor.CharaController>().AccessoryDynamicBoneData);
        }

        private void RenameAccessory(int slot, string newName)
        {
            ListInfoComponent infoComponent = ChaControl.GetAccessoryComponent(slot).gameObject.GetComponent<ListInfoComponent>();
            if (!infoComponent || ChaControl.infoAccessory.IsNullOrEmpty()) return;
            ListInfoBase listInfoBase = ChaControl.infoAccessory[slot].Clone();
            listInfoBase.dictInfo[41] = newName;
            infoComponent.data = listInfoBase;
            ChaControl.infoAccessory[slot] = listInfoBase;
        }

        public void Import(int slot, int type, string parent, string path, Vector3 scale, bool armature, bool perRendererMaterials, bool doFbxTranslation,bool loadBlendshapes)
        {
            // unify path structure
            path = path.Replace("\"", "");
            path = path.Replace("\\", "/");
            
            if (!(File.Exists(path)))
            {
                Logger.LogMessage($"File {path} does not exist");
                return;
            }

            Logger.LogDebug($"Accessory Import started on slot {slot} with type [{type}] and parentNode [{parent}]");

            ChaAccessoryComponent accessory = ChaControl.GetAccessoryComponent(slot);
            if (!accessory)
            {
                Logger.LogMessage("Please specify a Type and Parent by choosing a normal accessory first.");
                return;
            }

            Logger.LogInfo($"Importing [{path}] started...");

            GameObject baseObject = accessory.gameObject;
            Material baseMaterial = Instantiate(baseObject.GetComponentInChildren<Renderer>().material);

            string identifierHash = RamCacheUtility.ToCache(path);

            if (Main.dumpAssets.Value)
            {
                File.WriteAllBytes(Path.Combine(GetDumpPath(), Path.GetFileName(path)), RamCacheUtility.GetFileBlob(identifierHash));
            }

            Import import = new Import(
                identifierHash,
                armature, 
                baseMaterial, 
                doFbxTranslation, 
                perRendererMaterials,
                loadBlendshapes
            );
            import.Load();
            if (!import.IsLoaded) return;

            AccessoryHelper helper = new AccessoryHelper(ChaControl, accessory, slot);
            Main.currentLoadProcess = new LoadProcess(baseObject, helper, import, scale, LoadProcess.LoadProcessKind.Normal);
            AssetUI.ArmatureMode = Main.currentLoadProcess.Import.HasBones;
            AssetUI.CommonPathText = import.CommonPath;
            AssetUI.PreloadUI = true;
        }

        internal void FinishLoadProcess(LoadProcess loadProcess)
        {
            AssetUI.PreloadUI = false;

            Import import = loadProcess.Import;
            GameObject loadProcessBase = loadProcess.BaseGameObject;
            if (!(loadProcess.Component is AccessoryHelper accessory)) return;

            GameObject n_move = loadProcessBase.transform.FindLoop("N_move");

            for (int i = n_move.transform.childCount; i > 0; i--)
            {
                DestroyImmediate(n_move.transform.GetChild(i - 1).gameObject);
            }

            // destroy secondary accessory object if it exists
            if (loadProcessBase.transform.childCount > 1)
            {
                n_move.transform.SetParent(null); // detach n_move
                for (int j = loadProcessBase.transform.childCount; j > 0; j--) // remove all children
                {
                    DestroyImmediate(loadProcessBase.transform.GetChild(j - 1).gameObject);
                }
                n_move.transform.SetParent(loadProcessBase.transform); // reattach n_move
            }

            if (KKAPI.Maker.MakerAPI.InsideMaker && KKAPI.Maker.AccessoriesApi.SelectedMakerAccSlot >= 0) 
                Singleton<ChaCustom.CustomAcsChangeSlot>.Instance?.cvsAccessory[KKAPI.Maker.AccessoriesApi.SelectedMakerAccSlot]?.UpdateCustomUI();

            Vector3 scale = accessory.AddMove[0, 2];

            // set imported gameObject as child of base
            GameObject importObject = import.GameObject;
            importObject.transform.parent = n_move.transform;
            importObject.transform.localPosition = Vector3.zero;
            importObject.transform.localRotation = Quaternion.identity;
            importObject.transform.localScale = Vector3.one;

            // renderers
            accessory.RendNormal = import.Renderers.ToArray();
            accessory.RendAlpha = Array.Empty<Renderer>();

            // name (experimental, does not yet behave as intended)
            RenameAccessory(accessory.Slot, import.SourceFileName);
            Singleton<ChaCustom.CustomAcsChangeSlot>.Instance?.UpdateSlotNames();

            // remove dynamic bones that might exist
            foreach (DynamicBone bone in loadProcessBase.GetComponents<DynamicBone>())
            {
                DestroyImmediate(bone);
            }

            // dynamic bones
            if (import.HasBones)
            {
                var dBones = 0;
                foreach (BoneNode node in import.BoneNodes)
                {
                    if (!node.IsDynamicRoot) continue;
                    DynamicBone dBone = loadProcessBase.AddComponent<DynamicBone>();
                    dBone.enabled = false;
                    dBone.m_Root = node.GameObject.transform;
                    dBone.m_notRolls = new List<Transform>();
                    dBone.m_Colliders = new List<DynamicBoneCollider>();
                    dBone.enabled = true;
                    dBones++;
                }
                if (dBones > 0)
                {
                    Logger.LogDebug($"Activated {dBones} dynamic bone chains on {loadProcess.Import.SourceFileName}");
                }
            }

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
                        Singleton<KK_Plugins.MaterialEditor.MaterialEditorCharaController>.Instance?.SetMaterialTexture(
                            accessory.Slot,
                            KK_Plugins.MaterialEditor.MaterialEditorCharaController.ObjectType.Accessory,
                            p.Material,
                            prop,
                            data,
                            importObject);
                        Logger.LogDebug($"Set Texture on {p.Material.name} - {prop}");
                    }
                }
            }

            Vector3 s = importObject.transform.localScale;
            importObject.transform.localScale = new Vector3(s.x * loadProcess.Scale.x, s.y * loadProcess.Scale.y, s.z * loadProcess.Scale.z);
            importObject.transform.localRotation *= Quaternion.Euler(new Vector3(0, 180, 0));

            if (loadProcess.Kind == LoadProcess.LoadProcessKind.Normal)
            {
                Asset asset = new Asset
                {
                    SourceFile = loadProcess.Import.SourceIdentifier,
                    DynamicBoneIndices = new List<int>()
                };
                for (int i = 0; i < import.BoneNodes.Count; i++)
                {
                    BoneNode node = import.BoneNodes[i];
                    if (node.IsDynamicRoot) asset.DynamicBoneIndices.Add(i);
                }
                asset.Identifier = accessory.Slot;
                asset.Scale = new float[] { loadProcess.Scale.x, loadProcess.Scale.y, loadProcess.Scale.z };
                asset.HasBones = loadProcess.Import.HasBones;
                asset.PerRendererMaterials = loadProcess.Import.PerRendererMaterials;
                asset.DoFbxTranslation = loadProcess.Import.DoFbxTranslation;

                if (!loadedObjects.ContainsKey(ChaControl.fileStatus.coordinateType)) loadedObjects[ChaControl.fileStatus.coordinateType] = new Dictionary<int, Asset>();
                loadedObjects[ChaControl.fileStatus.coordinateType][asset.Identifier] = asset;
            }

            ChaControl.ChangeAccessoryColor(accessory.Slot);

            Logger.LogInfo($"Asset [{loadProcess.Import.SourceFileName}] was loaded successfully");
        }
    }
}
