using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using KKAPI;
using KKAPI.Maker;
using KKAPI.Studio.SaveLoad;
using KKAPI.Utilities;
using KK_Plugins.MaterialEditor;
using Studio;

namespace AssetImport
{
    internal class Hooks
    {
#if KK
        internal const string PoseEditorAssembly = "KKPE";
        internal const string BoneEditorAssembly = "KKABMX";
        internal const string DynamicBoneEditorAssembly = "KK_DynamicBoneEditor";
#else
        internal const string PoseEditorAssembly = "KKSPE";
        internal const string BoneEditorAssembly = "KKSABMX";
        internal const string DynamicBoneEditorAssembly = "KKS_DynamicBoneEditor";
#endif

        internal static bool IsOptionalIntegrationAvailable(string assemblyName)
        {
            return AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name == assemblyName);
        }

        internal static void RunOptionalIntegration(string assemblyName, string operation, Action action)
        {
            if (!IsOptionalIntegrationAvailable(assemblyName)) return;
            try { action(); }
            catch (Exception ex) { AssetImport.Logger.LogWarning($"AssetImport {operation} could not be applied: {ex}"); }
        }

        // These hooks run inside MaterialEditor's load path. An import failure must
        // never prevent MaterialEditor from restoring the remaining materials.
        private static void RunBeforeMaterialEditor(string operation, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                AssetImport.Logger.LogError($"AssetImport {operation} failed; MaterialEditor will continue: {ex}");
            }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(SceneInfo), nameof(SceneInfo.Load), new[] { typeof(string) })]
        private static void SceneLoadHook(string _path)
        {
            if (AssetImport.asc == null || string.IsNullOrEmpty(_path)) return;
            AssetImport.asc.SceneName = System.IO.Path.GetFileName(_path.Replace("\\", "/"));
        }

        [HarmonyPrefix, HarmonyPatch(typeof(KK_Plugins.MaterialEditor.SceneController), "OnSceneLoad")]
        private static void MaterialEditorSceneLoadHook(SceneOperationKind operation, ReadOnlyDictionary<int, ObjectCtrlInfo> loadedItems)
        {
            RunBeforeMaterialEditor("scene load", () => AssetImport.asc?.LoadScene(operation, loadedItems));
        }

        [HarmonyPrefix, HarmonyPatch(typeof(KK_Plugins.MaterialEditor.SceneController), "OnObjectsCopied")]
        private static void MaterialEditorSceneCopyHook(ReadOnlyDictionary<int, ObjectCtrlInfo> copiedItems)
        {
            RunBeforeMaterialEditor("scene copy", () => AssetImport.asc?.ObjectsCopied(copiedItems));
        }

        [HarmonyPrefix, HarmonyPatch(typeof(MaterialEditorCharaController), "OnCoordinateBeingLoaded")]
        private static void MaterialEditorCoordinateLoadHook(ChaFileCoordinate coordinate, bool maintainState, MaterialEditorCharaController __instance)
        {
            RunBeforeMaterialEditor("coordinate data load", () =>
                __instance?.ChaControl?.gameObject.GetComponent<AssetCharaController>()?.LoadCoordinate(coordinate));
        }

        public static void MaterialEditorLoadDataTranspilerContinuer(MaterialEditorCharaController controller, bool accessories)
        {
            if (!accessories || controller == null) return;
            RunBeforeMaterialEditor("accessory restore", () =>
                controller.ChaControl?.gameObject.GetComponent<AssetCharaController>()?.LoadData());
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(MaterialEditorCharaController), nameof(MaterialEditorCharaController.LoadData), MethodType.Enumerator)]
        [HarmonyPatch(new[] { typeof(bool), typeof(bool), typeof(bool), typeof(bool) })]
        private static IEnumerable<CodeInstruction> MaterialEditorLoadDataTranspiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            if (!MaterialEditorLoadDataPatch.TryPatch(instructions, __originalMethod,
                typeof(MaterialEditorCharaController),
                AccessTools.Method(typeof(MaterialEditorCharaController), "CorrectTongue"),
                AccessTools.Method(typeof(Hooks), nameof(MaterialEditorLoadDataTranspilerContinuer)),
                out var result, out var reason))
                AssetImport.Logger.LogError($"AssetImport accessory restore hook was disabled because this MaterialEditor build is incompatible. Original material loading is preserved: {reason}");
            return result;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(MaterialEditorCharaController), "OnReload")]
        private static void MaterialEditorCharacterLoadHook(GameMode currentGameMode, bool maintainState, MaterialEditorCharaController __instance)
        {
            RunBeforeMaterialEditor("character data load", () =>
            {
                if (MakerAPI.InsideMaker && MakerAPI.GetCharacterLoadFlags()?.Clothes == false) return;
                __instance?.ChaControl?.gameObject.GetComponent<AssetCharaController>()?.LoadCharacter(currentGameMode, maintainState);
            });
        }
    }
}
