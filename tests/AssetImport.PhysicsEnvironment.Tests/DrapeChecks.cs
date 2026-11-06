using System.Reflection;
using System.Reflection.Emit;
using AssetImport;
using HarmonyLib;
using UnityEngine;

internal static class DrapeChecks
{
    private static readonly FieldInfo Elasticity = typeof(DynamicBone.Particle).GetField("m_Elasticity")!;
    private static readonly FieldInfo Stiffness = typeof(DynamicBone.Particle).GetField("m_Stiffness")!;

    internal static void InstallAndRun(Action<bool, string> check)
    {
        // Execute probes emitted through the exact production IL rewrite.
        // The fixture uses these delegates for every native-style constraint
        // read, including render frames without an integration step.
        DynamicBone.ElasticityRead = CompileRead(Elasticity, false, check);
        DynamicBone.StiffnessRead = CompileRead(Stiffness, false, check);
        DynamicBone.SkipStiffnessRead = CompileRead(Stiffness, true, check);
        NaturalDrapePatch.SetReady(nameof(DynamicBone.UpdateParticles2), true);
        NaturalDrapePatch.SetReady(nameof(DynamicBone.SkipUpdateParticles), true);

        foreach (var coefficients in new[] { (.07f, .1f, .18f), (.15f, .84f, .36f), (.3f, .1f, .4f) })
        {
            var old = Model(false, coefficients); var drape = Model(true, coefficients);
            var bind = drape.Chain.m_Particles.Select(p => (p.m_InitLocalPosition, p.m_InitLocalRotation)).ToArray();
            var parameters = drape.Chain.m_Particles.Select(p => (p.m_Elasticity, p.m_Stiffness, p.m_Damping)).ToArray();
            var originalTip = drape.Chain.m_Particles.Last().m_Transform.position;
            Run(old.Chain, 1400, true); Run(drape.Chain, 1400, true);
            var tip = drape.Chain.m_Particles.Last().m_Position;
            var anchor = drape.Chain.m_Root.position;
            Console.WriteLine($"Static fixture (elasticity {coefficients.Item1}, stiffness {coefficients.Item2}): old tip X={old.Chain.m_Particles.Last().m_Position.x:F5}; draped X={tip.x:F5}, drop={anchor.y - tip.y:F5} (length 1).");
            check(tip.y < anchor.y - .98f && Math.Abs(tip.x - anchor.x) < .025f, "stationary chain hangs vertically despite original shape springs");
            check(old.Chain.m_Particles.Last().m_Position.x > tip.x + .6f, "old downward-force-only path reproduces a near-bind static pose");
            check(drape.Chain.m_Particles.Select(p => (p.m_Elasticity, p.m_Stiffness, p.m_Damping)).SequenceEqual(parameters), "draping never mutates editor particle parameters");
            check(drape.Chain.m_Particles.Select(p => (p.m_InitLocalPosition, p.m_InitLocalRotation)).SequenceEqual(bind), "binding and native initialization data remain unchanged");
            CheckLengths(drape.Chain, check);

            // Move the attachment, then stop; compare the new equilibrium to
            // the final attachment rather than to the original world origin.
            for (int i = 0; i < 150; i++)
            {
                drape.Chain.m_Root.position = new(.15f * MathF.Sin(i * .1f), 2, 0);
                Run(drape.Chain, 1, true);
            }
            anchor = drape.Chain.m_Root.position;
            Run(drape.Chain, 1400, true); tip = drape.Chain.m_Particles.Last().m_Position;
            check(tip.y < anchor.y - .98f && Math.Abs(tip.x - anchor.x) < .025f, "after motion stops equilibrium remains hanging instead of springing back to bind pose");

            drape.Chain.m_Root.position = new(0, 2, 0);
            drape.Chain.SetEnabled(false);
            check((drape.Chain.m_Particles.Last().m_Transform.position - originalTip).magnitude < .0001f, "disabling physics restores original unforced geometry");
            check(DynamicBone.ElasticityRead(drape.Chain, drape.Chain.m_Particles[1]) == coefficients.Item1, "disabled chains retain native coefficient reads");
            drape.Chain.SetEnabled(true); Run(drape.Chain, 1400, true);
            check(drape.Chain.m_Particles.Last().m_Position.y < 1.02f, "reenabling physics falls again without baking the previous result");
            drape.Environment.Apply(new() { DownwardForce = .001f, NaturalDrape = false });
            Run(drape.Chain, 300, true);
            check(drape.Chain.m_Particles.Last().m_Position.x > .6f, "turning drape off immediately restores the existing shape springs");
        }

        foreach (float scale in new[] { .1f, 1f, 10f })
            foreach (var rest in new[] { new Vector3(-1, -.5f, .3f), new Vector3(1, .5f, -.3f) })
            {
                var drape = Model(true, (.15f, .84f, .36f), scale, rest);
                Run(drape.Chain, 1800, true);
                var delta = drape.Chain.m_Particles.Last().m_Position - drape.Chain.m_Root.position;
                check(delta.y < -.98f * scale && Math.Abs(delta.x) < .025f * scale && Math.Abs(delta.z) < .025f * scale,
                    "mirrored, diagonal and scaled chains all hang in world gravity direction");
            }

        var loaded = PhysicsEnvironmentPersistence.Read(new Asset { DownwardForce = .001f });
        check(!loaded.NaturalDrape, "unversioned downward-force saves keep shape retention");
        check(!PhysicsEnvironmentPersistence.Read(new Asset { DownwardForce=.001f,NaturalDrape=true }).NaturalDrape,
            "unversioned automatic free-hanging saves migrate to shape retention");
        check(!PhysicsEnvironmentPersistence.Read(new Asset()).NaturalDrape, "pre-environment saves do not silently enable new physics");
        check(!PhysicsEnvironmentPersistence.Read(new Asset { DownwardForce = .001f, NaturalDrape = false }).NaturalDrape, "explicit drape opt-out survives migration");
        loaded.NaturalDrape=true;
        var asset = new Asset(); PhysicsEnvironmentPersistence.Write(asset, loaded);
        var copy = new Asset(); PhysicsEnvironmentPersistence.Write(copy, PhysicsEnvironmentPersistence.Read(asset));
        loaded.NaturalDrape = false;
        check(asset.NaturalDrape == true && PhysicsEnvironmentPersistence.Read(copy).NaturalDrape, "saved and copied drape settings are independent values");

        var warm = Model(true, (.3f, .1f, .4f)); warm.Environment.Apply(new() { NaturalDrape = true, DownwardForce = .001f, Settle = true });
        for (int frame = 0; frame < 1000 && warm.Environment.IsSettling; frame++)
            typeof(AssetPhysicsEnvironment).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(warm.Environment, null);
        check(!warm.Environment.IsSettling && warm.Chain.Steps <= AssetPhysicsEnvironment.DrapeSettleSteps, "warmup stops at convergence or its finite cap");
        check(warm.Chain.m_Particles.Last().m_Position.y < 1.1f, "warmup grants enough steps for the initially horizontal chain to sag");
        Console.WriteLine($"Warmup fixture: {warm.Chain.Steps} steps; tip Y={warm.Chain.m_Particles.Last().m_Position.y:F5}, anchor Y=2.");

        var force = Model(true, (.3f, .1f, .4f));
        force.Chain.m_Force = new(.001f, 0, 0); force.Chain.m_Gravity = new(0, 0, -.0005f);
        Run(force.Chain, 1400, false);
        var direction = (force.Chain.m_Particles.Last().m_Position - force.Chain.m_Root.position).normalized;
        var expected = new Vector3(.001f, -.001f, -.0005f).normalized;
        check(Vector3.Dot(direction, expected) > .99f, "equilibrium responds to total load, including external force and gravity");
        check(force.Chain.m_Force.x == .001f && force.Chain.m_Gravity.z == -.0005f, "external forces remain unchanged");
        force.Chain.Contact = p => { if (p.m_Position.z < -.1f) p.m_Position.z = -.1f; };
        Run(force.Chain, 300, false);
        check(force.Chain.m_Particles.Last().m_Position.z >= -.102f, "native collision stage is retained");

        var owned = Model(true, (.3f, .84f, .4f)); var particle = owned.Chain.m_Particles[1];
        var outsider = new GameObject().AddComponent<DynamicBone>();
        check(DynamicBone.ElasticityRead(outsider, particle) == .3f && DynamicBone.SkipStiffnessRead(outsider, particle) == .84f, "foreign chains keep native shape springs");
        owned.Chain.m_Weight = 0;
        check(DynamicBone.StiffnessRead(owned.Chain, particle) == .84f, "zero-weight chain is not relaxed"); owned.Chain.m_Weight = 1;
        NaturalDrapePatch.SetReady(nameof(DynamicBone.SkipUpdateParticles), false);
        check(DynamicBone.ElasticityRead(owned.Chain, particle) == .3f, "failure of either hook disables the entire drape override");
        NaturalDrapePatch.SetReady(nameof(DynamicBone.SkipUpdateParticles), true);
        check(DynamicBone.ElasticityRead(owned.Chain, particle) == 0 && DynamicBone.SkipStiffnessRead(owned.Chain, particle) == 0, "full-step and skipped-step constraints share the mode");
        UnsupportedAndLabels(check, owned.Chain, particle);
    }

