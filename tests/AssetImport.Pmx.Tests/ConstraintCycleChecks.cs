using AssetImport;
using System.Text.Json;

internal static class ConstraintCycleChecks
{
    internal static void Run(Action<bool,string> check)
    {
        for(int seed=0;seed<48;seed++)
        {
            const int count=18;
            var random=new Random(seed);
            int[] order=Enumerable.Range(0,count).OrderBy(_=>random.Next()).ToArray();
            int[] index=new int[count];for(int i=0;i<count;i++)index[order[i]]=i;
            var bones=Enumerable.Range(0,count).Select(i=>new PmxModel.Bone {Name="constraint_"+seed+"_"+i,
                Parent=i==0?-1:index[random.Next(i)],Flags=6}).ToArray();
            foreach(var bone in bones)
            {
                if(random.Next(2)==0){bone.Flags|=0x100;bone.GrantParent=random.Next(count);bone.GrantWeight=.5f;}
                if(random.Next(3)==0)
                {
                    bone.Flags|=0x20;bone.IkIterations=3;bone.IkAngle=1;bone.IkTarget=random.Next(count);
                    bone.IkLinks.Add(new PmxModel.IkLink {Bone=random.Next(count)});
                }
            }
            var model=new PmxModel {Bones=order.Select(i=>bones[i]).ToArray(),Morphs=Array.Empty<PmxModel.Morph>()};
            // Independent transitive-closure oracle (not the production SCC traversal).
            var reach=new bool[count,count];
            for(int i=0;i<count;i++)
            {
                var b=model.Bones[i];if(b.Parent>=0)reach[i,b.Parent]=true;
                if(b.GrantParent>=0)reach[i,b.GrantParent]=true;
                if((b.Flags&0x20)!=0){reach[i,b.IkTarget]=true;foreach(var link in b.IkLinks)reach[i,link.Bone]=true;}
            }
            for(int k=0;k<count;k++)for(int i=0;i<count;i++)for(int j=0;j<count;j++)reach[i,j]|=reach[i,k]&&reach[k,j];
            for(int i=0;i<count;i++)reach[i,i]=true;
            var plan=PmxAnimationPlan.Build(model);
            int disabled=0;
            for(int i=0;i<count;i++)
            {
                var b=model.Bones[i];bool grant=b.GrantParent>=0,ik=(b.Flags&0x20)!=0;
                bool cyclicGrant=grant&&reach[b.GrantParent,i];
                bool cyclicIk=ik&&(reach[b.IkTarget,i]||b.IkLinks.Any(link=>reach[link.Bone,i]));
                check(plan.Grant[i]==(grant&&!cyclicGrant) && plan.Ik[i]==(ik&&!cyclicIk),"SCC plan matches reachability oracle, seed="+seed+", bone="+i);
                disabled+=(cyclicGrant?1:0)+(cyclicIk?1:0);
            }
            var mapping=PmxMmddMapping.Build(model,new byte[]{9},"random dependency graph",Array.Empty<PmxMorphTarget>(),Array.Empty<int>());
            var definitions=JsonDocument.Parse(mapping.MappingJson).RootElement.GetProperty("bones").EnumerateArray().ToArray();
            check(mapping.BoneCount==count && mapping.IkCount==plan.Ik.Count(v=>v),"cyclic optional operations preserve all bones and unaffected IK");
            check(JsonDocument.Parse(mapping.ReportJson).RootElement.GetProperty("disabled_constraints").GetArrayLength()==disabled,"every skipped operation has an explicit diagnostic");
            foreach(var definition in definitions)
            {
                int i=int.Parse(definition.GetProperty("name").GetString()!.Split('_').Last());
                check(definition.TryGetProperty("isTrack",out _)==plan.Grant[i],"only safe append operations exported");
                check(definition.TryGetProperty("isIK",out _)==plan.Ik[i],"only safe IK operations exported");
            }
        }
    }
}
