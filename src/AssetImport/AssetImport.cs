using System.Collections.Generic;
using UnityEngine;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Configuration;
using KKAPI;
using KKAPI.Studio.SaveLoad;
using KKAPI.Chara;
using HarmonyLib;

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
        public const string Version = "2.0.6";

        internal new static ManualLogSource Logger;
        internal static AssetSceneController asc;
        internal static AssetUI UI;
        internal static AssetImport instance;

        // Config
        internal static ConfigEntry<KeyboardShortcut> hotkey;
        internal static ConfigEntry<string> defaultDir;
        internal static ConfigEntry<bool> clearCache;

        // current import
        internal static LoadProcess currentLoadProcess;

        void Awake()
        {
            Logger = base.Logger;

            KeyboardShortcut defaultShortcut = new KeyboardShortcut(KeyCode.I, KeyCode.LeftAlt);
            hotkey = Config.Bind("_General_", "Hotkey", defaultShortcut, "Press this key to open the UI");
            defaultDir = Config.Bind("_General_", "Default Directory", "C:", "The default directory of the file dialoge.");
            clearCache = Config.Bind("Backend", "Clear Cache", true, "Toggles clearing of the cache at /UserData/AssetImport/cache on startup");

            UI = this.GetOrAddComponent<AssetUI>();

            // custom controllers and hooks
            Harmony harmony = Harmony.CreateAndPatchAll(typeof(Hooks), null);

            StudioSaveLoadApi.RegisterExtraBehaviour<AssetSceneController>(GUID);
            CharacterApi.RegisterExtraBehaviour<AssetCharaController>(GUID);

            if (clearCache.Value) CacheUtility.clearCache();
            KKAPI.Maker.AccessoriesApi.AccessoryKindChanged += AccessoryKindChanged;
            KKAPI.Maker.AccessoriesApi.AccessoriesCopied += AccessoryCopied;
            KKAPI.Maker.AccessoriesApi.AccessoryTransferred += AccessoryTransferred;

            instance = this;
        }

        private void AccessoryTransferred(object sender, KKAPI.Maker.AccessoryTransferEventArgs e)
        {
            int dSlot = e.DestinationSlotIndex;
            int sSlot = e.SourceSlotIndex;
            KKAPI.Maker.MakerAPI.GetCharacterControl().gameObject.GetComponent<AssetCharaController>().accessoryTransferedEvent(sSlot, dSlot);
        }

        private void AccessoryCopied(object sender, KKAPI.Maker.AccessoryCopyEventArgs e)
        {
            ChaFileDefine.CoordinateType dType = e.CopyDestination;
            ChaFileDefine.CoordinateType sType = e.CopySource;
            IEnumerable<int> slots = e.CopiedSlotIndexes;
            KKAPI.Maker.MakerAPI.GetCharacterControl().gameObject.GetComponent<AssetCharaController>().accessoryCopiedEvent((int)sType, (int)dType, slots);
        }

        private void AccessoryKindChanged(object sender, KKAPI.Maker.AccessorySlotEventArgs e)
        {
            int slot = e.SlotIndex;
            KKAPI.Maker.MakerAPI.GetCharacterControl().gameObject.GetComponent<AssetCharaController>().accessoryChangeEvent(slot);
        }
    }
}