    private static Func<DynamicBone, DynamicBone.Particle, float> CompileRead(FieldInfo field, bool skip, Action<bool, string> check)
    {
        var dynamic = new DynamicMethod("ReadCoefficient", typeof(float), [typeof(DynamicBone), typeof(DynamicBone.Particle)], typeof(DrapeChecks).Module, true);
        var il = dynamic.GetILGenerator();
        var code = new List<CodeInstruction>();
        if (!skip)
        {
            code.Add(new(OpCodes.Ldarg_1)); code.Add(new(OpCodes.Ldfld, field == Elasticity ? Stiffness : Elasticity)); code.Add(new(OpCodes.Pop));
        }
        code.Add(new(OpCodes.Ldarg_1)); code.Add(new(OpCodes.Ldfld, field)); code.Add(new(OpCodes.Ret));
        var method = typeof(DynamicBone).GetMethod(skip ? nameof(DynamicBone.SkipUpdateParticles) : nameof(DynamicBone.UpdateParticles2))!;
        check(NaturalDrapePatch.TryRewrite(code, method, il, out var rewritten, out var reason), "production rewrite accepts fixture reads: " + reason);
        Emit(il, rewritten);
        return dynamic.CreateDelegate<Func<DynamicBone, DynamicBone.Particle, float>>();
    }

