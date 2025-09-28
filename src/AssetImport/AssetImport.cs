using System.Collections.Generic;
using ADV.Commands.Base;
using UnityEngine;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Configuration;
using KKAPI;
using KKAPI.Studio.SaveLoad;
using KKAPI.Chara;
using HarmonyLib;
using KK_Plugins.MaterialEditor;
using KKAPI.Utilities;

namespace AssetImport
{
    [BepInPlugin(GUID, PluginName, Version)]
    [BepInDependency(KoikatuAPI.GUID, KoikatuAPI.VersionConst)]
    [BepInDependency(KK_Plugins.MaterialEditor.MaterialEditorPlugin.PluginGUID, KK_Plugins.MaterialEditor.MaterialEditorPlugin.PluginVersion)]
    [BepInDependency(KK_Plugins.DynamicBoneEditor.Plugin.PluginGUID, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("LoadFileLmitedFix")]
    public class AssetImport : BaseUnityPlugin
    {
        public const string PluginName = "KKS_AssetImport";
        public const string GUID = "org.njaecha.plugins.assetimport";
        public const string Version = "3.3.1";

        internal new static ManualLogSource Logger;
        internal static AssetSceneController asc;
        internal static AssetUI UI;
        internal static AssetImport instance;

        // Config
        internal static ConfigEntry<KeyboardShortcut> hotkey;
        internal static ConfigEntry<string> defaultDir;
        internal static ConfigEntry<bool> dumpAssets;

        // current import
        internal static LoadProcess currentLoadProcess;

        internal static ComputeShader vertexDeltaComputeShader;

        void Awake()
        {
            Logger = base.Logger;
            
            KeyboardShortcut defaultShortcut = new KeyboardShortcut(KeyCode.I, KeyCode.LeftAlt);
            hotkey = Config.Bind("_General_", "Hotkey", defaultShortcut, "Press this key to open the UI");
            defaultDir = Config.Bind("_General_", "Default Directory", "C:", "The default directory of the file dialogue.");
            dumpAssets = Config.Bind("Backend", "Dump Assets", false, "Dumps assets to /UserData/AssetImport/ when loading a card with assets.");

            UI = this.GetOrAddComponent<AssetUI>();

            // custom controllers and hooks
            Harmony harmony = Harmony.CreateAndPatchAll(typeof(Hooks), null);

            StudioSaveLoadApi.RegisterExtraBehaviour<AssetSceneController>(GUID);
            CharacterApi.RegisterExtraBehaviour<AssetCharaController>(GUID);

            KKAPI.Maker.AccessoriesApi.AccessoryKindChanged += AccessoryKindChanged;
            KKAPI.Maker.AccessoriesApi.AccessoriesCopied += AccessoryCopied;
            KKAPI.Maker.AccessoriesApi.AccessoryTransferred += AccessoryTransferred;

            instance = this;
            
            // load assets
            byte[] data = ResourceUtils.GetEmbeddedResource("assetimport-resources");
            AssetBundle bundle = AssetBundle.LoadFromMemory(data);
            // vertexDelta ComputeShader
            ComputeShader vertexDelta = bundle.LoadAsset<ComputeShader>("VertexDelta");
            vertexDeltaComputeShader = vertexDelta;
        }

        private void AccessoryTransferred(object sender, KKAPI.Maker.AccessoryTransferEventArgs e)
        {
            int dSlot = e.DestinationSlotIndex;
            int sSlot = e.SourceSlotIndex;
            KKAPI.Maker.MakerAPI.GetCharacterControl().gameObject.GetComponent<AssetCharaController>().AccessoryTransferedEvent(sSlot, dSlot);
        }

        private void AccessoryCopied(object sender, KKAPI.Maker.AccessoryCopyEventArgs e)
        {
            ChaFileDefine.CoordinateType dType = e.CopyDestination;
            ChaFileDefine.CoordinateType sType = e.CopySource;
            IEnumerable<int> slots = e.CopiedSlotIndexes;
            KKAPI.Maker.MakerAPI.GetCharacterControl().gameObject.GetComponent<AssetCharaController>().AccessoryCopiedEvent((int)sType, (int)dType, slots);
        }

        private void AccessoryKindChanged(object sender, KKAPI.Maker.AccessorySlotEventArgs e)
        {
            int slot = e.SlotIndex;
            KKAPI.Maker.MakerAPI.GetCharacterControl().gameObject.GetComponent<AssetCharaController>().AccessoryChangeEvent(slot);
        }
    }
}