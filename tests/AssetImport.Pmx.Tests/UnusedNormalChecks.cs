using AssetImport;
using System.Text;

internal static class UnusedNormalChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var original=Fixture();
        var clean=PmxReader.Read(original.Bytes);
        check(clean.UnusedNormalsRepaired==0,"normal fixture needs no repairs");
        // Include NaN and both infinities. These vertices stay
        // in the PMX table (and can still be morph targets), but have no faces.
        var damaged=(byte[])original.Bytes.Clone();
        float[] invalid={float.NaN,float.PositiveInfinity,float.NegativeInfinity};
        for (int i=0;i<invalid.Length;i++) Put(damaged,original.Vertices[3+i]+12+i*4,invalid[i]);
        var before=(byte[])damaged.Clone();
        var model=PmxReader.Read(damaged);
        check(model.UnusedNormalsRepaired==3,"only three unused non-finite normals repaired");
        check(model.Vertices.Length==clean.Vertices.Length && model.Indices.SequenceEqual(clean.Indices),"normal repair does not remove or renumber vertices/indices");
        check(damaged.SequenceEqual(before),"normal repair does not mutate disk/cache bytes");
        for (int i=0;i<model.Vertices.Length;i++)
        {
            var v=model.Vertices[i]; var expected=clean.Vertices[i];
            check(Equal(v.Position,expected.Position) && Equal(v.UV,expected.UV),"repair preserves position and UV "+i);
            check(v.Bones.SequenceEqual(expected.Bones) && v.Weights.SequenceEqual(expected.Weights),"repair preserves bone weights "+i);
            check(i<3 || i==6 ? Equal(v.Normal,expected.Normal) : Equal(v.Normal,new PmxModel.V3(0,1,0)),"custom normal preserved / unused normal finite fallback "+i);
        }
        check(model.Vertices[0].Normal.Z==2,"valid nonunit custom normal is not renormalized");
        check(Equal(model.Vertices[2].Normal,new PmxModel.V3()) && Equal(model.Vertices[6].Normal,new PmxModel.V3()),"finite zero normals retain prior compatibility for referenced and unused vertices");
        check(model.Morphs[0].Deltas.ContainsKey(5) && Equal(model.Morphs[0].Deltas[5],new PmxModel.V3(1,2,3)),"morph still targets original unused vertex index");
        var scene=PmxSceneBuilder.Build(model,true); var expectedScene=PmxSceneBuilder.Build(clean,true);
        check(scene.MeshCount==1 && scene.Meshes[0].VertexCount==3,"unused vertices never enter renderer");
        check(scene.Meshes[0].Vertices.SequenceEqual(expectedScene.Meshes[0].Vertices) && scene.Meshes[0].Normals.SequenceEqual(expectedScene.Meshes[0].Normals),"repair leaves rendered geometry and original shading unchanged");
        var cached=PmxReader.Read((byte[])damaged.Clone());
        check(cached.UnusedNormalsRepaired==model.UnusedNormalsRepaired && cached.Vertices.Select(v=>v.Normal).Zip(model.Vertices.Select(v=>v.Normal),Equal).All(v=>v),"disk import and cached blob produce identical normals/repair counts");

        foreach (float value in invalid)
        for (int axis=0;axis<3;axis++)
        {
            byte[] referenced=(byte[])original.Bytes.Clone();
            Put(referenced,original.Vertices[0]+12+axis*4,value);
            Reject(referenced,check,"referenced non-finite normal "+value+" axis "+axis,"referenced vertex 0");
        }
        foreach (float value in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity})
        {
            // The compatibility exception must never leak into geometry, UV,
            // weights, including unused vertices and later data in the file.
            foreach (int vertex in new[]{0,3})
            foreach (int offset in new[]{0,4,8,24,28,35})
            {
                byte[] corrupt=(byte[])damaged.Clone(); Put(corrupt,original.Vertices[vertex]+offset,value);
                Reject(corrupt,check,"non-finite position/UV/weight at vertex "+vertex+" offset "+offset,"non-finite number");
            }
        }
        byte[] badIndex=(byte[])damaged.Clone(); badIndex[original.Indices]=127;
        Reject(badIndex,check,"invalid topology still validated before unused-normal repair","index outside referenced table");
    }

    private static bool Equal(PmxModel.V3 a,PmxModel.V3 b) { return a.X==b.X && a.Y==b.Y && a.Z==b.Z; }
    private static void Put(byte[] bytes,int offset,float value) { BitConverter.GetBytes(value).CopyTo(bytes,offset); }
    private static void Reject(byte[] bytes,Action<bool,string> check,string message,string detail)
    {
        try { PmxReader.Read(bytes); check(false,message+" accepted"); }
        catch (FormatException error) { check(error.Message.Contains(detail),message+" explicitly rejected: "+error.Message); }
    }
    private sealed class Data
    {
        internal byte[] Bytes;
        internal int[] Vertices;
        internal int Indices;
    }
    private static Data Fixture()
    {
        using var stream=new MemoryStream(); using var writer=new BinaryWriter(stream);
        void Text(string value) { var bytes=Encoding.UTF8.GetBytes(value);writer.Write(bytes.Length);writer.Write(bytes); }
        void Vec(float x,float y,float z) { writer.Write(x);writer.Write(y);writer.Write(z); }
        writer.Write(Encoding.ASCII.GetBytes("PMX "));writer.Write(2f);writer.Write((byte)8);writer.Write(new byte[]{1,0,1,1,1,1,1,1});
        Text("Unused normal fixture");Text("");Text("");Text("");
        var result=new Data { Vertices=new int[7] };
        writer.Write(7);
        for (int i=0;i<7;i++)
        {
            result.Vertices[i]=(int)stream.Position;
            Vec(i==1 ? 1 : 0,i==2 ? 1 : 0,0); // first three form a triangle
            if(i==2 || i==6) Vec(0,0,0); else if(i<3) Vec(0,0,2); else Vec(1,0,0);
            writer.Write(.2f);writer.Write(.3f);
            writer.Write((byte)1);writer.Write((byte)0);writer.Write((byte)0);writer.Write(.25f);writer.Write(1f);
        }
        writer.Write(3); result.Indices=(int)stream.Position; writer.Write(new byte[]{0,1,2});
        writer.Write(0);writer.Write(1);Text("paint");Text("");
        foreach (var value in new[]{1f,1,1,1}) writer.Write(value);
        writer.Write(new byte[28]);writer.Write((byte)0);writer.Write(new byte[20]);
        writer.Write((sbyte)-1);writer.Write((sbyte)-1);writer.Write((byte)0);writer.Write((byte)1);writer.Write((byte)0);Text("");writer.Write(3);
        writer.Write(1);Text("root");Text("");Vec(0,0,0);writer.Write((sbyte)-1);writer.Write(0);writer.Write((ushort)0x1a);Vec(0,1,0);
        writer.Write(1);Text("unused vertex morph");Text("");writer.Write((byte)4);writer.Write((byte)1);writer.Write(1);writer.Write((byte)5);Vec(1,2,3);
        writer.Write(0);writer.Write(0);writer.Write(0);
        result.Bytes=stream.ToArray();return result;
    }
}
