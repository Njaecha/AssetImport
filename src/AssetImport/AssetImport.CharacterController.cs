using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using IllusionUtility.GetUtility;
using BepInEx.Logging;
using KKAPI;
using KKAPI.Chara;
using KKAPI.Maker;
using ExtensibleSaveFormat;
using MessagePack;
using System.IO;
using Main = AssetImport.AssetImport;
using System.Collections;
using System.Text.RegularExpressions;
using BepInEx.Bootstrap;
using KKABMX.Core;
using HSPE;
using HarmonyLib;
using System.Runtime.CompilerServices;

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

        private AssetAccessoryPhysics GetAccessoryPhysics(int coordinate, int slot, out Asset asset)
        {
            asset = null;
            if (coordinate != ChaControl.fileStatus.coordinateType ||
                !loadedObjects.TryGetValue(coordinate, out var assets) ||
                !assets.TryGetValue(slot, out asset)) return null;
            var accessory = ChaControl.GetAccessoryComponent(slot);
            if (!accessory) return null;
            var physics = accessory.gameObject.GetComponent<AssetAccessoryPhysics>();
            return physics && physics.SourceIdentifier == asset.SourceFile ? physics : null;
        }

        private void CaptureAccessoryPhysics(int coordinate, int slot)
        {
            var physics = GetAccessoryPhysics(coordinate, slot, out var asset);
            if (physics) physics.CaptureState(asset);
        }

        private void CaptureCurrentAccessoryPhysics()
        {
            int coordinate = ChaControl.fileStatus.coordinateType;
            if (!loadedObjects.TryGetValue(coordinate, out var assets)) return;
            foreach (int slot in assets.Keys) CaptureAccessoryPhysics(coordinate, slot);
        }

        private void RestoreAccessoryPhysics(int coordinate, int slot)
        {
            var physics = GetAccessoryPhysics(coordinate, slot, out var asset);
            if (physics)
            {
                AssetPhysicsEnvironment.Attach(physics.gameObject, physics.ImportedRoot, physics.Chains, PhysicsEnvironmentPersistence.Read(asset));
                physics.SetEnabled(AssetAccessoryPhysics.RestoreEnabled(asset));
            }
        }

        internal AssetPhysicsEnvironment GetSelectedPhysicsEnvironment(out Asset asset)
        {
            var physics = GetSelectedAccessoryPhysics(out asset);
            return physics ? physics.GetComponent<AssetPhysicsEnvironment>() : null;
        }

        private IEnumerator ApplyEditorDataAndRestorePhysics(IEnumerator applyData, int coordinate)
        {
            // Await the optional editor's own restore rather than forcing the
            // global switch every frame or guessing how many frames it needs.
            yield return applyData;
            if (coordinate != ChaControl.fileStatus.coordinateType ||
                !loadedObjects.TryGetValue(coordinate, out var assets)) yield break;
            foreach (int slot in assets.Keys)
            {
                if (!AssetAccessoryPhysics.RestoreEnabled(assets[slot])) RestoreAccessoryPhysics(coordinate, slot);
                else
                {
                    var physics = GetAccessoryPhysics(coordinate, slot, out var asset);
                    var environment = physics ? physics.GetComponent<AssetPhysicsEnvironment>() : null;
                    if (environment && environment.Settings.Settle) environment.RequestSettle();
                }
            }
        }

        private AssetAccessoryPhysics GetSelectedAccessoryPhysics(out Asset asset)
        {
            asset = null;
            if (!MakerAPI.InsideMaker || MakerAPI.GetCharacterControl() != ChaControl ||
                AccessoriesApi.SelectedMakerAccSlot < 0) return null;
            return GetAccessoryPhysics(ChaControl.fileStatus.coordinateType, AccessoriesApi.SelectedMakerAccSlot, out asset);
        }

        internal LocalizedText GetSelectedAccessoryPhysicsStatus()
        {
            var physics = GetSelectedAccessoryPhysics(out var asset);
            if (!physics) return L.Message("Select an accessory imported by AssetImport to control its physics.");
            var chains = physics.GetOwnedChains();
            if (chains.Count == 0) return L.Message("Selected accessory has no physics chains. Select roots when importing.");
            int enabled = 0;
            foreach (var chain in chains) if (chain.enabled) enabled++;
            return L.Message("Accessory physics: {0}/{1} chains ON.", enabled, chains.Count);
        }

        internal LocalizedText SetSelectedAccessoryPhysicsEnabled(bool enabled)
        {
            var physics = GetSelectedAccessoryPhysics(out var asset);
            if (!physics) return L.Message("Select an accessory imported by AssetImport first.");
            int count = physics.SetEnabled(enabled);
            if (count == 0) return L.Message("No physics chains exist. Select roots when importing the accessory.");
            asset.PhysicsEnabled = enabled;
            return L.Message("Accessory physics {0}: {1} chains; roots and colliders retained.", L.Message(enabled ? "ON" : "OFF"), count);
        }

        internal LocalizedText ResetSelectedAccessoryPhysics()
        {
            var physics = GetSelectedAccessoryPhysics(out var asset);
            if (!physics) return L.Message("Select an accessory imported by AssetImport first.");
            var result = PhysicsReset.Reset(physics.GetOwnedChains());
            foreach (string message in result.FailureMessages) Logger.LogWarning("Cannot reset accessory physics: " + message);
            return L.Message("Physics reset: {0} chain(s); {1} inactive, {2} invalid, {3} failed. Settings retained.", result.ResetCount, result.InactiveCount, result.InvalidCount, result.FailedCount);
        }

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
            if (AssetDebug.Enabled) Logger.LogInfo("Accessory Transfer Event");
            if (loadedObjects[cSet].ContainsKey(source))
            {
                CaptureAccessoryPhysics(cSet, source);
                Asset asset = new Asset
                {
                    SourceFile = loadedObjects[cSet][source].SourceFile,
                    DynamicBoneIndices = loadedObjects[cSet][source].DynamicBoneIndices,
                    Identifier = destination,
                    Scale = loadedObjects[cSet][source].Scale,
                    HasBones = loadedObjects[cSet][source].HasBones,
                    PerRendererMaterials = loadedObjects[cSet][source].PerRendererMaterials,
                    PhysicsEnabled = loadedObjects[cSet][source].PhysicsEnabled,
                    PhysicsProfile = loadedObjects[cSet][source].PhysicsProfile,
                    PmxHairCollisionAssist = loadedObjects[cSet][source].PmxHairCollisionAssist
                };
                PhysicsEnvironmentPersistence.Write(asset, PhysicsEnvironmentPersistence.Read(loadedObjects[cSet][source]));
                loadedObjects[cSet][destination] = asset;
                RestoreAccessoryPhysics(cSet, destination);
                if (AssetDebug.Enabled) Logger.LogInfo($"Source slot {source} --> Destination slot {destination}");
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
                    CaptureAccessoryPhysics(source, slot);
                    Asset asset = new Asset();
                    asset.SourceFile = loadedObjects[source][slot].SourceFile;
                    asset.DynamicBoneIndices = loadedObjects[source][slot].DynamicBoneIndices;
                    asset.Identifier = slot;
                    asset.Scale = loadedObjects[source][slot].Scale;
                    asset.HasBones = loadedObjects[source][slot].HasBones;
                    asset.PerRendererMaterials = loadedObjects[source][slot].PerRendererMaterials;
                    asset.PhysicsEnabled = loadedObjects[source][slot].PhysicsEnabled;
                    asset.PhysicsProfile = loadedObjects[source][slot].PhysicsProfile;
                    asset.PmxHairCollisionAssist = loadedObjects[source][slot].PmxHairCollisionAssist;
                    PhysicsEnvironmentPersistence.Write(asset, PhysicsEnvironmentPersistence.Read(loadedObjects[source][slot]));
                    loadedObjects[destination][slot] = asset;
                    RestoreAccessoryPhysics(destination, slot);
                    if (AssetDebug.Enabled) Logger.LogInfo($"Source: Type {source}, Slot {slot} --> Destination: Type {destination}");
                }
                else if (loadedObjects[destination].ContainsKey(slot))
                {
                    loadedObjects[destination].Remove(slot);
                }
            }
        }

        protected override void OnCardBeingSaved(GameMode currentGameMode)
        {
            CaptureCurrentAccessoryPhysics();
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
            if (AssetDebug.Enabled) Logger.LogInfo("Set Extended data");
        }

        internal void LoadCharacter(GameMode currentGameMode, bool MaintainState)
        {
            if (MaintainState || (currentGameMode == GameMode.Maker && MakerAPI.GetCharacterLoadFlags()?.Clothes == false)) return;
            if (AssetDebug.Enabled) Logger.LogInfo($"Character Load Started {ChaControl.fileParam.fullname}");
            loadedObjects.Clear();
            if (currentGameMode == GameMode.Maker)
            {
                RamCacheUtility.ClearCache();
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
                                if (AssetDebug.Enabled) Logger.LogInfo($"No sourceFiles found in extended data for clothing slot {cSet}");
                                continue;
                            }
                            if (AssetDebug.Enabled) Logger.LogInfo($"{sourceFiles.Count} sourceFiles found, extracting to cache...");
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
                        if (AssetDebug.Enabled) Logger.LogInfo($"No assets found in extended data for clothing slot {cSet}");
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
            CaptureCurrentAccessoryPhysics();
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
            if (AssetDebug.Enabled) Logger.LogInfo("Set Coordinate Extended data");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void LoadCoordinateCompatibilityDynamicBoneEditor(int cSet)
        {
            if (!(_dBoneBackup is List<KK_Plugins.DynamicBoneEditor.DynamicBoneData> backup)) return;
            foreach (KK_Plugins.DynamicBoneEditor.DynamicBoneData dboneData in backup)
            {
                if (dboneData.CoordinateIndex != cSet) continue;
                KK_Plugins.DynamicBoneEditor.CharaController dBoneController = ChaControl.gameObject.GetComponentInChildren<KK_Plugins.DynamicBoneEditor.CharaController>();
                var data = GetDynamicBoneEditorData(dBoneController);
                if (data == null || data.Any(entry =>
                        entry.CoordinateIndex.Equals(dboneData.CoordinateIndex)
                        && entry.Slot.Equals(dboneData.Slot)
                        && entry.BoneName.Equals(dboneData.BoneName))) continue;
                if (AssetDebug.Enabled) Logger.LogInfo($"Compatibility Mode: Added back DynamicBoneEditor data for slot {dboneData.Slot}: {dboneData.BoneName}");
                data.Add(dboneData);
            }
        }

        internal void LoadCoordinate(ChaFileCoordinate coordinate)
        {
            int cSet = ChaControl.fileStatus.coordinateType;

            // CoordinateLoadOption compatibilty
            // check if Coordinate Load Option is installed
            var cMode = false;
            var cloImportAccessories = new List<int>(); // slots that are loaded new
#if KK
            const string coordinateLoadOptionGuid = "com.jim60105.kk.coordinateloadoption";
#else
            const string coordinateLoadOptionGuid = "com.jim60105.kks.coordinateloadoption";
#endif
            if (Chainloader.PluginInfos.ContainsKey(coordinateLoadOptionGuid))
            {
                if (AssetDebug.Enabled) Logger.LogInfo("Coordinate Load Option deducted");
                if (GameObject.Find("CoordinateTooglePanel")?.activeInHierarchy == true)
                {
                    if (AssetDebug.Enabled) Logger.LogInfo("Coordinate Load Option enabled");
                    bool? accEnabled = GameObject.Find("CoordinateTooglePanel/accessories")?.GetComponent<Toggle>()?.isOn;
                    switch (accEnabled)
                    {
                        case true:
                        {
                            if (AssetDebug.Enabled) Logger.LogInfo("Coordinate Load Option accessory load enabled, entering compatibility mode");
                            cMode = true;

                            if (GameObject.Find("CoordinateTooglePanel/AccessoriesTooglePanel/BtnChangeAccLoadMode")?.GetComponentInChildren<Text>()?.text != "Replace Mode")
                            {
                                Logger.LogMessage(L.Text("Asset Import WARNING: Add Mode is not supported! Stopping asset load"));
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
                            if (AssetDebug.Enabled) Logger.LogInfo("Coordinate Load Option accessory load disabled -> stopping asset load.");
                            if (Chainloader.PluginInfos.ContainsKey("com.deathweasel.bepinex.dynamicboneeditor") &&
                                Hooks.IsOptionalIntegrationAvailable(Hooks.DynamicBoneEditorAssembly))
                                Hooks.RunOptionalIntegration(Hooks.DynamicBoneEditorAssembly, "dynamic bone data restore", () => LoadCoordinateCompatibilityDynamicBoneEditor(cSet));
                            return;
                        }
                    }
                }
            }

            // Maker partial coordinate load fix
            if (KKAPI.Maker.MakerAPI.InsideAndLoaded)
            {
                // return if no new accessories are being loaded
                if (MakerAPI.GetCoordinateLoadFlags()?.Accessories == false) return;
            }

            if (coordinate == null) return;
            CoordinateCardNames[cSet] = (coordinate.coordinateFileName ?? "").Replace(".png", "");

            if (AssetDebug.Enabled) Logger.LogInfo($"Coordinate Load Started {cSet} on {ChaControl.fileParam.fullname}");
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
                            if (AssetDebug.Enabled) AssetImport.Logger.LogInfo("No sourceFiles found in extended data.");
                            return;
                        }
                        if (AssetDebug.Enabled) Logger.LogInfo($"{sourceFiles.Count} sourceFiles found, extracting to cache...");
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
                    if (AssetDebug.Enabled) Logger.LogInfo("No assets found in extended data, aborting load...");
                    return;
                }

                foreach (Asset asset in assets)
                {
                    if (!loadedObjects.ContainsKey(cSet)) loadedObjects[cSet] = new Dictionary<int, Asset>();

                    // ignore Asset Data of accessories that are not being loaded
                    if (cMode && !cloImportAccessories.Contains(asset.Identifier))
                    {
                        if (AssetDebug.Enabled) Logger.LogInfo($"Compatibilty Mode: Accessory in slot {asset.Identifier} is not being loaded -> discarding data.");
                        continue;
                    }

                    loadedObjects[cSet][asset.Identifier] = asset;
                    if (AssetDebug.Enabled) Logger.LogInfo($"Loading dat of asset for accessory in slot {asset.Identifier}");
                }
            }

            // dynamic bone editor 
            if (cMode && Chainloader.PluginInfos.ContainsKey("com.deathweasel.bepinex.dynamicboneeditor"))
            {
                if (Hooks.IsOptionalIntegrationAvailable(Hooks.DynamicBoneEditorAssembly))
                    Hooks.RunOptionalIntegration(Hooks.DynamicBoneEditorAssembly, "partial dynamic bone data restore", () => CoordinateLoadOptionDynamicBoneEditor(cSet, cloImportAccessories));
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void CoordinateLoadOptionDynamicBoneEditor(int cSet, List<int> cloImportAccessories)
        {
            if (!(_dBoneBackup is List<KK_Plugins.DynamicBoneEditor.DynamicBoneData> backup)) return;
            foreach (KK_Plugins.DynamicBoneEditor.DynamicBoneData dboneData in backup)
            {
                if (dboneData.CoordinateIndex != cSet || cloImportAccessories.Contains(dboneData.Slot)) continue;
                KK_Plugins.DynamicBoneEditor.CharaController dBoneController = ChaControl.gameObject.GetComponentInChildren<KK_Plugins.DynamicBoneEditor.CharaController>();
                var data = GetDynamicBoneEditorData(dBoneController);
                if (data == null || data.Contains(dboneData)) continue;
                Logger.LogInfo($"Compatibility Mode: Added back DynamicBoneEditor data for {dboneData.Slot}");
                data.Add(dboneData);
            }
        }

        private object _dBoneBackup;

        internal void LoadData()
        {
            if (_hasBeenLoadedAlready) return;
            if (AssetDebug.Enabled) Logger.LogInfo("Reloading Assets");
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

                if (AssetDebug.Enabled)
                {
                    File.WriteAllBytes(Path.Combine(GetDumpPath(cSet), RamCacheUtility.GetFileName(asset.SourceFile)), RamCacheUtility.GetFileBlob(asset.SourceFile));
                }

                Import import = new Import(
                    asset.SourceFile,
                    asset.HasBones,
                    Instantiate(accessory.Accessory.gameObject.GetComponentInChildren<Renderer>().material),
                    asset.PerRendererMaterials);
                import.PhysicsProfile = asset.PhysicsProfile;
                import.PmxHairCollisionAssist = asset.PmxHairCollisionAssist;
                import.PhysicsEnvironment = PhysicsEnvironmentPersistence.Read(asset);
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

            if (Hooks.IsOptionalIntegrationAvailable(Hooks.PoseEditorAssembly))
                Hooks.RunOptionalIntegration(Hooks.PoseEditorAssembly, "pose editor refresh", RefreshPoseEditor);
            if (Chainloader.PluginInfos.ContainsKey("com.deathweasel.bepinex.dynamicboneeditor") &&
                Hooks.IsOptionalIntegrationAvailable(Hooks.DynamicBoneEditorAssembly))
                Hooks.RunOptionalIntegration(Hooks.DynamicBoneEditorAssembly, "dynamic bone editor backup", DynamicBoneEditorBackup);
            if (Hooks.IsOptionalIntegrationAvailable(Hooks.BoneEditorAssembly))
                Hooks.RunOptionalIntegration(Hooks.BoneEditorAssembly, "bone editor refresh", RefreshBoneEditor);
            
            _hasBeenLoadedAlready = true;
            this.StartCoroutine(ResetLoadedAlready());
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void RefreshBoneEditor()
        {
            var controller = ChaControl.gameObject.GetComponentInChildren<BoneController>();
            if (controller) controller.NeedsFullRefresh = true;
        }

        // These members are non-public in the official KK releases. Reflect at
        // this optional integration boundary instead of requiring publicized DLLs.
        private static List<KK_Plugins.DynamicBoneEditor.DynamicBoneData> GetDynamicBoneEditorData(KK_Plugins.DynamicBoneEditor.CharaController controller)
        {
            if (!controller) return null;
            return AccessTools.Field(controller.GetType(), "AccessoryDynamicBoneData")?.GetValue(controller)
                as List<KK_Plugins.DynamicBoneEditor.DynamicBoneData>;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void RefreshPoseEditor()
        {
            try
            {
                var window = Singleton<HSPE.MainWindow>.Instance;
                if (window != null)
                    AccessTools.Method(window.GetType(), "OnCharaLoad")?.Invoke(window, new object[] { ChaControl.chaFile });
                var pose = ChaControl.gameObject.GetComponentInChildren<PoseController>();
                if (pose == null) return;
                var editor = AccessTools.Field(pose.GetType(), "_dynamicBonesEditor")?.GetValue(pose);
                if (editor != null)
                    AccessTools.Method(editor.GetType(), "RefreshDynamicBoneList")?.Invoke(editor, null);
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Could not refresh the pose editor after asset import: {ex}");
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void DynamicBoneEditorBackup()
        {
            var controller = ChaControl.gameObject.GetComponentInChildren<KK_Plugins.DynamicBoneEditor.CharaController>();
            var data = GetDynamicBoneEditorData(controller);
            if (data == null) return;
            var applyData = AccessTools.Method(controller.GetType(), "ApplyData")?.Invoke(controller, null) as IEnumerator;
            if (applyData != null) ChaControl.StartCoroutine(ApplyEditorDataAndRestorePhysics(applyData, ChaControl.fileStatus.coordinateType));
            // Replace the snapshot rather than retaining data from older outfits.
            _dBoneBackup = new List<KK_Plugins.DynamicBoneEditor.DynamicBoneData>(data);
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

        public void Import(int slot, int type, string parent, string path, Vector3 scale, bool armature, bool perRendererMaterials, bool loadBlendshapes)
        {
            // unify path structure
            path = path.Replace("\"", "");
            path = path.Replace("\\", "/");
            
            if (!(File.Exists(path)))
            {
                Logger.LogMessage(L.Format("File {0} does not exist", path));
                return;
            }

            if (AssetDebug.Enabled) Logger.LogInfo($"Accessory Import started on slot {slot} with type [{type}] and parentNode [{parent}]");

            ChaAccessoryComponent accessory = ChaControl.GetAccessoryComponent(slot);
            if (!accessory)
            {
                Logger.LogMessage(L.Text("Please specify a Type and Parent by choosing a normal accessory first."));
                return;
            }

            Logger.LogInfo($"Importing [{path}] started...");

            GameObject baseObject = accessory.gameObject;
            Material baseMaterial = Instantiate(baseObject.GetComponentInChildren<Renderer>().material);

            string identifierHash = RamCacheUtility.ToCache(path);

            if (AssetDebug.Enabled)
            {
                File.WriteAllBytes(Path.Combine(GetDumpPath(), Path.GetFileName(path)), RamCacheUtility.GetFileBlob(identifierHash));
            }

            Import import = new Import(
                identifierHash,
                armature, 
                baseMaterial, 
                perRendererMaterials,
                loadBlendshapes
            );
            import.Load();
            if (!import.IsLoaded) return;
            import.ResolveTextureFiles(path);

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
            bool enablePhysics = import.PhysicsEnabledAfterImport;
            if (loadProcess.Kind != LoadProcess.LoadProcessKind.Normal &&
                loadedObjects.TryGetValue(ChaControl.fileStatus.coordinateType, out var savedAssets) &&
                savedAssets.TryGetValue(accessory.Slot, out var savedAsset))
                enablePhysics = AssetAccessoryPhysics.RestoreEnabled(savedAsset);

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
            // DynamicBone must see the final scale when it initializes particles.
            importObject.transform.localScale = loadProcess.Scale;

            // renderers
            accessory.RendNormal = import.Renderers.ToArray();
            accessory.RendAlpha = new Renderer[0];

            // name (experimental, does not yet behave as intended)
            RenameAccessory(accessory.Slot, import.SourceFileName);
            Singleton<ChaCustom.CustomAcsChangeSlot>.Instance?.UpdateSlotNames();

            // remove dynamic bones that might exist
            foreach (DynamicBone bone in loadProcessBase.GetComponents<DynamicBone>())
            {
                DestroyImmediate(bone);
            }

            // Keep exact ownership references so the manual switch never touches
            // other accessories, character hair or another plugin's components.
            var physics = loadProcessBase.GetComponent<AssetAccessoryPhysics>() ?? loadProcessBase.AddComponent<AssetAccessoryPhysics>();
            physics.SourceIdentifier = import.SourceIdentifier;
            physics.ImportedRoot = importObject.transform;
            physics.Chains = new DynamicBone[0];
            AssetPhysicsEnvironment.Attach(loadProcessBase, importObject.transform, physics.Chains, import.PhysicsEnvironment);

            // dynamic bones
            if (import.HasBones)
            {
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
                    physics.Chains = dBones.ToArray();
                    AssetPhysicsEnvironment.Attach(loadProcessBase, importObject.transform, physics.Chains, import.PhysicsEnvironment, import.JointPhysicsBindings);
                    NativeContactController.Attach(loadProcessBase, import.BuildNativeContacts());
                    physics.SetEnabled(enablePhysics);
                    if (AssetDebug.Enabled) Logger.LogInfo($"Created {dBones.Count} dynamic bone chains on {loadProcess.Import.SourceFileName}; physics {(enablePhysics ? "ON" : "OFF")}");
                }
            }

            if (!loadProcess.Import.IsPmx) importObject.transform.localRotation *= Quaternion.Euler(new Vector3(0, 180, 0));

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
                asset.PhysicsEnabled = enablePhysics;
                asset.PhysicsProfile = import.PhysicsProfile;
                asset.PmxHairCollisionAssist = import.PmxHairCollisionAssist;
                PhysicsEnvironmentPersistence.Write(asset, import.PhysicsEnvironment);

                if (!loadedObjects.ContainsKey(ChaControl.fileStatus.coordinateType)) loadedObjects[ChaControl.fileStatus.coordinateType] = new Dictionary<int, Asset>();
                loadedObjects[ChaControl.fileStatus.coordinateType][asset.Identifier] = asset;
            }

            ChaControl.ChangeAccessoryColor(accessory.Slot);

            if (loadProcess.Kind == LoadProcess.LoadProcessKind.Normal)
            {
                int coordinate = ChaControl.fileStatus.coordinateType;
                Logger.LogInfo("Importing textures in background; wait for completion before saving.");
                // Keep polling target validity even when this character is destroyed/disabled.
                Main.instance.StartCoroutine(new ImportTextureData(loadProcess.ColorLut).ApplyAsync(
                    import.MaterialTextures.Values.SelectMany(paths => paths).ToArray(),
                    () => this && ChaControl && loadProcessBase && importObject &&
                        ChaControl.fileStatus.coordinateType == coordinate &&
                        ChaControl.GetAccessoryComponent(accessory.Slot) == accessory.Accessory,
                    (path, bytes) =>
                    {
                        if (!path.Material) return;
                        string property = path.Type == Assimp.TextureType.Normals ? "NormalMap" : "MainTex";
                        ChaControl.gameObject.GetComponent<KK_Plugins.MaterialEditor.MaterialEditorCharaController>()?.SetMaterialTexture(
                            accessory.Slot, KK_Plugins.MaterialEditor.MaterialEditorCharaController.ObjectType.Accessory,
                            path.Material, property, bytes, importObject);
                    },
                    message => Logger.LogWarning(message),
                    () => Logger.LogInfo($"Asset [{import.SourceFileName}] loaded; texture processing finished.")));
            }
            else Logger.LogInfo($"Asset [{import.SourceFileName}] was loaded successfully");
        }
    }
}
