using AssetImport;
using System.Text.Json;

// Synthetic skeletons deliberately have no relationship to any supplied model.
// Reindex every reference and vary names/layers to catch file/order special cases.
internal static class DependencyClosureChecks
{
    internal static void Run(Action<bool,string> check)
    {
        for (int seed=0;seed<24;seed++)
        foreach (bool selected in new[]{false,true})
        foreach (bool targetDependency in new[]{false,true})
        {
            var random=new Random(seed);
            int[] order=Enumerable.Range(0,10).OrderBy(_=>random.Next()).ToArray();
            int[] index=new int[order.Length];
            for(int i=0;i<order.Length;i++) index[order[i]]=i;
            int Ref(int i)=>i<0 ? -1 : index[i];
            PmxModel.Bone Bone(int id,int parent)=>new PmxModel.Bone {
                Name="generated_"+seed+"_"+id,Parent=Ref(parent),Flags=6,Layer=random.Next(4)
            };
            var logical=Enumerable.Range(0,10).Select(i=>Bone(i,i==0 ? -1 : 0)).ToArray();
            // 1: physics source, 2: descendant, 3: first IK, 4: its child,
            // 5: append follower, 6: second IK, 7: append follower of that IK.
            logical[2].Parent=Ref(1);
            foreach(int id in new[]{3,6})
            {
                var b=logical[id]; b.Flags|=0x20; b.IkIterations=4; b.IkAngle=1;
                int dependency=id==3 ? 2 : 5;
                b.IkTarget=Ref(targetDependency ? dependency : 8);
                b.IkLinks.Add(new PmxModel.IkLink {Bone=Ref(targetDependency ? 8 : dependency)});
            }
            logical[4].Parent=Ref(3);
            foreach(int id in new[]{5,7})
            {
                // Unsupported append operations on excluded helpers must not
                // prevent an unrelated animation/morph branch from exporting.
                logical[id].Flags|=0x380; logical[id].GrantParent=Ref(id==5 ? 4 : 6); logical[id].GrantWeight=.5f;
            }
            // Zero weight never creates an ownership dependency.
            logical[9].Flags|=0x100; logical[9].GrantParent=Ref(7); logical[9].GrantWeight=0;
            var model=new PmxModel {Bones=order.Select(i=>logical[i]).ToArray(),Morphs=Array.Empty<PmxModel.Morph>()};
            if(!selected) model.PhysicsBones.Add(Ref(1));
            var result=PmxMmddMapping.Build(model,new byte[]{7},"synthetic graph",
                new[]{new PmxMorphTarget {Renderer="mesh",Shape="expression"}},selected ? new[]{Ref(1)} : Array.Empty<int>());
            using var json=JsonDocument.Parse(result.MappingJson);
            var bones=json.RootElement.GetProperty("bones").EnumerateArray().ToArray();
            var names=bones.Select(b=>b.GetProperty("vmdName").GetString()).ToHashSet();
            check(names.SetEquals(new[]{logical[0].Name,logical[8].Name,logical[9].Name}),"transitive physics/child/IK/append closure, seed="+seed+", selected="+selected+", target="+targetDependency);
            var keys=bones.Select(b=>b.GetProperty("name").GetString()).ToHashSet();
            check(bones.All(b=>!b.TryGetProperty("parent",out var p)||keys.Contains(p.GetString())),"export has no dangling parent references");
            check(result.IkCount==0 && result.MorphCount==1,"excluded IK helpers preserve unrelated morphs");
            check(result.Warnings.Count(w=>w.Contains("Physics append follower"))==2,"transitive append exclusions are diagnosed");
        }
    }
}
