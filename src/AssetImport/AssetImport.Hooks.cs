using System;
using System.Collections.Generic;
using HarmonyLib;
using KKAPI;
using KKAPI.Studio.SaveLoad;
using KKAPI.Utilities;
using Studio;
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

        public static void MaterialEditorLoadDataTranspilerContinuer(ChaControl chaControl, bool accessories)
        {
            if (accessories) // do not reload accessories if they wont be updated by ME
            {
                chaControl?.gameObject.GetComponent<AssetCharaController>()?.LoadData();
            }
        }

        [HarmonyTranspiler, HarmonyPatch(typeof(KK_Plugins.MaterialEditor.MaterialEditorCharaController), "LoadData", MethodType.Enumerator)]
        static IEnumerable<CodeInstruction> MaterialEditorLoadDataTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var code = new List<CodeInstruction>(instructions);
            object accessoriesLdfldOperant = null;
            object thisLdfldOperant = null;

            foreach(CodeInstruction c in code)
            {
                if (c == null) continue;
                if (c.operand == null) continue;
                if  (c.operand.ToString().Equals("System.Boolean accessories"))
                {
                    accessoriesLdfldOperant = c.operand;
                }
                if (c.operand.ToString().Contains("__this"))
                {
                    thisLdfldOperant = c.operand;
                }
            }

            CodeMatcher cm = new CodeMatcher(instructions, generator);
            cm.MatchForward(false, new CodeMatch(OpCodes.Call, AccessTools.Method(typeof(KK_Plugins.MaterialEditor.MaterialEditorCharaController), "CorrectTongue")));
            cm.Advance(-2);
            cm.InsertAndAdvance(
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Call, AccessTools.PropertyGetter(typeof(KKAPI.Chara.CharaCustomFunctionController), nameof(KKAPI.Chara.CharaCustomFunctionController.ChaControl))),
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldfld, accessoriesLdfldOperant),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Hooks), nameof(MaterialEditorLoadDataTranspilerContinuer))),
                new CodeInstruction(OpCodes.Nop)
                );

            return cm.Instructions();
        }

        [HarmonyPrefix, HarmonyPatch(typeof(KK_Plugins.MaterialEditor.MaterialEditorCharaController), "OnReload")]
        private static void MaterialEdtiorCharacterLoadHook(GameMode currentGameMode, bool maintainState, KK_Plugins.MaterialEditor.MaterialEditorCharaController __instance)
        {
            __instance?.ChaControl?.gameObject.GetComponentInChildren<AssetCharaController>()?.LoadCharacter(currentGameMode, maintainState);
        }


    }
}
