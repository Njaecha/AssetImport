using AssetImport;
using Assimp;
using System.Text.Json;

// An explicit read/build/bind-pose/cache/texture probe for models whose MMDD
// mapping may be unsupported. Default tests still require mapping to succeed.
internal static class GeometryProbe
{
    internal static void Run(string[] paths)
    {
        if(paths.Length==0) throw new ArgumentException("--geometry-only requires one or more PMX paths");
        foreach(string path in paths) RunModel(path);
    }
    private static void RunModel(string path)
    {
        int checks=0;
        void Check(bool condition,string message) { checks++;if(!condition)throw new Exception(message); }
        var bytes=File.ReadAllBytes(path);var original=(byte[])bytes.Clone();
        var model=PmxReader.Read(bytes);var scene=PmxSceneBuilder.Build(model,true);
        var cached=PmxReader.Read((byte[])bytes.Clone());var restored=PmxSceneBuilder.Build(cached,true);
        Check(bytes.SequenceEqual(original),"input bytes unchanged");
        Check(model.Vertices.Length==cached.Vertices.Length && model.UnusedNormalsRepaired==cached.UnusedNormalsRepaired,"cached vertex count and normal repair count");
        Check(scene.MeshCount==restored.MeshCount && scene.Meshes.Sum(m=>m.FaceCount)*3==model.Indices.Length,"rendered triangle count agrees with source/cache");
        var matrices=new Dictionary<string,Matrix4x4>();
        void Walk(Node node,Matrix4x4 parent) { var world=parent*node.Transform;matrices.Add(node.Name,world);foreach(var child in node.Children)Walk(child,world); }
        Walk(scene.RootNode,Matrix4x4.Identity);
        bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        for(int i=0;i<model.Vertices.Length;i++)
        {
            var a=model.Vertices[i].Normal;var b=cached.Vertices[i].Normal;
            Check(Finite(a.X) && Finite(a.Y) && Finite(a.Z),"finite parsed normal "+i);
            Check(a.X==b.X && a.Y==b.Y && a.Z==b.Z,"identical cached normal "+i);
        }
        for(int i=0;i<scene.MeshCount;i++)
        {
            var mesh=scene.Meshes[i];var copy=restored.Meshes[i];
            Check(mesh.Vertices.SequenceEqual(copy.Vertices) && mesh.Normals.SequenceEqual(copy.Normals) && mesh.TextureCoordinateChannels[0].SequenceEqual(copy.TextureCoordinateChannels[0]),"cached mesh positions, normals and UVs "+i);
            Check(mesh.Faces.SelectMany(f=>f.Indices).SequenceEqual(copy.Faces.SelectMany(f=>f.Indices)),"cached mesh topology "+i);
            var weights=new float[mesh.VertexCount];
            foreach(var bone in mesh.Bones)
            {
                var skin=matrices[bone.Name]*bone.OffsetMatrix;
                foreach(var weight in bone.VertexWeights)
                {
                    weights[weight.VertexID]+=weight.Weight;
                    var position=mesh.Vertices[weight.VertexID];
                    Check((skin*position-position).Length()<.0001f,"rest bind-pose drift");
                }
            }
            Check(weights.All(w=>Math.Abs(w-1)<.00001f),"normalized effective skin weights");
            Check(mesh.MeshAnimationAttachmentCount==copy.MeshAnimationAttachmentCount,"cached morph count");
            for(int j=0;j<mesh.MeshAnimationAttachmentCount;j++)
            {
                var morph=mesh.MeshAnimationAttachments[j];var cachedMorph=copy.MeshAnimationAttachments[j];
                Check(morph.Vertices.Count==mesh.VertexCount && morph.Name==cachedMorph.Name && morph.Vertices.SequenceEqual(cachedMorph.Vertices),"cached morph topology/name/deltas");
            }
        }
        var textures=scene.Materials.Where(m=>m.HasTextureDiffuse).Select(m=>m.TextureDiffuse.FilePath).Distinct().ToArray();
        var renderedTextures=new HashSet<string>(scene.Meshes.Select(m=>scene.Materials[m.MaterialIndex]).Where(m=>m.HasTextureDiffuse).Select(m=>m.TextureDiffuse.FilePath));
        int loaded=0, decoded=0;var missingTextures=new List<string>();
        foreach(string texture in textures)
        {
            string resolved=Path.Combine(Path.GetDirectoryName(path)!,texture.Replace('\\','/'));
            if(!File.Exists(resolved)) { missingTextures.Add(texture);continue; }
            var image=TextureImageData.Read(resolved);Check(image.Length>0,"texture loaded/decoded: "+texture);loaded++;
            if(new[]{".tga",".bmp",".dds"}.Contains(Path.GetExtension(resolved).ToLowerInvariant())) decoded++;
        }
        string mappingStatus;
        try
        {
            var renderers=scene.Meshes.SelectMany(m=>m.MeshAnimationAttachments.Select(a=>new PmxMorphTarget {
                Renderer=m.Name+"_"+scene.Materials[m.MaterialIndex].Name,Shape=a.Name }));
            var mapping=PmxMmddMapping.Build(model,bytes,Path.GetFileNameWithoutExtension(path),renderers,Array.Empty<int>());
            mappingStatus="PASS: "+mapping.BoneCount+" bones";
        }
        catch(FormatException error) { mappingStatus="UNSUPPORTED (separate from geometry import): "+error.Message; }
        Console.WriteLine(JsonSerializer.Serialize(new {
            model=Path.GetFileName(path),result="OFFLINE GEOMETRY PASS",vertices=model.Vertices.Length,triangles=scene.Meshes.Sum(m=>m.FaceCount),
            meshes=scene.MeshCount,bones=model.Bones.Length,morphs=model.Morphs.Length,rigidBodies=model.RigidBodies,joints=model.Joints,
            unusedNormalsRepaired=model.UnusedNormalsRepaired,invalidIgnoredMetadataFields=model.InvalidIgnoredMetadataFields,
            ignoredMetadataWarnings=model.IgnoredMetadataWarnings,cacheNormalsIdentical=true,loadedDiffuseTextures=loaded,decodedLegacyTextures=decoded,
            requiredDiffuseTextures=renderedTextures.Count,missingRenderTextures=missingTextures.Where(renderedTextures.Contains).ToArray(),
            textureStatus=missingTextures.Any(renderedTextures.Contains) ? "INCOMPLETE: required texture files missing" : "Available required textures read/converted",
            missingTextureMaterials=scene.Meshes.Select(m=>m.MaterialIndex).Distinct().Where(i=>scene.Materials[i].HasTextureDiffuse && missingTextures.Contains(scene.Materials[i].TextureDiffuse.FilePath)).Select(i=>new {
                index=i,name=model.Materials[i].Name,texture=scene.Materials[i].TextureDiffuse.FilePath,
                alpha=model.Materials[i].Diffuse[3],indexCount=model.Materials[i].IndexCount }),
            unusedMaterialMissingTextures=missingTextures.Where(t=>!renderedTextures.Contains(t)).ToArray(),
            mapping=mappingStatus,checks,windowsGame="NOT RUN"
        },new JsonSerializerOptions { WriteIndented=true }));
    }
}
