using AssetImport;
using System.Collections;
using System.Diagnostics;
using System.IO.Compression;

internal static class ImportTextureChecks
{
    internal static void Run(Action<bool,string> check)
    {
        string path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".png");
        string tgaPath=Path.ChangeExtension(path,".tga");
        byte[] source={137,80,78,71,13,10,26,10};
        File.WriteAllBytes(path,source);
        KKAPI.Utilities.TextureUtils.MainThread=Environment.CurrentManagedThreadId;
        try
        {
            var lut=CubeLut.Read(new StringReader("LUT_3D_SIZE 2\n"+string.Join("\n",from b in Enumerable.Range(0,2) from g in Enumerable.Range(0,2) from r in Enumerable.Range(0,2) select $"{1-r} {1-g} {1-b}")));
            var diffuse=new TexturePath(null,Assimp.TextureType.Diffuse,path);
            var normal=new TexturePath(null,Assimp.TextureType.Normals,path);
            var expected=new byte[]{245,235,225,40,155,105,55,0};
            var results=new List<byte[]>();
            int finished=0;
            var errors=new List<string>();
            KKAPI.Utilities.TextureUtils.Reset();
            var iterator=new ImportTextureData(lut).ApplyAsync(new[]{diffuse,normal,diffuse},()=>true,(p,b)=>
            {
                check(Environment.CurrentManagedThreadId==KKAPI.Utilities.TextureUtils.MainThread,"material assignment stays on main thread");
                results.Add(b);
            },errors.Add,()=>finished++);
            Pump(iterator);
            check(errors.Count==0 && finished==1 && results.Count==3,"asynchronous import finishes all references once");
            check(DecodePng(results[0],1,2).SequenceEqual(expected),"RGBA/alpha and bottom-to-top row order survive asynchronous pipeline");
            check(ReferenceEquals(results[0],results[1]) && KKAPI.Utilities.TextureUtils.Calls==1,"nonadjacent shared texture references convert once");
            check(results[2].SequenceEqual(source),"normal texture bypasses LUT and converted cache");
            check(KKAPI.Utilities.TextureUtils.Last.destroyed,"temporary Unity texture is destroyed before worker consumes owned pixels");
            check(KKAPI.Utilities.TextureUtils.RawCalls==1 && KKAPI.Utilities.TextureUtils.ColorCalls==0,"RGBA32 fast path skips Color32 copy");
            check(ImportTextureData.PendingImports==0,"pending import count is balanced");
            check(File.ReadAllBytes(path).SequenceEqual(source),"source texture unchanged");

            results.Clear();
            Pump(new ImportTextureData(null).ApplyAsync(new[]{diffuse},()=>true,(_,b)=>results.Add(b),errors.Add,()=>{}));
            check(results.Single().SequenceEqual(source) && KKAPI.Utilities.TextureUtils.Calls==1,"conversion OFF preserves original bytes without Unity decode");
            foreach(var format in new[]{UnityEngine.TextureFormat.RGB24,UnityEngine.TextureFormat.RGBA32})
            {
                KKAPI.Utilities.TextureUtils.Format=format;
                KKAPI.Utilities.TextureUtils.Mips=format==UnityEngine.TextureFormat.RGBA32 ? 2 : 1;
                results.Clear();
                Pump(new ImportTextureData(lut).ApplyAsync(new[]{diffuse},()=>true,(_,b)=>results.Add(b),errors.Add,()=>{}));
                check(DecodePng(results.Single(),1,2).SequenceEqual(expected),"RGB24/mipmap layouts use safe fallback: "+format);
            }
            check(KKAPI.Utilities.TextureUtils.ColorCalls==2,"unsupported raw layouts use GetPixels32");
            KKAPI.Utilities.TextureUtils.Reset();

            byte[] tga=new byte[26];tga[2]=2;tga[12]=1;tga[14]=2;tga[16]=32;tga[17]=8;
            new byte[]{30,20,10,40,200,150,100,0}.CopyTo(tga,18);
            File.WriteAllBytes(tgaPath,tga);
            results.Clear();
            Pump(new ImportTextureData(lut).ApplyAsync(new[]{new TexturePath(null,Assimp.TextureType.Diffuse,tgaPath)},()=>true,(_,b)=>results.Add(b),errors.Add,()=>{}));
            check(DecodePng(results.Single(),1,2).SequenceEqual(expected) && KKAPI.Utilities.TextureUtils.Calls==0,"managed TGA converts directly without Unity round trip");

            // Cancel after queueing file work, before the next main-thread frame.
            bool alive=true;
            int applied=0;
            iterator=new ImportTextureData(lut).ApplyAsync(new[]{diffuse},()=>alive,(_,b)=>applied++,errors.Add,()=>finished++);
            iterator.MoveNext(); alive=false; Pump(iterator);
            check(applied==0 && finished==1,"removed target never receives late texture or completion callback");
            check(ImportTextureData.PendingImports==0,"cancelled import clears pending count");

            // Stopping the coroutine must release/cancel the global slot for the next import.
            iterator=new ImportTextureData(lut).ApplyAsync(new[]{diffuse},()=>true,(_,b)=>applied++,errors.Add,()=>{});
            iterator.MoveNext(); ((IDisposable)iterator).Dispose();
            results.Clear();
            Pump(new ImportTextureData(lut).ApplyAsync(new[]{diffuse},()=>true,(_,b)=>results.Add(b),errors.Add,()=>{}));
            check(results.Count==1 && ImportTextureData.PendingImports==0,"disposed coroutine cannot strand subsequent imports");

            // Decode failure is reported and releases the queue; later textures still apply.
            KKAPI.Utilities.TextureUtils.FailDecode=true;
            results.Clear(); errors.Clear();
            Pump(new ImportTextureData(lut).ApplyAsync(new[]{diffuse,new TexturePath(null,Assimp.TextureType.Diffuse,tgaPath)},()=>true,(_,b)=>results.Add(b),errors.Add,()=>{}));
            check(errors.Count==1 && results.Count==1,"Unity decode failure does not block later textures");
            KKAPI.Utilities.TextureUtils.Reset();
            File.WriteAllBytes(tgaPath,new byte[1]); errors.Clear(); results.Clear();
            Pump(new ImportTextureData(lut).ApplyAsync(new[]{new TexturePath(null,Assimp.TextureType.Diffuse,tgaPath),diffuse},()=>true,(_,b)=>results.Add(b),errors.Add,()=>{}));
            check(errors.Count==1 && results.Count==1,"worker failure releases queue and is reported on main thread");

            // Deliberately overlap two imports, then cancel the waiter.
            alive=true; int secondApplied=0;
            var first=new ImportTextureData(lut).ApplyAsync(new[]{diffuse},()=>true,(_,b)=>{},errors.Add,()=>{});
            var second=new ImportTextureData(lut).ApplyAsync(new[]{diffuse},()=>alive,(_,b)=>secondApplied++,errors.Add,()=>{});
            first.MoveNext(); second.MoveNext(); alive=false; Pump(second); Pump(first);
            check(secondApplied==0 && ImportTextureData.PendingImports==0,"queued import cancellation does not release another import's slot");

            var random=new byte[257*129*4];new Random(712).NextBytes(random);
            byte[] png=TgaDecoder.EncodePng(new TgaDecoder.Image{Width=257,Height=129,Pixels=random});
            check(DecodePng(png,257,129).SequenceEqual(random),"multi-IDAT streaming PNG independently verifies CRC, zlib checksum and all pixels");
            try { TgaDecoder.EncodePng(new TgaDecoder.Image{Width=257,Height=129,Pixels=random},()=>throw new OperationCanceledException()); check(false,"PNG cancellation ignored"); }
            catch(OperationCanceledException) { check(true,"PNG encoder supports cancellation between rows"); }
            try { lut.Apply(random,()=>throw new OperationCanceledException()); check(false,"LUT cancellation ignored"); }
            catch(OperationCanceledException) { check(true,"LUT supports cancellation between blocks"); }
        }
        finally { File.Delete(path); if(File.Exists(tgaPath)) File.Delete(tgaPath); }
    }

    private static void Pump(IEnumerator iterator)
    {
        var clock=Stopwatch.StartNew();
        try
        {
            while(iterator.MoveNext())
            {
                if(clock.Elapsed.TotalSeconds>10) throw new Exception("Texture iterator timed out");
                if(iterator.Current!=null) throw new Exception("Expected frame yield");
                Thread.Sleep(1);
            }
        }
        finally { (iterator as IDisposable)?.Dispose(); }
    }

    // Independent PNG reader: concatenate IDAT, check every CRC and let ZLibStream
    // verify Adler-32. Generated scanlines must use filter 0 and preserve every byte.
    private static byte[] DecodePng(byte[] png,int width,int height)
    {
        int U32(int offset)=>System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset,4));
        using var idat=new MemoryStream();
        for(int p=8;p<png.Length;)
        {
            int count=U32(p); uint crc=0xffffffff;
            for(int i=p+4;i<p+8+count;i++)
            {
                crc^=png[i];for(int j=0;j<8;j++)crc=(crc>>1)^((crc&1)!=0?0xedb88320u:0u);
            }
            if((crc^0xffffffff)!=unchecked((uint)U32(p+8+count)))throw new Exception("Bad PNG CRC");
            string type=System.Text.Encoding.ASCII.GetString(png,p+4,4);
            if(type=="IHDR" && (U32(p+8)!=width || U32(p+12)!=height))throw new Exception("Bad dimensions");
            if(type=="IDAT")idat.Write(png,p+8,count);
            p+=12+count;
        }
        idat.Position=0;
        using var zlib=new ZLibStream(idat,CompressionMode.Decompress);
        var pixels=new byte[width*height*4];
        for(int y=height-1;y>=0;y--)
        {
            if(zlib.ReadByte()!=0)throw new Exception("Bad row filter");
            zlib.ReadExactly(pixels.AsSpan(y*width*4,width*4));
        }
        if(zlib.ReadByte()!=-1)throw new Exception("Extra PNG data");
        return pixels;
    }
}

