using AssetImport;
using Assimp;
using System.Text;
using System.Text.Json;

if(args.Length>0 && args[0]=="--image")
{
    File.WriteAllBytes(args[2],TextureImageData.Read(args[1]));
    Console.WriteLine("Converted: "+args[1]); return;
}
if(args.Length>0 && args[0]=="--geometry-only")
{
    GeometryProbe.Run(args.Skip(1).ToArray()); return;
}
if(args.Length>0 && args[0]=="--lut")
{
    using var reader = File.OpenText(args[1]); var lut = CubeLut.Read(reader);
    byte[] pixels = {0,0,0,7,255,255,255,64}; lut.Apply(pixels);
    if (!pixels.SequenceEqual(new byte[]{66,82,66,7,249,249,249,64})) throw new Exception("User PS-to-KK LUT endpoints changed: " + string.Join(",",pixels));
    Console.WriteLine("PASS: supplied PS-to-KK LUT, endpoints and alpha."); return;
}
int checks=0;
void Check(bool value,string message) { checks++; if(!value) throw new Exception(message); }
void Reject(byte[] data,string label) { try { PmxReader.Read(data); throw new Exception("Accepted "+label); } catch(FormatException) { checks++; } }
var fixture=Fixture();
var model=PmxReader.Read(fixture);
var scene=PmxSceneBuilder.Build(model,true);
Check(scene.MeshCount==1 && scene.RootNode.Children.Count==2,"triangle and root node");
var root=scene.RootNode.Children.First(n=>n.Name.StartsWith("PMX_0000"));
Check(Math.Abs(root.Transform.A4 + 2*.0832f)<1e-6,"Nonzero root position retained and coordinate converted");
Check(scene.Meshes[0].Faces[0].Indices.SequenceEqual(new[]{0,1,2}),"face uses remapped reversed indices");
Check(scene.Meshes[0].Vertices[0].Y>scene.Meshes[0].Vertices[1].Y,"winding reverses once before Unity conversion");
Check(Math.Abs(scene.Meshes[0].TextureCoordinateChannels[0][0].Y-.7f)<1e-6,"PMX texture V origin");
Check(scene.Materials[0].IsTwoSided,"two-sided PMX flag");
Check(scene.Meshes[0].MeshAnimationAttachments[0].Vertices[0].Y>scene.Meshes[0].Vertices[0].Y,"vertex morph preserved");
ValidateSkin(scene);
for(int i=0;i<fixture.Length;i++) Reject(fixture[..i],"truncated PMX");
var invalid=(byte[])fixture.Clone(); BitConverter.GetBytes(2.1f).CopyTo(invalid,4); Reject(invalid,"PMX 2.1");
invalid=(byte[])fixture.Clone(); invalid[11]=3; Reject(invalid,"invalid index width");
// Additional fixtures change the wire parent index (recorded by the builder).
Reject(Fixture(true),"cyclic bone hierarchy");
Check(PmxReader.Read(Fixture(false,true)).Bones[0].Name=="親", "UTF-16 text");
TgaTests();
TextureDecoderChecks.Run(Check);
CubeLutChecks.Run(Check);
ImportTextureChecks.Run(Check);
MappingChecks.Run(Check);
OptionalPmxDataChecks.Run(fixture,Check);
ConstraintCycleChecks.Run(Check);
RigidBodyReaderChecks.Run(fixture, Check);
JointChecks.Run(fixture, Check);
NativeContactPlanChecks.Run(Check);
UnusedNormalChecks.Run(Check);
IgnoredMetadataChecks.Run(Check);
CollisionPlanChecks.Run(Check);
CollisionRuntimeChecks.Run(Check);
PhysicsRootSuggestionChecks.Run(Check);
PhysicsRootSelectionChecks.Run(Check);
PhysicsImportOptionsChecks.Run(Check);
PhysicsPresetBindingChecks.Run(Check);
var fixtureMap=PmxMmddMapping.Build(model,fixture,"fixture",Array.Empty<PmxMorphTarget>(),Array.Empty<int>());
Check(fixtureMap.BoneCount==1 && fixtureMap.IkCount==0,"automatic mapping handles a simple original PMX");
Check(JsonDocument.Parse(fixtureMap.MappingJson).RootElement.GetProperty("bones")[0].GetProperty("vmdName").GetString()=="親","automatic mapping retains original motion name");
if(args.Length>0)
{
    var bytes=File.ReadAllBytes(args[0]); var pmx=PmxReader.Read(bytes); var converted=PmxSceneBuilder.Build(pmx,true);
    NativeContactPlanChecks.Corpus(pmx, Check);
    ValidateSkin(converted);
    // Same serialized bytes are used by AssetFile/RamCache on scene reload.
    var reloaded=PmxSceneBuilder.Build(PmxReader.Read((byte[])bytes.Clone()),true);
    Check(reloaded.Meshes.Sum(m=>m.FaceCount)==converted.Meshes.Sum(m=>m.FaceCount),"cached blob topology");
    var actualRenderers=converted.Meshes.SelectMany(m=>LegacyMeshGeometry.Partition(m.VertexCount,m.Faces.SelectMany(f=>f.Indices).ToArray()).Select(part=>new {
        name=m.Name+"_"+converted.Materials[m.MaterialIndex].Name,shapes=m.MeshAnimationAttachments.Select(a=>a.Name).ToArray() })).ToArray();
    var mapping=PmxMmddMapping.Build(pmx,bytes,Path.GetFileNameWithoutExtension(args[0]),actualRenderers.SelectMany(r=>r.shapes.Select(s=>new PmxMorphTarget {Renderer=r.name,Shape=s})),Array.Empty<int>());
    var physicsRoots=pmx.PhysicsBones.Where(i=>!pmx.PhysicsBones.Contains(pmx.Bones[i].Parent)).OrderBy(i=>i).ToArray();
    var suggestedRoots=PmxPhysicsRootSuggestions.Build(pmx);
    bool Below(int bone,int ancestor) { for(int b=bone;b>=0;b=pmx.Bones[b].Parent) if(b==ancestor)return true; return false; }
    var physicsChains=physicsRoots.Select(root=>Enumerable.Range(0,pmx.Bones.Length).Where(i=>Below(i,root)).ToArray()).ToArray();
    var collisionPlan=PmxCollisionPlan.Build(pmx,physicsChains);
    var restoredPmx=PmxReader.Read((byte[])bytes.Clone());
    var restoredPlan=PmxCollisionPlan.Build(restoredPmx,physicsChains);
    var jointPlan=PmxJointPlan.Build(pmx);
    var restoredJoints=PmxJointPlan.Build(restoredPmx);
    Check(pmx.JointData.Length==pmx.Joints && jointPlan.Angular.Select(j=>j.Index).SequenceEqual(restoredJoints.Angular.Select(j=>j.Index)),
        "cached blob reconstructs the same supported joint constraints");
    Check(pmx.RigidBodyData.Length==pmx.RigidBodies,"full rigid-body table retained from source/cache blob");
    Check(collisionPlan.Colliders.Select(c=>c.Index).SequenceEqual(restoredPlan.Colliders.Select(c=>c.Index)),"cached blob reconstructs same collider list");
    for(int i=0;i<physicsChains.Length;i++) Check(collisionPlan.Chains[i].ColliderIndices.SequenceEqual(restoredPlan.Chains[i].ColliderIndices),"cached blob reconstructs same per-chain collision filters");
    var collisionReport=new { bodies=pmx.RigidBodyData.Select((b,i)=>new { index=i,b.Name,b.Bone,b.Mode,b.Group,b.CollisionMask,b.Shape,
        size=new[]{b.Size.X,b.Size.Y,b.Size.Z},position=new[]{b.Position.X,b.Position.Y,b.Position.Z},rotation=new[]{b.Rotation.X,b.Rotation.Y,b.Rotation.Z} }),
        colliders=collisionPlan.Colliders.Select(c=>new { c.Index,c.Bone,c.Name,c.Radius,c.Height,c.Direction,c.BoxApproximation,c.Rotation,
            localPosition=new[]{c.LocalPosition.X,c.LocalPosition.Y,c.LocalPosition.Z} }),
        chains=physicsRoots.Select((root,i)=>new { root, name=PmxModel.BoneName(root,pmx.Bones[root].Name),
            bodies=collisionPlan.Chains[i].ColliderIndices.Select(c=>collisionPlan.Colliders[c].Index),
            collisionPlan.Chains[i].ParticleRadius,collisionPlan.Chains[i].HasPhysicsMetadata,collisionPlan.Chains[i].MixedFilters }),
        warnings=collisionPlan.Warnings,windowsGame="NOT RUN" };
    var textures=converted.Materials.Where(m=>m.HasTextureDiffuse).Select(m=>m.TextureDiffuse.FilePath).Distinct().ToArray();
    var missingTextures=textures.Where(texture=>!File.Exists(Path.Combine(Path.GetDirectoryName(args[0])!,texture.Replace('\\','/')))).ToArray();
    foreach(var missing in missingTextures) Console.WriteLine("INPUT WARNING: missing diffuse texture: "+missing);
    var report=new { result="OFFLINE PASS", vertices=pmx.Vertices.Length, bones=pmx.Bones.Length, meshes=converted.MeshCount,
        triangles=converted.Meshes.Sum(m=>m.FaceCount), vertexMorphs=pmx.Morphs.Count(m=>m.Type==1),
        morphAttachments=converted.Meshes.Sum(m=>m.MeshAnimationAttachmentCount), rigidBodies=pmx.RigidBodies,joints=pmx.Joints,
        sdefApproximated=pmx.SdefVertices, invalidIgnoredMetadataFields=pmx.InvalidIgnoredMetadataFields,
        ignoredMetadataWarnings=pmx.IgnoredMetadataWarnings, morphWarnings=pmx.MorphWarnings, checks, windowsGame="NOT RUN",
        boneNames=pmx.Bones.Select((b,i)=>PmxModel.BoneName(i,b.Name)),textures,missingTextures,
        renderers=actualRenderers };
    if(args.Length>1)
    {
        Directory.CreateDirectory(args[1]);
        File.WriteAllText(Path.Combine(args[1],"joint-validation.json"),JsonSerializer.Serialize(new {
            total=pmx.Joints, supported=jointPlan.Angular.Count,
            selected=jointPlan.Angular.Count(j=>physicsChains.Any(c=>c.Contains(j.Bone))),
            angular=jointPlan.Angular.Select(j=>new {j.Index,j.Parent,j.Bone,j.Reverse}),
            warnings=jointPlan.Warnings,windowsGame="NOT RUN"
        },new JsonSerializerOptions {WriteIndented=true}));
        File.WriteAllText(Path.Combine(args[1],"pmx-validation.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions { WriteIndented=true }));
        File.WriteAllText(Path.Combine(args[1],"collision-validation.json"),JsonSerializer.Serialize(collisionReport,new JsonSerializerOptions { WriteIndented=true, NumberHandling=System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals }));
        File.WriteAllText(Path.Combine(args[1],"physics-root-suggestions.json"),JsonSerializer.Serialize(new {
            hairRoots=suggestedRoots.HairRoots,
            physicsRoots=suggestedRoots.PhysicsRoots,
            candidates=suggestedRoots.Candidates.Select(c=>new { c.Bone,c.Name,c.DynamicBoneCount,c.IsHair,c.IsUsable,c.Reason }),
            skippedRoots=suggestedRoots.SkippedRootCount,
            unboundBodies=suggestedRoots.IgnoredBodyCount,
            diagnostics=suggestedRoots.Diagnostics
        },new JsonSerializerOptions { WriteIndented=true }));
        string mapBase=Path.Combine(args[1],"item_AssetImport_PMX_"+mapping.ModelId);
        File.WriteAllText(mapBase+".json",mapping.MappingJson);
        File.WriteAllText(mapBase+".report.txt",mapping.ReportJson);
        using(var sr=new StreamReader(typeof(PmxModel).Assembly.GetManifestResourceStream("AssetImport.MmddMorphFunctions.py")!))
            File.WriteAllText(Path.Combine(args[1],"MmddExtMorphFunc_AssetImport_Pmx_"+mapping.ModelId+".py"),sr.ReadToEnd()+"\n\n_register("+mapping.TargetsJson+")\n");
        foreach(var texture in textures.Where(t=>t.EndsWith(".tga",StringComparison.OrdinalIgnoreCase)))
        {
            string path=Path.Combine(Path.GetDirectoryName(args[0])!,texture.Replace('\\','/'));
            if(File.Exists(path)) File.WriteAllBytes(Path.Combine(args[1],Path.GetFileName(texture.Replace('\\','/'))+".png"),TextureImageData.Read(path));
        }
    }
    Console.WriteLine($"Private model: {pmx.Vertices.Length} vertices, {pmx.Bones.Length} bones, {converted.MeshCount} materials, {pmx.Morphs.Count(m=>m.Type==1)} vertex morphs.");
    Console.WriteLine($"Auto mapping: {mapping.BoneCount} bones, {mapping.IkCount} IK, {mapping.MorphCount} morphs; {actualRenderers.Length} runtime renderer chunks.");
    Console.WriteLine($"Body collision plan: {collisionPlan.Colliders.Count} candidates, {physicsRoots.Length} natural physics chains; {collisionPlan.Chains.Count(c=>c.ColliderIndices.Length>0)} chains with permitted obstacles.");
    Console.WriteLine($"Suggested hair roots: {suggestedRoots.HairRoots.Length}; all physics roots: {suggestedRoots.PhysicsRoots.Length}.");
}
Console.WriteLine($"PASS: {checks} checks of production PMX parsing, scene/bind poses, automatic mapping, morphs and image decoding/PNG encoding; no Unity runtime.");

void ValidateSkin(Scene s)
{
    var matrices=new Dictionary<string,Matrix4x4>();
    void Walk(Node n,Matrix4x4 parent) { var w=parent*n.Transform; matrices.Add(n.Name,w); foreach(var c in n.Children) Walk(c,w); }
    Walk(s.RootNode,Matrix4x4.Identity);
    foreach(var mesh in s.Meshes)
    {
        var weights=new float[mesh.VertexCount];
        foreach(var bone in mesh.Bones)
        {
            var skin=matrices[bone.Name]*bone.OffsetMatrix;
            foreach(var w in bone.VertexWeights)
            {
                weights[w.VertexID]+=w.Weight;
                var pos=mesh.Vertices[w.VertexID]; var transformed=skin*pos;
                Check((transformed-pos).Length()<.0001f,"bind-pose drift");
            }
        }
        Check(weights.All(w=>Math.Abs(w-1)<.00001),"normalized effective weights");
        foreach(var morph in mesh.MeshAnimationAttachments) Check(morph.Vertices.Count==mesh.VertexCount,"morph vertex topology");
    }
}
byte[] Fixture(bool cycle=false,bool utf16=false)
{
    using var ms=new MemoryStream(); using var w=new BinaryWriter(ms);
    void Text(string s) { byte[] b=(utf16 ? Encoding.Unicode : Encoding.UTF8).GetBytes(s); w.Write(b.Length);w.Write(b); }
    void Vec(float x,float y,float z) { w.Write(x);w.Write(y);w.Write(z); }
    w.Write(Encoding.ASCII.GetBytes("PMX "));w.Write(2f);w.Write((byte)8);w.Write(new byte[]{(byte)(utf16?0:1),0,1,1,1,1,1,1});
    Text("Original fixture");Text("");Text("");Text("");
    w.Write(3);
    foreach(var p in new[]{new[]{2f,0,0},new[]{3f,0,0},new[]{2f,1,0}})
    { Vec(p[0],p[1],p[2]);Vec(0,0,-1);w.Write(.2f);w.Write(.3f);w.Write((byte)0);w.Write((byte)0);w.Write(1f); }
    w.Write(3);w.Write(new byte[]{0,1,2});w.Write(0); // indices and no textures
    w.Write(1); Text("paint");Text(""); foreach(var f in new[]{1f,1,1,1})w.Write(f);w.Write(new byte[28]);w.Write((byte)1);w.Write(new byte[20]);
    w.Write((sbyte)-1);w.Write((sbyte)-1);w.Write((byte)0);w.Write((byte)1);w.Write((byte)0);Text("");w.Write(3);
    w.Write(1);Text("親");Text("");Vec(2,0,0);w.Write((sbyte)(cycle?0:-1));w.Write(0);w.Write((ushort)0x1a);Vec(0,1,0);
    w.Write(1);Text("lift");Text("");w.Write((byte)4);w.Write((byte)1);w.Write(1);w.Write((byte)2);Vec(0,1,0);
    w.Write(0);w.Write(0);w.Write(0);return ms.ToArray();
}
void TgaTests()
{
    byte[] Header(byte type,byte descriptor,int width=2) { var h=new byte[18];h[2]=type;h[12]=(byte)width;h[14]=2;h[16]=24;h[17]=descriptor;return h; }
    byte[] pixels={0,0,255,0,255,0,255,0,0,255,255,255}; // stream: red green blue white
    var image=TgaDecoder.Decode(Header(2,32).Concat(pixels).ToArray());
    Check(image.Pixels.Take(4).SequenceEqual(new byte[]{0,0,255,255}),"TGA top origin -> bottom row blue");
    Check(image.Pixels.Skip(8).Take(4).SequenceEqual(new byte[]{255,0,0,255}),"TGA BGR -> RGBA");
    var mirrored=TgaDecoder.Decode(Header(2,48).Concat(pixels).ToArray());
    Check(mirrored.Pixels.Take(4).SequenceEqual(new byte[]{255,255,255,255}),"TGA right origin -> bottom left white");
    var rle=TgaDecoder.Decode(Header(10,32).Concat(new byte[]{3}).Concat(pixels).ToArray());
    Check(rle.Pixels.SequenceEqual(image.Pixels),"raw RLE packet");
    var repeated=TgaDecoder.Decode(Header(10,16).Concat(new byte[]{131,0,255,0}).ToArray());
    Check(repeated.Pixels.Where((v,i)=>i%4==1).All(v=>v==255),"repeat RLE packet");
    byte[] png=TgaDecoder.EncodePng(image);Check(png.Take(8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}),"PNG signature");
    try { TgaDecoder.Decode(Header(10,0).Concat(new byte[]{132,0,0,0}).ToArray());throw new Exception("RLE overflow accepted"); } catch(FormatException) {checks++;}
    try { TgaDecoder.Decode(new byte[3]);throw new Exception("truncated TGA accepted"); } catch(FormatException) {checks++;}
}