    private static void UnsupportedAndLabels(Action<bool, string> check, DynamicBone chain, DynamicBone.Particle particle)
    {
        var method = typeof(DynamicBone).GetMethod(nameof(DynamicBone.UpdateParticles2))!;
        var dynamic = new DynamicMethod("BranchProbe", typeof(float), [typeof(DynamicBone), typeof(DynamicBone.Particle)], typeof(DrapeChecks).Module, true);
        var il = dynamic.GetILGenerator(); var target = il.DefineLabel();
        var code = new List<CodeInstruction> { new(OpCodes.Ldarg_1), new(OpCodes.Br_S, target) };
        for (int i = 0; i < 17; i++) code.AddRange([new(OpCodes.Ldarg_1), new(OpCodes.Ldfld, Stiffness), new(OpCodes.Pop)]);
        var read = new CodeInstruction(OpCodes.Ldfld, Elasticity); read.labels.Add(target); code.Add(read); code.Add(new(OpCodes.Ret));
        check(NaturalDrapePatch.TryRewrite(code, method, il, out var rewritten, out _), "branch probe rewrites");
        check(code[1].opcode == OpCodes.Br_S && read.labels.Contains(target), "source instruction labels and opcodes not mutated");
        check(rewritten.Single(c => c.labels.Contains(target)).opcode == OpCodes.Ldfld, "branch target still reads the coefficient before filtering it");
        Emit(il, rewritten);
        var execute = dynamic.CreateDelegate<Func<DynamicBone, DynamicBone.Particle, float>>();
        check(execute(chain, particle) == 0, "expanded branches and field-stack shape execute correctly");
        chain.SettingsForProbe(false);
        check(execute(chain, particle) == particle.m_Elasticity, "same emitted path preserves the native parameter when off");
        chain.SettingsForProbe(true);

        var incomplete = new List<CodeInstruction> { new(OpCodes.Ldarg_1), new(OpCodes.Ldfld, Elasticity), new(OpCodes.Ret) };
        check(!NaturalDrapePatch.TryRewrite(incomplete, method, il, out var rejected, out _) && rejected.SequenceEqual(incomplete), "missing stiffness leaves the entire original method intact");
        var blockProbe = new List<CodeInstruction> { new(OpCodes.Ldarg_1), new(OpCodes.Ldfld, Stiffness), new(OpCodes.Pop), new(OpCodes.Ldarg_1), new(OpCodes.Ldfld, Elasticity), new(OpCodes.Ret) };
        blockProbe[4].blocks.Add(new(ExceptionBlockType.EndExceptionBlock));
        check(NaturalDrapePatch.TryRewrite(blockProbe, method, il, out var blockResult, out _) &&
            blockResult.Single(c => c.blocks.Any()).opcode == OpCodes.Call, "exception block end remains after the replacement read");
    }