namespace AssetImport
{
    internal sealed class TexturePath
    {
        internal string Path;
        internal Assimp.TextureType Type;
        internal UnityEngine.Material Material;
        internal bool Use=true;
        internal TexturePath(UnityEngine.Material material,Assimp.TextureType type,string path){Material=material;Type=type;Path=path;}
        internal bool PathOkay()=>File.Exists(Path);
    }
}
namespace UnityEngine
{
    public sealed class Material : Object { }
    public enum TextureFormat { RGBA32, RGB24 }
    public struct Color32 { public byte r,g,b,a; }
    public sealed class Texture2D:Object
    {
        public int width=1,height=2;
        public TextureFormat format=TextureFormat.RGBA32;
        public int mipmapCount=1;
        public byte[] GetRawTextureData()
        {
            KKAPI.Utilities.TextureUtils.AssertMain(); KKAPI.Utilities.TextureUtils.RawCalls++;
            return new byte[]{10,20,30,40,100,150,200,0};
        }
        public Color32[] GetPixels32()
        {
            KKAPI.Utilities.TextureUtils.AssertMain(); KKAPI.Utilities.TextureUtils.ColorCalls++;
            return new[]{new Color32 {r=10,g=20,b=30,a=40},new Color32 {r=100,g=150,b=200,a=0}};
        }
    }
}
namespace KKAPI.Utilities
{
    internal static class TextureUtils
    {
        internal static int Calls,RawCalls,ColorCalls,MainThread,Mips=1;
        internal static UnityEngine.TextureFormat Format=UnityEngine.TextureFormat.RGBA32;
        internal static bool FailDecode;
        internal static UnityEngine.Texture2D Last;
        internal static void AssertMain() {if(Environment.CurrentManagedThreadId!=MainThread)throw new Exception("Unity API on worker thread");}
        internal static void Reset(){Calls=RawCalls=ColorCalls=0;Mips=1;Format=UnityEngine.TextureFormat.RGBA32;FailDecode=false;}
        public static UnityEngine.Texture2D LoadTexture(byte[] bytes,UnityEngine.TextureFormat format,bool option)
        {
            AssertMain();
            if(format!=UnityEngine.TextureFormat.RGBA32 || option) throw new Exception("Unexpected texture decode options");
            if(FailDecode)throw new Exception("Synthetic decode failure");
            Calls++;return Last=new UnityEngine.Texture2D{format=Format,mipmapCount=Mips};
        }
    }
}
