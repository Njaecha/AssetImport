using AssetImport;
using System.Text;

internal static class TextureDecoderChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var bmp=new byte[70];
        void U32(byte[] b,int p,uint v)=>BitConverter.GetBytes(v).CopyTo(b,p);
        void U16(byte[] b,int p,ushort v)=>BitConverter.GetBytes(v).CopyTo(b,p);
        bmp[0]=66;bmp[1]=77;U32(bmp,2,70);U32(bmp,10,54);U32(bmp,14,40);U32(bmp,18,2);U32(bmp,22,2);U16(bmp,26,1);U16(bmp,28,24);
        // bottom row red/green, top row blue/white with 2-byte row padding
        new byte[]{0,0,255,0,255,0,0,0,255,0,0,255,255,255,0,0}.CopyTo(bmp,54);
        var image=BmpDecoder.Decode(bmp);
        check(image.Pixels.Take(4).SequenceEqual(new byte[]{255,0,0,255}),"BMP padded rows and BGR order");
        U32(bmp,22,unchecked((uint)-2));
        check(BmpDecoder.Decode(bmp).Pixels.Take(4).SequenceEqual(new byte[]{0,0,255,255}),"BMP negative height origin");
        foreach(int n in new[]{0,20,54,69}) {try{BmpDecoder.Decode(bmp[..n]);throw new Exception("accepted BMP truncation");}catch(FormatException){check(true,"BMP truncation");}}
        var indexed=new byte[66];bmp[..54].CopyTo(indexed,0);U32(indexed,10,62);U32(indexed,18,2);U32(indexed,22,1);U16(indexed,28,1);U32(indexed,46,2);
        indexed[56]=255; indexed[59]=255; indexed[62]=0x40;
        var pal=BmpDecoder.Decode(indexed);check(pal.Pixels[0]==255 && pal.Pixels[5]==255,"BMP 1-bit palette unpacking");
        foreach(int mode in new[]{1,2,3})
        {
            var dds=new byte[128+(mode==1?8:16)];Encoding.ASCII.GetBytes("DDS ").CopyTo(dds,0);
            U32(dds,4,124);U32(dds,12,4);U32(dds,16,4);U32(dds,76,32);U32(dds,80,4);
            Encoding.ASCII.GetBytes(mode==1?"DXT1":mode==2?"DXT3":"DXT5").CopyTo(dds,84);
            int color=128+(mode==1?0:8);U16(dds,color,0xf800);U16(dds,color+2,0x07e0);
            if(mode==2)for(int i=0;i<8;i++)dds[128+i]=0x88;
            if(mode==3){dds[128]=128;dds[129]=0;}
            var decoded=DdsDecoder.Decode(dds);
            check(decoded.Pixels[0]==255 && decoded.Pixels[1]==0 && decoded.Pixels[3]==(mode==1?255:mode==2?136:128),"DDS BC"+mode+" color/alpha");
            try{DdsDecoder.Decode(dds[..^1]);throw new Exception("accepted DDS truncation");}catch(FormatException){check(true,"DDS truncation");}
            if(mode==1)
            {
                U16(dds,color,0);U16(dds,color+2,0xffff);U32(dds,color+4,uint.MaxValue);
                check(DdsDecoder.Decode(dds).Pixels[3]==0,"BC1 transparent endpoint");
            }
            U32(dds,112,0x200);
            try{DdsDecoder.Decode(dds);throw new Exception("accepted cubemap");}catch(FormatException){check(true,"DDS cubemap rejection");}
        }
    }
}