    private static void SettingsForProbe(this DynamicBone chain, bool drape)
        => chain.gameObject.GetComponent<AssetPhysicsEnvironment>().Apply(new() { NaturalDrape = drape, DownwardForce = .001f });

    private static void Emit(ILGenerator il, IEnumerable<CodeInstruction> code)
    {
        foreach (var c in code)
        {
            foreach (var label in c.labels) il.MarkLabel(label);
            switch (c.operand)
            {
                case null: il.Emit(c.opcode); break;
                case MethodInfo method: il.Emit(c.opcode, method); break;
                case FieldInfo field: il.Emit(c.opcode, field); break;
                case LocalBuilder local: il.Emit(c.opcode, local); break;
                case Label label: il.Emit(c.opcode, label); break;
                default: throw new Exception("Unexpected operand");
            }
        }
    }

    internal static (AssetPhysicsEnvironment Environment, DynamicBone Chain) Model(bool drape, (float Elasticity, float Stiffness, float Damping) values, float scale = 1, Vector3? direction = null)
    {
        var host = new GameObject(); var model = new GameObject().transform; model.parent = host.transform;
        model.lossyScale = new(scale, scale, scale);
        var anchor = new GameObject().transform; anchor.parent = model; anchor.position = new(0, 2 * scale, 0);
        var chain = host.AddComponent<DynamicBone>(); chain.m_Root = anchor;
        chain.m_Particles.Add(new() { m_Transform = anchor, m_Position = anchor.position, m_PrevPosition = anchor.position });
        for (int i = 1; i <= 4; i++)
        {
            var t = new GameObject().transform; t.parent = chain.m_Particles[i - 1].m_Transform;
            t.position = anchor.position + (direction ?? new Vector3(1, 0, 0)).normalized * (scale * i * .25f);
            chain.m_Particles.Add(new() { m_ParentIndex = i - 1, m_Transform = t, m_Position = t.position, m_PrevPosition = t.position,
                m_Elasticity = values.Elasticity, m_Stiffness = values.Stiffness, m_Damping = values.Damping });
        }
        chain.SetupParticles();
        AssetPhysicsEnvironment.Attach(host, model, [chain], new() { DownwardForce = .001f, NaturalDrape = drape });
        return (host.GetComponent<AssetPhysicsEnvironment>(), chain);
    }

    internal static void Run(DynamicBone chain, int steps, bool skipFrames)
    {
        for (int i = 0; i < steps; i++)
        {
            chain.InitTransforms(); chain.UpdateDynamicBones(1f / 60);
            if (skipFrames) { chain.InitTransforms(); chain.SkipUpdateParticles(); }
        }
    }

    private static void CheckLengths(DynamicBone chain, Action<bool, string> check)
    {
        foreach (var p in chain.m_Particles.Skip(1))
            check(Math.Abs((p.m_Position - chain.m_Particles[p.m_ParentIndex].m_Position).magnitude - .25f) < .00001f, "gravity does not stretch bone links");
    }
}
