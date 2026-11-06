using AssetImport;
using System.Text.Json;

// Exercise data classes with generated names and shuffled indices, independent of
// private files. A bad optional morph must not poison unrelated geometry or motion.
internal static class OptionalPmxDataChecks
{
    internal static void Run(byte[] fixture, Action<bool,string> check)
    {
        for (int seed=0;seed<24;seed++)
        {
            var random=new Random(seed);
            int[] order=Enumerable.Range(0,16).OrderBy(_=>random.Next()).ToArray();
            int[] index=new int[16];for(int i=0;i<16;i++)index[order[i]]=i;
            var morphs=Enumerable.Range(0,16).Select(i=>new PmxModel.Morph {Name="graph_"+seed+"_"+i,Type=0}).ToArray();
            void Edge(int from,int to,float weight)=>morphs[from].Groups.Add(new KeyValuePair<int,float>(index[to],weight));
            morphs[0].Type=morphs[1].Type=1;
            morphs[0].Deltas[0]=new PmxModel.V3(0,1,0);morphs[1].Deltas[0]=new PmxModel.V3(0,2,0);
            Edge(2,0,.5f);Edge(2,1,.25f);Edge(2,0,.5f); // repeated edge still sums all weights
            Edge(3,3,1);Edge(3,0,1); // self-cycle cannot partially expose the valid child
            Edge(4,5,1);Edge(5,4,1);Edge(5,1,1); // multi-node cycle
            Edge(6,3,1);Edge(6,2,1); // cycle ancestor
            Edge(7,2,2);Edge(7,3,0); // zero-weight edge does not depend on the cycle
            morphs[8].Type=2;Edge(9,0,1);Edge(9,8,1); // unsupported mixed group stays unavailable
            Edge(10,1,float.MaxValue);Edge(11,10,1);Edge(11,0,1); // finite inputs overflow only this branch
            Edge(12,1,-.5f);Edge(13,0,0);Edge(14,13,1);Edge(14,1,1);Edge(15,12,.5f);
            var model=PmxReader.Read(fixture);model.Morphs=order.Select(i=>morphs[i]).ToArray();
            var before=morphs.Select(m=>m.Groups.ToArray()).ToArray();
            var scene=PmxSceneBuilder.Build(model,true);var mesh=scene.Meshes.Single();
            var expected=new Dictionary<int,float>{{0,1},{1,2},{2,1.5f},{7,3},{12,-1},{14,2},{15,-.5f}};
            check(mesh.MeshAnimationAttachmentCount==expected.Count,"only complete supported morphs survive graph, seed="+seed);
            foreach(var pair in expected)
            {
                var shape=mesh.MeshAnimationAttachments.Single(m=>m.Name==morphs[pair.Key].Name);
                float sum=shape.Vertices.Zip(mesh.Vertices,(a,b)=>(a.Y-b.Y)/PmxSceneBuilder.UnitScale).Sum();
                check(Math.Abs(sum-pair.Value)<.0001f,"weighted/repeated/negative/zero-edge group is exact, seed="+seed);
            }
            check(model.MorphWarnings.Count==7,"cycles, ancestors, unsupported mix and overflow diagnosed once each");
            check(model.MorphWarnings.Count(w=>w.Contains("cyclic"))==4,"only cycle members and dependents are quarantined");
            check(morphs.Select((m,i)=>m.Groups.SequenceEqual(before[i])).All(v=>v),"source morph graph is never rewritten");
            var saved=PmxSceneBuilder.Build(model,true);
            check(saved.Meshes[0].MeshAnimationAttachments.SelectMany(m=>m.Vertices).SequenceEqual(mesh.MeshAnimationAttachments.SelectMany(m=>m.Vertices)) && model.MorphWarnings.Count==7,"rebuilding is deterministic without accumulating warnings");
            var plain=PmxSceneBuilder.Build(model,false).Meshes.Single();
            check(plain.Vertices.SequenceEqual(mesh.Vertices) && plain.Normals.SequenceEqual(mesh.Normals) && plain.Faces.SelectMany(f=>f.Indices).SequenceEqual(mesh.Faces.SelectMany(f=>f.Indices)),"optional morph errors do not alter mesh geometry/topology");
            PmxSceneBuilder.RefreshMorphWarnings(model);
            check(model.MorphWarnings.Count==7,"repair recovers omission diagnostics without rebuilding geometry");
            CheckInactiveIk(seed,check);
        }
        // A recursive implementation can overflow on otherwise valid, deep groups.
        var deep=PmxReader.Read(fixture);
        deep.Morphs=Enumerable.Range(0,4096).Select(i=>new PmxModel.Morph{Name="depth_"+i,Type=i==4095?1:0}).ToArray();
        deep.Morphs[4095].Deltas[0]=new PmxModel.V3(0,1,0);
        for(int i=0;i<4095;i++)deep.Morphs[i].Groups.Add(new KeyValuePair<int,float>(i+1,1));
        check(PmxSceneBuilder.Build(deep,true).Meshes[0].MeshAnimationAttachmentCount==4096 && deep.MorphWarnings.Count==0,"4096 nested groups resolve without recursion or dropping valid shapes");
    }

