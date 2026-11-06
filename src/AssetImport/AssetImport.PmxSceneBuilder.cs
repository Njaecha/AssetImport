using System;
using System.Collections.Generic;
using Assimp;
#if KK
using Vec = Assimp.Vector3D;
using Mat = Assimp.Matrix4x4;
using Colour = Assimp.Color4D;
#else
using Vec = System.Numerics.Vector3;
using Mat = System.Numerics.Matrix4x4;
using Colour = System.Numerics.Vector4;
#endif

namespace AssetImport
{
    internal static class PmxSceneBuilder
    {
        // PMX/MMD units -> KK MMDD units. Rotate 180 degrees around Y once here;
        // FinishLoadProcess must not apply the generic import's extra rotation.
        internal const float UnitScale = .0832f;
        internal static Vec Convert(PmxModel.V3 v, bool scale)
        { float s=scale ? UnitScale : 1; return new Vec(-v.X*s,v.Y*s,-v.Z*s); }
        private static Mat Translation(Vec v)
        {
#if KK
            return Mat.FromTranslation(v);
#else
            // AssimpNetter stores Assimp matrices by rows, unlike Numerics' TRS.
            Mat result=Mat.Identity; result.M14=v.X; result.M24=v.Y; result.M34=v.Z; return result;
#endif
        }
        internal static Scene Build(PmxModel model, bool morphs)
        {
            var scene=new Scene { RootNode=new Node("PMX_Model") };
            var nodes=new Node[model.Bones.Length];
            for (int i=0;i<nodes.Length;i++) nodes[i]=new Node(PmxModel.BoneName(i,model.Bones[i].Name));
            for (int i=0;i<nodes.Length;i++)
            {
                var b=model.Bones[i]; var pos=b.Position;
                if (b.Parent>=0) pos=PmxModel.V3.Add(pos,PmxModel.V3.Mul(model.Bones[b.Parent].Position,-1));
                nodes[i].Transform=Translation(Convert(pos,true));
                (b.Parent>=0 ? nodes[b.Parent] : scene.RootNode).Children.Add(nodes[i]);
            }
            model.MorphWarnings.Clear();
            var morphData=morphs ? FlattenMorphs(model) : new Dictionary<int,PmxModel.V3>[model.Morphs.Length];
            int indexStart=0;
            for (int mi=0;mi<model.Materials.Length;mi++)
            {
                var source=model.Materials[mi];
                var material=new Material { Name="PMX_M"+mi.ToString("D3")+"_"+source.Name,
                    ColorDiffuse=new Colour(source.Diffuse[0],source.Diffuse[1],source.Diffuse[2],source.Diffuse[3]), IsTwoSided=source.TwoSided };
                if (source.Texture>=0) material.TextureDiffuse=new TextureSlot(model.Textures[source.Texture],TextureType.Diffuse,0,TextureMapping.FromUV,0,1,TextureOperation.Multiply,TextureWrapMode.Wrap,TextureWrapMode.Wrap,0);
                scene.Materials.Add(material);
                var mesh=new Mesh("PMX_Mesh"+mi.ToString("D3"),PrimitiveType.Triangle) { MaterialIndex=mi };
                var originals=new List<int>(); var localIndices=new Dictionary<int,int>(); var influences=new Dictionary<int,List<VertexWeight>>();
                for (int f=0;f<source.IndexCount;f+=3)
                {
                    var face=new Face();
                    // ProcessMeshes reverses Assimp faces on the final Unity mesh.
                    for (int k=2;k>=0;k--)
                    {
                        int original=model.Indices[indexStart+f+k],local;
                        if (!localIndices.TryGetValue(original,out local))
                        {
                            local=originals.Count; originals.Add(original); localIndices.Add(original,local);
                            var v=model.Vertices[original]; mesh.Vertices.Add(Convert(v.Position,true)); mesh.Normals.Add(Convert(v.Normal,false));
                            mesh.TextureCoordinateChannels[0].Add(new Vec(v.UV.X,1-v.UV.Y,0));
                            for (int w=0;w<v.Bones.Length;w++)
                            {
                                if (v.Weights[w]<=0) continue;
                                List<VertexWeight> weights;
                                if (!influences.TryGetValue(v.Bones[w],out weights)) { weights=new List<VertexWeight>(); influences.Add(v.Bones[w],weights); }
                                weights.Add(new VertexWeight(local,v.Weights[w]));
                            }
                        }
                        face.Indices.Add(local);
                    }
                    mesh.Faces.Add(face);
                }
                indexStart+=source.IndexCount; mesh.UVComponentCount[0]=2;
                // Include only effective skin influences in each renderer. Unweighted
                // controller bones still live in the node tree and the import UI.
                var keys=new List<int>(influences.Keys); keys.Sort();
                foreach (int index in keys)
                {
                    var bone=new Bone { Name=nodes[index].Name, OffsetMatrix=Translation(Convert(PmxModel.V3.Mul(model.Bones[index].Position,-1),true)) };
                    bone.VertexWeights.AddRange(influences[index]); mesh.Bones.Add(bone);
                }
                if (morphs) for (int i=0;i<morphData.Length;i++)
                {
                    var deltas=morphData[i]; if (deltas==null || deltas.Count==0) continue;
                    bool relevant=false; foreach (int original in originals) if (deltas.ContainsKey(original)) { relevant=true; break; }
                    if (!relevant) continue;
                    var attachment=new MeshAnimationAttachment { Name=model.Morphs[i].Name, Weight=1 };
                    foreach (int original in originals)
                    {
                        PmxModel.V3 delta; deltas.TryGetValue(original,out delta);
                        attachment.Vertices.Add(Convert(PmxModel.V3.Add(model.Vertices[original].Position,delta),true));
                    }
                    mesh.MeshAnimationAttachments.Add(attachment);
                }
                if (mesh.VertexCount==0) continue;
                var node=new Node("PMX_Part"+mi.ToString("D3")); node.MeshIndices.Add(scene.Meshes.Count);
                scene.Meshes.Add(mesh); scene.RootNode.Children.Add(node);
            }
            return scene;
        }
        internal static void RefreshMorphWarnings(PmxModel model)
        {
            // Repair reads saved PMX bytes without rebuilding meshes. Recover the
            // same omission diagnostics so the new report does not hide them.
            model.MorphWarnings.Clear();
            FlattenMorphs(model);
        }
        // Resolve a dependency graph, not recursive traversal. This also makes deep
        // acyclic groups safe and memoizes unsupported results. Cycles and all their
        // dependent groups stay unresolved; independent vertex/group morphs survive.
        private static Dictionary<int,PmxModel.V3>[] FlattenMorphs(PmxModel model)
        {
            int count=model.Morphs.Length;
            var cache=new Dictionary<int,PmxModel.V3>[count];
            var remaining=new int[count];
            var parents=new List<int>[count];
            for (int i=0;i<count;i++) parents[i]=new List<int>();
            for (int i=0;i<count;i++)
            {
                if (model.Morphs[i].Type!=0) continue;
                var unique=new HashSet<int>();
                foreach (var group in model.Morphs[i].Groups)
                {
                    if (group.Value==0) continue;
                    if (group.Key<0 || group.Key>=count) throw new FormatException("PMX: invalid group morph index");
                    if (unique.Add(group.Key)) { remaining[i]++; parents[group.Key].Add(i); }
                }
            }
            var ready=new Queue<int>();
            for (int i=0;i<count;i++) if (remaining[i]==0) ready.Enqueue(i);
            while (ready.Count>0)
            {
                int index=ready.Dequeue();
                var morph=model.Morphs[index];
                if (morph.Type==1) cache[index]=morph.Deltas;
                else if (morph.Type==0)
                {
                    var result=new Dictionary<int,PmxModel.V3>();
                    string reason=null;
                    foreach (var group in morph.Groups)
                    {
                        if (group.Value==0) continue;
                        var child=cache[group.Key];
                        // An incomplete group must not appear to be a working morph.
                        if (child==null) { reason="depends on unsupported/omitted morph #"+group.Key; break; }
                        foreach (var delta in child)
                        {
                            PmxModel.V3 old; result.TryGetValue(delta.Key,out old);
                            var value=PmxModel.V3.Add(old,PmxModel.V3.Mul(delta.Value,group.Value));
                            if (!Finite(value.X) || !Finite(value.Y) || !Finite(value.Z))
                            { reason="non-finite combined vertex offset"; break; }
                            result[delta.Key]=value;
                        }
                        if (reason!=null) break;
                    }
                    if (reason==null) cache[index]=result;
                    else MorphWarning(model,index,reason);
                }
                foreach (int parent in parents[index]) if (--remaining[parent]==0) ready.Enqueue(parent);
            }
            for (int i=0;i<count;i++)
                if (remaining[i]>0) MorphWarning(model,i,"cyclic group reference or dependency on a cycle");
            return cache;
        }
        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        private static void MorphWarning(PmxModel model,int index,string reason)
        {
            model.MorphWarnings.Add("Skipped group morph #"+index+" '"+model.Morphs[index].Name+"': "+reason);
        }
    }
}
