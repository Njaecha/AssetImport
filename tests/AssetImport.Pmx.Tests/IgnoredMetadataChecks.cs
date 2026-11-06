using AssetImport;
using System.Text;

internal static class IgnoredMetadataChecks
{
    internal static void Run(Action<bool,string> check)
    {
        foreach (int indexSize in new[]{1,2,4})
        foreach (bool utf16 in new[]{false,true})
        {
            var fixture=Fixture(indexSize,utf16);
            var clean=PmxReader.Read(fixture.Bytes);
            var expected=PmxSceneBuilder.Build(clean,true);
            var expectedMap=PmxMmddMapping.Build(clean,new byte[]{1},"generated",Array.Empty<PmxMorphTarget>(),Array.Empty<int>());
            foreach (float value in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity})
            foreach (int offset in fixture.Unused)
            {
                var data=(byte[])fixture.Bytes.Clone(); BitConverter.GetBytes(value).CopyTo(data,offset);
                var before=(byte[])data.Clone();
                var parsed=PmxReader.Read(data);
                var scene=PmxSceneBuilder.Build(parsed,true);
                string detail="unused metadata at "+offset+", index width="+indexSize+", utf16="+utf16;
                check(parsed.InvalidIgnoredMetadataFields==1 && parsed.IgnoredMetadataWarnings.Count==1,detail+" diagnosed");
                check(data.SequenceEqual(before),detail+" preserves original bytes");
                check(scene.Meshes[0].Vertices.SequenceEqual(expected.Meshes[0].Vertices) &&
                    scene.Meshes[0].Normals.SequenceEqual(expected.Meshes[0].Normals) &&
                    scene.Meshes[0].TextureCoordinateChannels[0].SequenceEqual(expected.Meshes[0].TextureCoordinateChannels[0]),detail+" preserves geometry");
                check(scene.Meshes[0].Bones[0].OffsetMatrix==expected.Meshes[0].Bones[0].OffsetMatrix &&
                    parsed.Vertices.All(v=>v.Weights.SequenceEqual(clean.Vertices[0].Weights)),detail+" preserves binding/weights");
                check(scene.Meshes[0].MeshAnimationAttachments[0].Vertices.SequenceEqual(expected.Meshes[0].MeshAnimationAttachments[0].Vertices),detail+" preserves morphs");
                var mapping=PmxMmddMapping.Build(parsed,new byte[]{1},"generated",Array.Empty<PmxMorphTarget>(),Array.Empty<int>());
                check(mapping.MappingJson==expectedMap.MappingJson,detail+" preserves mapping");
                check(PmxReader.Read((byte[])data.Clone()).InvalidIgnoredMetadataFields==1,detail+" cache reload matches");
            }
            foreach (int offset in fixture.Required)
            foreach (float value in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity})
            {
                var data=(byte[])fixture.Bytes.Clone(); BitConverter.GetBytes(value).CopyTo(data,offset);
                Reject(data,check,"used numeric field stays strict at "+offset);
            }
            foreach (int offset in fixture.Unused)
                for(int bytes=0;bytes<4;bytes++) Reject(fixture.Bytes[..(offset+bytes)],check,"truncated optional metadata stays fatal");
        }
    }

    private static void Reject(byte[] data,Action<bool,string> check,string label)
    {
        try {PmxReader.Read(data);check(false,label);}
        catch(FormatException) {check(true,label);}
    }

    private sealed class Data
    {
        internal byte[] Bytes=Array.Empty<byte>();
        internal readonly List<int> Unused=new();
        internal readonly List<int> Required=new();
    }

    private static Data Fixture(int indexSize,bool utf16)
    {
        var result=new Data();
        using var stream=new MemoryStream(); using var writer=new BinaryWriter(stream);
        var encoding=utf16 ? Encoding.Unicode : Encoding.UTF8;
        void Text(string value) {var bytes=encoding.GetBytes(value);writer.Write(bytes.Length);writer.Write(bytes);}
        void Index(int value) {if(indexSize==1)writer.Write((sbyte)value);else if(indexSize==2)writer.Write((short)value);else writer.Write(value);}
        void Values(bool unused,params float[] values)
        {
            foreach(float value in values) {(unused ? result.Unused : result.Required).Add((int)stream.Position);writer.Write(value);}
        }
        writer.Write(Encoding.ASCII.GetBytes("PMX ")); writer.Write(2f);writer.Write((byte)8);
        writer.Write(new[]{(byte)(utf16 ? 0 : 1),(byte)1,(byte)indexSize,(byte)indexSize,(byte)indexSize,(byte)indexSize,(byte)indexSize,(byte)indexSize});
        Text("生成骨架 "+indexSize);Text("");Text("");Text("");
        writer.Write(3);
        for(int i=0;i<3;i++)
        {
            Values(false,i==1 ? 1 : 0,i==2 ? 1 : 0,0);Values(false,0,0,1);Values(false,.2f,.3f);
            Values(true,0,0,0,0); // additional UV not imported
            writer.Write((byte)3);Index(0);Index(0);Values(false,.25f);
            Values(true,0,0,0,1,0,0,0,1,0); // SDEF approximated as BDEF2
            Values(true,1); // edge scale
        }
        writer.Write(3);Index(0);Index(1);Index(2);
        writer.Write(0);writer.Write(1);Text("material");Text("");Values(false,1,1,1,1);
        Values(true,0,0,0,1,0,0,0);writer.Write((byte)0);Values(true,0,0,0,1,1);
        Index(-1);Index(-1);writer.Write((byte)0);writer.Write((byte)1);writer.Write((byte)0);Text("");writer.Write(3);
        writer.Write(1);Text("任意骨骼");Text("");Values(false,0,0,0);Index(-1);writer.Write(0);writer.Write((ushort)0xc06);
        Values(true,0,1,0); // display tail, not the bone's actual position
        Values(false,1,0,0); // fixed axis used by MMDD, must stay strict
        Values(true,1,0,0,0,0,1); // local axes, not consumed by current import/mapping
        writer.Write(1);Text("expression");Text("");writer.Write((byte)4);writer.Write((byte)1);writer.Write(1);Index(0);Values(false,0,1,0);
        writer.Write(0);writer.Write(0);writer.Write(0);
        result.Bytes=stream.ToArray();return result;
    }
}
