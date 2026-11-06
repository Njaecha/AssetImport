using System.Reflection;
using AssetImport;
using UnityEngine;

internal static class ChainPhysicsChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var model = DrapeChecks.Model(false, (.3f, .84f, .4f));
        var chain = model.Chain;
        var environment = model.Environment;
        chain.m_Root.name = "PMX_0012_part";
        var particle = chain.m_Particles[1];
        var other = chain.gameObject.AddComponent<DynamicBone>();
        other.m_Root = new GameObject().transform;
        other.m_Root.parent = environment.ImportedRoot;
        other.m_Root.name = "PMX_0034_part";
        AssetPhysicsEnvironment.Attach(chain.gameObject, environment.ImportedRoot, [chain, other], PhysicsEnvironmentSettings.NewImport());
        check(environment.Mode(chain) == PhysicsChainMode.KeepShape && DynamicBone.ElasticityRead(chain, particle) == .3f,
            "new imports keep their shape attraction");
        var draft = environment.Settings.Copy();
        var choice = draft.EditRoot(chain.m_Root.name);
        choice.Mode = (int)PhysicsChainMode.FreeHanging;
        environment.Apply(draft);
        check(AssetPhysicsEnvironment.UsesNaturalDrape(chain) && !AssetPhysicsEnvironment.UsesNaturalDrape(other),
            "per-chain free hanging leaves sibling chains in keep-shape mode");
        choice.Mode = (int)PhysicsChainMode.KeepShape;
        choice.ShapeRetention = .25f;
        choice.ForceScale = 2f;
        environment.Apply(draft);
        check(Math.Abs(DynamicBone.ElasticityRead(chain, particle) - .075f) < 1e-6 &&
            Math.Abs(DynamicBone.SkipStiffnessRead(chain, particle) - .21f) < 1e-6,
            "per-chain shape multiplier applies to integrated and skipped constraints");
        float before = particle.m_Position.y;
        AssetPhysicsEnvironment.AfterIntegrate(chain);
        check(Math.Abs(before - particle.m_Position.y - .002f) < 1e-6, "force multiplier affects only additional downward load");
        choice.ForceScale = 0;
        environment.Apply(draft); before = particle.m_Position.y;
        AssetPhysicsEnvironment.AfterIntegrate(chain);
        check(particle.m_Position.y == before && chain.m_Gravity.y == 0, "zero additional load does not rewrite external gravity");

        // Changing the editing copy cannot affect the running simulation.
        choice.Mode = (int)PhysicsChainMode.Animation;
        check(AssetPhysicsEnvironment.ShouldSimulate(chain), "draft is independent from live choices");
        environment.Apply(draft);
        check(!AssetPhysicsEnvironment.ShouldSimulate(chain) && AssetPhysicsEnvironment.ShouldSimulate(other) && chain.enabled,
            "no-simulation choice is scoped to one chain without disabling the component");
        int steps = chain.Steps;
        chain.UpdateDynamicBones(1f / 60);
        chain.SkipUpdateParticles();
        check(chain.Steps == steps && particle.m_Position.y == particle.m_Transform.position.y,
            "no-simulation restores the input pose and does not advance or queue steps");
        check(environment.RequestSettle() == 1, "settling excludes chains with simulation turned off");
        var foreign = new GameObject().AddComponent<DynamicBone>();
        check(AssetPhysicsEnvironment.ShouldSimulate(foreign), "native characters and foreign chains remain simulated");
        choice.Mode = (int)PhysicsChainMode.Inherit;
        draft.NaturalDrape = true;
        environment.Apply(draft);
        check(AssetPhysicsEnvironment.UsesNaturalDrape(chain), "inherit follows the model default");
        draft.NaturalDrape = false;
        environment.Apply(draft);
        check(!AssetPhysicsEnvironment.UsesNaturalDrape(chain), "inherit responds to later default changes");

        var asset = new Asset();
        PhysicsEnvironmentPersistence.Write(asset, draft);
        var restored = PhysicsEnvironmentPersistence.Read(asset);
        check(asset.ChainPhysicsVersion == 1 && restored.ForRoot(chain.m_Root.name).ForceScale == 0 &&
            restored.ForRoot(chain.m_Root.name).ShapeRetention == .25f, "versioned per-chain settings survive asset persistence");
        restored.ForRoot(chain.m_Root.name).ShapeRetention = 1;
        check(asset.ChainPhysicsSettings[0].ShapeRetention == .25f, "read creates independent choices for copies and coordinate loads");
        draft.Chains[0].ShapeRetention = .5f;
        check(asset.ChainPhysicsSettings[0].ShapeRetention == .25f, "write does not keep references to editing values");
        var sanitized = new PhysicsEnvironmentSettings { DefaultMode = 99, Chains = [null,
            new() { Root = "", Mode = 3 }, new() { Root = "a", Mode = -1, ForceScale = float.NaN, ShapeRetention = 5 },
            new() { Root = "a", Mode = 2 }, new() { Root = "b", ForceScale = 50, ShapeRetention = float.NegativeInfinity }] }.Copy();
        check(sanitized.DefaultMode == 1 && sanitized.Chains.Length == 2 && sanitized.Chains[0].Mode == 0 &&
            sanitized.Chains[0].ForceScale == 1 && sanitized.Chains[0].ShapeRetention == 1 && sanitized.Chains[1].ForceScale == 4,
            "malformed records and duplicate roots are sanitized deterministically");
        foreach (string key in new[] { "Root", "Mode", "ForceScale", "ShapeRetention" })
            check(typeof(PhysicsChainSettings).GetField(key)!.GetCustomAttribute<MessagePack.KeyAttribute>()!.Key == key,
                "stable MessagePack field key " + key);
        foreach (string key in new[] { "ChainPhysicsVersion", "PhysicsDefaultMode", "ChainPhysicsSettings" })
            check(typeof(Asset).GetProperty(key)!.GetCustomAttribute<MessagePack.KeyAttribute>()!.Key == key,
                "stable MessagePack asset key " + key);
    }
}
