using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using KKAPI;
using KKAPI.Studio.SaveLoad;
using KKAPI.Utilities;
using Studio;
using System.Reflection.Emit;
using KK_Plugins.MaterialEditor;

namespace AssetImport
{
    internal class Hooks
    {
        [HarmonyPostfix, HarmonyPatch(typeof(Studio.SceneInfo), nameof(Studio.SceneInfo.Load), new Type[]{typeof(string)})]
        private static void SceneLoadHook(string _path)
        {
            _path = _path.Replace("\\", "/");
            AssetImport.Logger.LogDebug("Scene name registered: "+ _path.Substring(_path.LastIndexOf("/") + 1, _path.Length - _path.LastIndexOf("/")-1));
            AssetImport.asc.SceneName = _path.Substring(_path.LastIndexOf("/") + 1, _path.Length - _path.LastIndexOf("/") - 1);
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

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(MaterialEditorCharaController), nameof(MaterialEditorCharaController.LoadData), MethodType.Enumerator)]
        [HarmonyPatch(new[] { typeof(bool), typeof(bool), typeof(bool), typeof(bool) })]
        static IEnumerable<CodeInstruction> MaterialEditorLoadDataTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var code = new List<CodeInstruction>(instructions);
            object accessoriesLdfldOperant = null;

            foreach (CodeInstruction c in code.Where(c => c != null).Where(c => c.operand != null))
            {
                if  (c.operand.ToString().Equals("System.Boolean accessories"))
                {
                    accessoriesLdfldOperant = c.operand;
                }
                if (c.operand.ToString().Contains("__this"))
                {
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
        private static void MaterialEditorCharacterLoadHook(GameMode currentGameMode, bool maintainState, KK_Plugins.MaterialEditor.MaterialEditorCharaController __instance)
        {
            __instance?.ChaControl?.gameObject.GetComponentInChildren<AssetCharaController>()?.LoadCharacter(currentGameMode, maintainState);
        }


    }
}
