using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace AssetImport
{
    // DynamicBone's elasticity and stiffness both attract particles to the
    // unforced input shape. Scale this attraction per chain, or release it for
    // free hanging under load, lengths and contacts. Override only the reads in its native
    // constraint passes: no editor values, particle parameters, transforms or
    // bind/rest data are written. Native damping, collisions, freeze axes and
    // the weight blend remain in the original solver.
    internal static class NaturalDrapePatch
    {
        private static bool _constraintsReady, _skipReady;
        internal static bool Available { get { return _constraintsReady && _skipReady; } }

        internal static void SetReady(string method, bool ready)
        {
            if (method == nameof(DynamicBone.UpdateParticles2)) _constraintsReady = ready;
            if (method == nameof(DynamicBone.SkipUpdateParticles)) _skipReady = ready;
        }

        internal static float RestAttraction(float coefficient, float retention)
        { return coefficient * retention; }

        internal static bool TryRewrite(IEnumerable<CodeInstruction> instructions, MethodBase method,
            ILGenerator generator, out List<CodeInstruction> result, out string reason)
        {
            var original = instructions.ToList();
            result = original;
            try
            {
                if (method == null || method.IsStatic || method.DeclaringType != typeof(DynamicBone) ||
                    method.GetParameters().Length != 0 ||
                    method.Name != nameof(DynamicBone.UpdateParticles2) && method.Name != nameof(DynamicBone.SkipUpdateParticles))
                    throw new InvalidOperationException("Unsupported DynamicBone constraint method.");
                var particle = typeof(DynamicBone).GetNestedType("Particle", BindingFlags.Public | BindingFlags.NonPublic);
                if (particle == null) throw new InvalidOperationException("DynamicBone particle type was not found.");
                var elasticity = particle.GetField("m_Elasticity", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var stiffness = particle.GetField("m_Stiffness", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (elasticity == null || stiffness == null || elasticity.FieldType != typeof(float) || stiffness.FieldType != typeof(float))
                    throw new InvalidOperationException("Unsupported particle parameter fields.");
                int elasticityReads = 0, stiffnessReads = 0;
                for (int i = 0; i < original.Count; i++)
                {
                    var instruction = original[i];
                    if (instruction.opcode != OpCodes.Ldfld) continue;
                    bool elastic = Equals(instruction.operand, elasticity), stiff = Equals(instruction.operand, stiffness);
                    if (!elastic && !stiff) continue;
                    if (elastic) elasticityReads++; else stiffnessReads++;
                }
                if (stiffnessReads == 0 || method.Name == nameof(DynamicBone.UpdateParticles2) && elasticityReads == 0)
                    throw new InvalidOperationException("Expected particle elasticity/stiffness reads were not found.");

                // Resolve ownership once per pass, not for every particle. Both
                // passes must patch successfully before either changes behavior.
                var active = generator.DeclareLocal(typeof(float));
                var patched = new List<CodeInstruction> {
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Call, typeof(AssetPhysicsEnvironment).GetMethod(nameof(AssetPhysicsEnvironment.ShapeRetention), BindingFlags.Static | BindingFlags.NonPublic)),
                    new CodeInstruction(OpCodes.Stloc, active)
                };
                foreach (var source in original)
                {
                    var instruction = new CodeInstruction(source);
                    instruction.opcode = LongBranch(instruction.opcode);
                    bool elastic = source.opcode == OpCodes.Ldfld && Equals(source.operand, elasticity);
                    bool stiff = source.opcode == OpCodes.Ldfld && Equals(source.operand, stiffness);
                    if (!elastic && !stiff) { patched.Add(instruction); continue; }
                    // Keep the field read in its original method, including
                    // access to nonpublic particle data. Filter the float it
                    // returns; never temporarily overwrite the field itself.
                    var filter = new CodeInstruction(OpCodes.Call, typeof(NaturalDrapePatch).GetMethod(nameof(RestAttraction), BindingFlags.Static | BindingFlags.NonPublic));
                    filter.blocks.AddRange(instruction.blocks.Where(block => block.blockType == ExceptionBlockType.EndExceptionBlock));
                    instruction.blocks.RemoveAll(block => block.blockType == ExceptionBlockType.EndExceptionBlock);
                    patched.Add(instruction);
                    patched.Add(new CodeInstruction(OpCodes.Ldloc, active));
                    patched.Add(filter);
                }
                result = patched;
                reason = null;
                return true;
            }
            catch (Exception error)
            {
                reason = error.Message;
                return false;
            }
        }

        private static OpCode LongBranch(OpCode op)
        {
            if (op == OpCodes.Br_S) return OpCodes.Br;
            if (op == OpCodes.Brfalse_S) return OpCodes.Brfalse;
            if (op == OpCodes.Brtrue_S) return OpCodes.Brtrue;
            if (op == OpCodes.Beq_S) return OpCodes.Beq;
            if (op == OpCodes.Bne_Un_S) return OpCodes.Bne_Un;
            if (op == OpCodes.Bge_S) return OpCodes.Bge;
            if (op == OpCodes.Bge_Un_S) return OpCodes.Bge_Un;
            if (op == OpCodes.Bgt_S) return OpCodes.Bgt;
            if (op == OpCodes.Bgt_Un_S) return OpCodes.Bgt_Un;
            if (op == OpCodes.Ble_S) return OpCodes.Ble;
            if (op == OpCodes.Ble_Un_S) return OpCodes.Ble_Un;
            if (op == OpCodes.Blt_S) return OpCodes.Blt;
            if (op == OpCodes.Blt_Un_S) return OpCodes.Blt_Un;
            if (op == OpCodes.Leave_S) return OpCodes.Leave;
            return op;
        }
    }
}
