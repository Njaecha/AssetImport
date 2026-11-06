using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace AssetImport
{
    internal static class PhysicsEnvironmentHooks
    {
        [HarmonyPrefix, HarmonyPatch(typeof(DynamicBone), nameof(DynamicBone.UpdateDynamicBones))]
        private static bool Simulate(DynamicBone __instance) { return AssetPhysicsEnvironment.ShouldSimulate(__instance); }
        [HarmonyPostfix, HarmonyPatch(typeof(DynamicBone), nameof(DynamicBone.UpdateParticles1))]
        private static void Integrate(DynamicBone __instance) { AssetPhysicsEnvironment.AfterIntegrate(__instance); }

        [HarmonyPostfix, HarmonyPatch(typeof(DynamicBone), nameof(DynamicBone.UpdateParticles2))]
        private static void Constraints(DynamicBone __instance) { AssetPhysicsEnvironment.AfterConstraints(__instance); }

        [HarmonyPostfix, HarmonyPatch(typeof(DynamicBone), nameof(DynamicBone.SkipUpdateParticles))]
        private static void Skip(DynamicBone __instance) { AssetPhysicsEnvironment.AfterConstraints(__instance, false); }

        [HarmonyPostfix, HarmonyPatch(typeof(DynamicBone), nameof(DynamicBone.ResetParticlesPosition))]
        private static void Reset(DynamicBone __instance) { AssetPhysicsEnvironment.AfterReset(__instance); }

        [HarmonyPostfix, HarmonyPatch(typeof(DynamicBone), nameof(DynamicBone.SetupParticles))]
        private static void Setup(DynamicBone __instance) { AssetPhysicsEnvironment.AfterReset(__instance); }

        [HarmonyTranspiler, HarmonyPatch(typeof(DynamicBone), nameof(DynamicBone.UpdateParticles2))]
        private static IEnumerable<CodeInstruction> DrapeConstraints(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod, ILGenerator generator)
        { return RewriteDrape(instructions, __originalMethod, generator); }

        [HarmonyTranspiler, HarmonyPatch(typeof(DynamicBone), nameof(DynamicBone.SkipUpdateParticles))]
        private static IEnumerable<CodeInstruction> DrapeSkip(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod, ILGenerator generator)
        { return RewriteDrape(instructions, __originalMethod, generator); }

        private static IEnumerable<CodeInstruction> RewriteDrape(IEnumerable<CodeInstruction> instructions, MethodBase method, ILGenerator generator)
        {
            List<CodeInstruction> result;
            string reason;
            bool ready = NaturalDrapePatch.TryRewrite(instructions, method, generator, out result, out reason);
            NaturalDrapePatch.SetReady(method.Name, ready);
            if (!ready) AssetImport.Logger.LogWarning("Natural drape unavailable: " + method.Name + ": " + reason);
            return result;
        }
    }
}
