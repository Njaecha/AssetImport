using AssetImport;

internal static class PhysicsPresetBindingChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var model = new PmxModel {
            Bones = new[] { ("root",-1), ("head",0), ("hair1",1), ("tip",2), ("skirt",0), ("tip",4) }
                .Select(b=>new PmxModel.Bone{Name=b.Item1,Parent=b.Item2}).ToArray(),
            RigidBodyData = new[] {2,4}.Select(b=>new PmxModel.RigidBody {Bone=b,Mode=1}).ToArray()
        };
        var import = new Import(model) { PhysicsProfile = 1 };
        var hair = import.Chain(2); var skirt = import.Chain(4);
        hair.m_Radius = .029f;
        var root = hair.m_Root; var colliders = hair.m_Colliders;
        import.ApplyPmxPhysicsDefaults(new[]{hair,skirt});
        check(hair.m_UpdateRate==60 && Math.Abs(hair.m_Stiffness-.1f)<1e-6, "production import adapter binds the matched hair preset");
        check(skirt.m_UpdateRate==60 && Math.Abs(skirt.m_Stiffness-.84f)<1e-6, "production import adapter binds a different skirt preset");
        check(hair.m_Radius==.029f && hair.m_Root==root && hair.m_Colliders==colliders,
            "preset binding keeps preexisting radius, root and collider ownership");
        var foreign = new Import(model).Chain(2); foreign.m_Damping=.77f;
        import.ApplyPmxPhysicsDefaults(new[]{foreign});
        check(foreign.m_Damping==.77f, "same-named bone on another imported object is not modified");
        hair.m_Damping=.81f;
        foreach(int profile in new[]{0,99})
        {
            import.PhysicsProfile=profile; import.ApplyPmxPhysicsDefaults(new[]{hair});
            check(hair.m_Damping==.81f, "legacy/unknown profile does not replace existing physics settings");
        }
        var fbx = new Import(model,false){PhysicsProfile=1}; var fbxChain=fbx.Chain(2); fbxChain.m_Damping=.77f;
        fbx.ApplyPmxPhysicsDefaults(new[]{fbxChain});
        check(fbxChain.m_Damping==.77f, "FBX and other formats do not inherit a PMX preset");
        import.PhysicsProfile=1;import.ImportBones=false;import.ApplyPmxPhysicsDefaults(new[]{hair});
        check(hair.m_Damping==.81f, "disabled armature does not create a physics configuration");
    }
}
