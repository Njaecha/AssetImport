using AssetImport;
using System.Text.Json;

internal static class MappingChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var model=new PmxModel { Bones=new[] {
            new PmxModel.Bone { Name="root", Parent=-1, Flags=6 },
            new PmxModel.Bone { Name="append", Parent=0, Flags=0x102, GrantParent=2, GrantWeight=.5f, Layer=1 },
            new PmxModel.Bone { Name="source", Parent=0, Flags=2 },
            new PmxModel.Bone { Name="hair", Parent=0, Flags=2 },
            new PmxModel.Bone { Name="hair_tip", Parent=3, Flags=2 }
        }, Morphs=Array.Empty<PmxModel.Morph>() };
        var targets=new[] { new PmxMorphTarget {Renderer="split",Shape="blink"},new PmxMorphTarget {Renderer="split",Shape="blink"} };
        PmxMmddResult Build(int[] roots)=>PmxMmddMapping.Build(model,new byte[]{1},"original test",targets,roots);
        var normal=Build(Array.Empty<int>());
        var json=JsonDocument.Parse(normal.MappingJson).RootElement;
        var order=json.GetProperty("bones").EnumerateArray().Select(b=>b.GetProperty("vmdName").GetString()).ToArray();
        check(Array.IndexOf(order,"source")<Array.IndexOf(order,"append"),"grant dependency before consumer despite source index order");
        check(json.GetProperty("morphs")[0].GetProperty("funcList").GetArrayLength()==1,"duplicate renderer chunks need one broadcasting FUNC");
        var variant=Build(new[]{3});
        check(variant.BoneCount==3 && variant.ModelId!=normal.ModelId,"selected physics root excludes descendants and changes mapping identity");
        var withoutMorphs=PmxMmddMapping.Build(model,new byte[]{1},"original test",Array.Empty<PmxMorphTarget>(),Array.Empty<int>());
        check(withoutMorphs.ModelId!=normal.ModelId,"different BlendShape options do not overwrite each other's map");
        void Reject(Action action,string reason) {try {action();throw new Exception("Accepted "+reason);}catch(FormatException){check(true,reason);}}
        Reject(()=>Build(new[]{0}),"all bones selected as physics");
        var selectedSource=Build(new[]{2});
        check(selectedSource.BoneCount==3 && selectedSource.Warnings.Any(w=>w.Contains("Physics append follower")),"selected physics grant source excludes follower just like intrinsic physics");
        model.Bones[2].Flags|=0x100; model.Bones[2].GrantParent=1; model.Bones[2].GrantWeight=1;
        var cyclic=Build(Array.Empty<int>());
        check(cyclic.BoneCount==5 && cyclic.Warnings.Count(w=>w.Contains("Disabled cyclic append"))==2,
            "cyclic append operations are isolated while all ordinary bone transforms remain");
        model.Bones[2].Flags=2;model.Bones[2].Name="root";
        Reject(()=>Build(Array.Empty<int>()),"ambiguous original motion names");
        model.Bones[2].Name="source"; model.Bones[1].Flags|=0x200;
        var combined=JsonDocument.Parse(Build(Array.Empty<int>()).MappingJson).RootElement.GetProperty("bones").EnumerateArray().Single(b=>b.GetProperty("vmdName").GetString()=="append");
        check(combined.GetProperty("trackData")[0].GetString()=="rp" && combined.GetProperty("movable").GetBoolean(),"rotation and position grant resets and syncs translation even without the movable flag");
        model.Bones[1].Flags=0x200;model.Bones[1].GrantWeight=-.25f;
        var position=JsonDocument.Parse(Build(Array.Empty<int>()).MappingJson).RootElement.GetProperty("bones").EnumerateArray().Single(b=>b.GetProperty("vmdName").GetString()=="append");
        check(position.GetProperty("trackData")[0].GetString()=="p" && position.GetProperty("trackData")[1].GetDouble()==-.25 && !position.GetProperty("rotatable").GetBoolean(),"position-only negative grant does not introduce rotation");
        model.Bones[1].Flags|=0x80;
        Reject(()=>Build(Array.Empty<int>()),"local append still requires world-space semantics");
        model.Bones[1].GrantParent=-1;
        var orphan=JsonDocument.Parse(Build(Array.Empty<int>()).MappingJson).RootElement.GetProperty("bones");
        check(!orphan.EnumerateArray().Single(b=>b.GetProperty("vmdName").GetString()=="append").TryGetProperty("isTrack",out _),"missing append source is a no-op, no pmx_-001 dependency");
        model.Bones[1].GrantParent=2;model.Bones[1].GrantWeight=0;
        check(Build(new[]{2}).BoneCount==4,"zero-weight append has no dependency on excluded source");
        model.Bones[1].Flags=0x22;model.Bones[1].IkTarget=2;model.Bones[1].IkIterations=3;model.Bones[1].IkAngle=1;
        model.Bones[1].IkLinks.Add(new PmxModel.IkLink {Bone=0,Limited=true,Min=new PmxModel.V3(-.35f,-1.05f,0),Max=new PmxModel.V3(.61f,1.05f,0)});
        var toe=JsonDocument.Parse(Build(Array.Empty<int>()).MappingJson).RootElement.GetProperty("bones").EnumerateArray().Single(b=>b.GetProperty("vmdName").GetString()=="append");
        var limits=toe.GetProperty("ikSolverSetting").GetProperty("limits")[0];
        check(toe.GetProperty("name").GetString()!.StartsWith("assetimport_pmx_") && limits[0][0].GetDouble()<0 && limits[0][1].GetDouble()>0 && limits[1][1].GetDouble()>60 && limits[2][1].GetDouble()==0,"toe IK retains signed X/Y ranges and locked Z for scoped runtime solver");
        model.Bones[1].IkLinks[0].Max.X=-1;
        Reject(()=>Build(Array.Empty<int>()),"inverted IK limits");
        model.Bones[1].Flags=0x102;model.Bones[1].GrantParent=2;model.Bones[1].GrantWeight=.6f;
        model.PhysicsBones.Add(2);
        var physicsFollower=Build(Array.Empty<int>());
        check(physicsFollower.BoneCount==3 && physicsFollower.Warnings.Any(w=>w.Contains("Physics append follower")),"intrinsic physics grant follower is excluded with diagnostics, unrelated animation still exports");
        DependencyClosureChecks.Run(check);
    }
}