    private static void CheckInactiveIk(int seed,Action<bool,string> check)
    {
        foreach(int mode in new[]{0,1,2}) foreach(bool physics in new[]{false,true})
        {
            var random=new Random(seed);
            int[] order=Enumerable.Range(0,9).OrderBy(_=>random.Next()).ToArray();
            int[] index=new int[9];for(int i=0;i<9;i++)index[order[i]]=i;
            var bones=Enumerable.Range(0,9).Select(i=>new PmxModel.Bone{Name="ik_"+seed+"_"+i,Parent=i==0?-1:index[0],Flags=6}).ToArray();
            bones[2].Parent=bones[7].Parent=index[1];bones[6].Parent=index[5];
            foreach(int id in new[]{1,4})
            {
                bones[id].Flags|=0x20;bones[id].IkIterations=4;bones[id].IkAngle=1;
                bones[id].IkTarget=index[id==1?2:6];
                bones[id].IkLinks.Add(new PmxModel.IkLink {Bone=index[id==1?3:5]});
            }
            if(mode==0)bones[1].IkAngle=0;else if(mode==1)bones[1].IkIterations=0;else bones[1].IkLinks.Clear();
            bones[8].Flags|=0x100;bones[8].GrantParent=index[1];bones[8].GrantWeight=.5f;
            var model=new PmxModel{Bones=order.Select(i=>bones[i]).ToArray(),Morphs=Array.Empty<PmxModel.Morph>()};
            if(physics)model.PhysicsBones.Add(index[3]);
            var result=PmxMmddMapping.Build(model,new byte[]{1},"generated inactive IK",Array.Empty<PmxMorphTarget>(),Array.Empty<int>());
            using var document=JsonDocument.Parse(result.MappingJson);
            var definitions=document.RootElement.GetProperty("bones").EnumerateArray().ToArray();
            var helper=definitions.Single(b=>b.GetProperty("vmdName").GetString()==bones[1].Name);
            check(!helper.TryGetProperty("isIK",out _) && helper.GetProperty("movable").GetBoolean() && helper.GetProperty("rotatable").GetBoolean(),"inactive IK preserves controller motion channels");
            check(result.IkCount==1 && result.BoneCount==(physics?8:9),"inactive links add neither fake cycles nor physics exclusions; valid IK retained");
            check(result.Warnings.Count(w=>w.Contains("Inactive IK"))==1 && JsonDocument.Parse(result.ReportJson).RootElement.GetProperty("inactive_ik").GetArrayLength()==1,"inactive IK gets structured diagnostics with remapped identity");
            check(bones[1].Flags==0x26 && bones[2].Parent==index[1],"source IK flag/hierarchy unchanged");
        }
    }
}
