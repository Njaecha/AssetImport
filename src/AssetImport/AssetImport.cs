using System.Collections.Generic;
using System;
using System.IO;
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
    [BepInDependency(KK_Plugins.MaterialEditor.MaterialEditorPlugin.PluginGUID, MinimumMaterialEditorVersion)]
    [BepInDependency(KK_Plugins.DynamicBoneEditor.Plugin.PluginGUID, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("LoadFileLmitedFix")]
    public class AssetImport : BaseUnityPlugin
    {
#if KK
        public const string PluginName = "KK_AssetImport";
        // Keep the supported minimum independent of future build-reference upgrades.
        internal const string MinimumMaterialEditorVersion = "4.0.3";
#else
        public const string PluginName = "KKS_AssetImport";
        internal const string MinimumMaterialEditorVersion = "3.13.5";
#endif
        public const string GUID = "org.njaecha.plugins.assetimport";
        public const string Version = "4.2.8";

        internal new static ManualLogSource Logger;
        internal static AssetSceneController asc;
        internal static AssetUI UI;
        internal static AssetImport instance;

        // Config
        internal static ConfigEntry<KeyboardShortcut> hotkey;
        internal static ConfigEntry<string> defaultDir;
        internal static ConfigEntry<bool> debugMode;
        internal static ConfigEntry<InterfaceLanguage> language;
        internal static ConfigEntry<bool> pmxBodyCollisions;
        internal static ConfigEntry<string> mmddLibraryPath;
        internal static ConfigEntry<string> colorLutPath;

        // current import
        internal static LoadProcess currentLoadProcess;

#if !KK
        internal static ComputeShader vertexDeltaComputeShader;
#endif

        void Awake()
        {
            Logger = base.Logger;
            language = Config.Bind("_General_", "Language", InterfaceLanguage.English,
                "Interface language. Changes apply immediately.\n界面语言，切换后立即生效。English / 简体中文");
            L.Language = language.Value;
            language.SettingChanged += (sender, args) => L.Language = language.Value;
            debugMode = Config.Bind("_General_", "Debug", false,
                "Enable detailed logs, diagnostic reports, physics previews and asset dumps. Disable for normal use.\n开启详细日志、诊断报告、物理预览与资源导出。通常请关闭。");
            AssetDebug.Enabled = debugMode.Value;
            debugMode.SettingChanged += (sender, args) => AssetDebug.Enabled = debugMode.Value;
            NativeContactDiagnostics.Initialize();
            var buildInfo = (System.Reflection.AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(
                typeof(AssetImport).Assembly, typeof(System.Reflection.AssemblyInformationalVersionAttribute));
            if (AssetDebug.Enabled) Logger.LogInfo($"Runtime build {Version}; source {buildInfo?.InformationalVersion ?? "unknown"}; assembly {typeof(AssetImport).Assembly.GetName().Version}");
#if KK
            // Match the original Packed ZIP layout; do not depend on the working directory.
            string nativePath = Path.Combine(Paths.GameRootPath,
                "runtimes/win-x64/native/assimp.dll");
            if (IntPtr.Size != 8 || !File.Exists(nativePath))
            {
                Logger.LogError("KK AssetImport requires the full Windows x64 package. Missing native library: " + nativePath);
                enabled = false;
                return;
            }
            try
            {
                var library = Assimp.Unmanaged.AssimpLibrary.Instance;
                if (library.IsLibraryLoaded && !string.Equals(Path.GetFullPath(library.LibraryPath),
                    Path.GetFullPath(nativePath), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Another plugin has already loaded AssimpNet from " +
                        library.LibraryPath + ". Restart with only one AssimpNet importer enabled.");
                if (!library.LoadLibrary(nativePath))
                    throw new InvalidOperationException("Assimp returned an unsuccessful library load.");
            }
            catch (Exception exception)
            {
                Logger.LogError("Could not load the bundled Assimp 5 library: " + exception);
                enabled = false;
                return;
            }
#endif
            
            KeyboardShortcut defaultShortcut = new KeyboardShortcut(KeyCode.I, KeyCode.LeftAlt);
            hotkey = Config.Bind("_General_", "Hotkey", defaultShortcut, "Press this key to open the UI.\n打开界面的快捷键。");
            defaultDir = Config.Bind("_General_", "Default Directory", Paths.GameRootPath, "The default directory of the file dialogue.\n文件选择窗口的默认目录。");
            pmxBodyCollisions = Config.Bind("PMX", "Enable Body Collisions", true,
                "Build DynamicBone body colliders from PMX static rigid bodies. Reload the scene/model after changing this setting. Uses PMX collision masks; this is not full MMD rigid-body/joint physics.\n按 PMX 静态刚体及碰撞分组创建身体碰撞体。修改后需重载模型或场景；并非完整 MMD 刚体物理。");
            mmddLibraryPath = Config.Bind("PMX", "MMDD Library Path", "",
                "Optional MMDD Lib folder. Leave empty to discover a unique MMDD installation under BepInEx/plugins. Relative paths start at the game root. Re-export PMX Auto after changing this setting.\n可选的 MMDD Lib 路径；留空自动查找。相对路径以游戏根目录为起点。修改后请重新生成 PMX Auto 映射。");

            colorLutPath = Config.Bind("Textures", "KK Color LUT", Path.Combine(Paths.GameRootPath, "UserData/AssetImport/PS转KK色.cube"),
                "PS-to-KK 3D .cube LUT used only when Convert diffuse to KK color is checked. Original texture files are never overwritten.\nPS 转 KK 的 .cube 色彩查找表，仅在开启贴图转色时使用，不修改原贴图。");
            Import.InstallMmddRuntime();
            UI = this.GetOrAddComponent<AssetUI>();

            // custom controllers and hooks
            Harmony harmony = Harmony.CreateAndPatchAll(typeof(Hooks), null);
            Harmony.CreateAndPatchAll(typeof(PhysicsEnvironmentHooks), GUID + ".physics-environment");

            StudioSaveLoadApi.RegisterExtraBehaviour<AssetSceneController>(GUID);
            CharacterApi.RegisterExtraBehaviour<AssetCharaController>(GUID);

            KKAPI.Maker.AccessoriesApi.AccessoryKindChanged += AccessoryKindChanged;
            KKAPI.Maker.AccessoriesApi.AccessoriesCopied += AccessoryCopied;
            KKAPI.Maker.AccessoriesApi.AccessoryTransferred += AccessoryTransferred;

            instance = this;
            
#if !KK
            // KK uses CPU deltas; Unity 5.6 cannot load this 2019 bundle.
            byte[] data = ResourceUtils.GetEmbeddedResource("assetimport-resources");
            AssetBundle bundle = AssetBundle.LoadFromMemory(data);
            // vertexDelta ComputeShader
            ComputeShader vertexDelta = bundle.LoadAsset<ComputeShader>("VertexDelta");
            vertexDeltaComputeShader = vertexDelta;
#endif
        }

        private void Update() { NativeContactDiagnostics.Poll(); }

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
