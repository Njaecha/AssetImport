using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace AssetImport
{
    // No game dependencies: the same production rewrite is exercised by the
    // compatibility harness against IL from the pinned MaterialEditor assemblies.
    internal static class MaterialEditorLoadDataPatch
    {
        internal static bool TryPatch(IEnumerable<CodeInstruction> instructions, MethodBase originalMethod,
            Type controllerType, MethodInfo correctTongue, MethodInfo continuation,
            out List<CodeInstruction> result, out string reason)
        {
            var original = instructions.ToList();
            try
            {
                // The patched method is MoveNext: arg.0 is the iterator, not the
                // MaterialEditor controller. Resolve its captured controller by type.
                var fields = originalMethod.DeclaringType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var controller = fields.SingleOrDefault(f => f.FieldType == controllerType);
                var accessories = fields.SingleOrDefault(f => f.Name == "accessories" && f.FieldType == typeof(bool));
                var body = fields.SingleOrDefault(f => f.Name == "body" && f.FieldType == typeof(bool));
                if (controller == null || accessories == null || body == null || correctTongue == null || continuation == null)
                    throw new InvalidOperationException("MaterialEditor iterator fields or load anchor were not found.");

                if (correctTongue.IsStatic || correctTongue.ReturnType != typeof(void) || correctTongue.GetParameters().Length != 0 ||
                    !continuation.IsStatic || continuation.ReturnType != typeof(void) ||
                    !continuation.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(new[] { controllerType, typeof(bool) }))
                    throw new InvalidOperationException("MaterialEditor anchor or callback signature is unsupported.");

                var calls = original.Select((instruction, index) => new { instruction, index })
                    .Where(x => x.instruction.opcode == OpCodes.Call && Equals(x.instruction.operand, correctTongue)).ToList();
                if (calls.Count != 1)
                    throw new InvalidOperationException("Expected exactly one MaterialEditor CorrectTongue call.");

                int callIndex = calls[0].index;
                int insertionIndex = -1;
                // Match the whole if(body) guard, including its receiver and branch.
                // Insert before the guard so accessory-only loads also restore assets.
                for (int i = Math.Max(0, callIndex - 5); i <= callIndex - 4; i++)
                {
                    if (original[i].opcode != OpCodes.Ldarg_0 || original[i + 1].opcode != OpCodes.Ldfld ||
                        !Equals(original[i + 1].operand, body)) continue;
                    var branch = original[i + 2];
                    if (branch.opcode != OpCodes.Brfalse && branch.opcode != OpCodes.Brfalse_S) continue;
                    if (!(branch.operand is Label target) || callIndex + 1 >= original.Count ||
                        !original[callIndex + 1].labels.Contains(target)) continue;
                    bool localReceiver = callIndex == i + 4 && IsLoadLocal(original[i + 3]);
                    bool fieldReceiver = callIndex == i + 5 && original[i + 3].opcode == OpCodes.Ldarg_0 &&
                        original[i + 4].opcode == OpCodes.Ldfld && Equals(original[i + 4].operand, controller);
                    if (localReceiver || fieldReceiver) insertionIndex = i;
                }
                if (insertionIndex < 0 || original[insertionIndex].blocks.Count != 0)
                    throw new InvalidOperationException("MaterialEditor load ordering did not match the supported iterator layout.");

                var code = original.Select(instruction => new CodeInstruction(instruction)).ToList();
                var injected = new[]
                {
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Ldfld, controller),
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Ldfld, accessories),
                    new CodeInstruction(OpCodes.Call, continuation)
                };
                injected[0].labels.AddRange(code[insertionIndex].labels);
                code[insertionIndex].labels.Clear();
                code.InsertRange(insertionIndex, injected);
                result = code;
                reason = null;
                return true;
            }
            catch (Exception ex)
            {
                result = original;
                reason = ex.Message;
                return false;
            }
        }

        private static bool IsLoadLocal(CodeInstruction instruction)
        {
            return instruction.opcode == OpCodes.Ldloc_0 || instruction.opcode == OpCodes.Ldloc_1 ||
                instruction.opcode == OpCodes.Ldloc_2 || instruction.opcode == OpCodes.Ldloc_3 ||
                instruction.opcode == OpCodes.Ldloc || instruction.opcode == OpCodes.Ldloc_S;
        }

    }
}
