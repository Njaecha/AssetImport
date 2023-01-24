using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using HarmonyLib;
using KKAPI;
using KKAPI.Studio.SaveLoad;
using KKAPI.Utilities;
using Studio;
using UnityEngine;
using HSPE;
using System.Reflection.Emit;

namespace AssetImport
{
    class Hooks
    {
        [HarmonyPostfix, HarmonyPatch(typeof(Studio.SceneInfo), nameof(Studio.SceneInfo.Load), new Type[]{typeof(string)})]
        private static void SceneLoadHook(string _path)
        {
            AssetImport.Logger.LogDebug("Scene name registered: "+ _path.Substring(_path.LastIndexOf("/") + 1, _path.Length - _path.LastIndexOf("/")-1));
            AssetImport.asc.sceneName = _path.Substring(_path.LastIndexOf("/") + 1, _path.Length - _path.LastIndexOf("/") - 1);
        }

        [HarmonyPrefix, HarmonyPatch(typeof(KK_Plugins.MaterialEditor.SceneController), "OnSceneLoad")]
        private static void MaterialEditorSceneLoadHook(SceneOperationKind operation, ReadOnlyDictionary<int, ObjectCtrlInfo> loadedItems)
        {
            AssetImport.asc?.LoadScene(operation, loadedItems);
        }

        [HarmonyPrefix, HarmonyPatch(typeof(KK_Plugins.MaterialEditor.SceneController), "OnObjectsCopied")]
        private static void MaterialEditorSceneCopyHook(ReadOnlyDictionary<int, ObjectCtrlInfo> copiedItems)
        {
            AssetImport.asc?.ObjectsCopied(copiedItems);
        }

        [HarmonyPrefix, HarmonyPatch(typeof(KK_Plugins.MaterialEditor.MaterialEditorCharaController), "OnCoordinateBeingLoaded")]
        private static void MaterialEditorCoordinateLoadHook(ChaFileCoordinate coordinate, bool maintainState, KK_Plugins.MaterialEditor.MaterialEditorCharaController __instance)
        {
            __instance?.ChaControl?.gameObject.GetComponentInChildren<AssetCharaController>()?.LoadCoordinate(coordinate);
        }

        [HarmonyPrefix, HarmonyPatch(typeof(KK_Plugins.MaterialEditor.MaterialEditorCharaController), "CorrectTongue")]
        private static void MaterialEditorLoadDataHook(KK_Plugins.MaterialEditor.MaterialEditorCharaController __instance)
        {
            __instance?.ChaControl?.gameObject.GetComponentInChildren<AssetCharaController>()?.LoadData();
        }

        [HarmonyPrefix, HarmonyPatch(typeof(KK_Plugins.MaterialEditor.MaterialEditorCharaController), "OnReload")]
        private static void MaterialEdtiorCharacterLoadHook(GameMode currentGameMode, bool maintainState, KK_Plugins.MaterialEditor.MaterialEditorCharaController __instance)
        {
            __instance?.ChaControl?.gameObject.GetComponentInChildren<AssetCharaController>()?.LoadCharacter(currentGameMode, maintainState);
        }
    }
}
