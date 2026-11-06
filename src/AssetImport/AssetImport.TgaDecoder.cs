using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace AssetImport
{
    internal static class TgaDecoder
    {
        internal sealed class Image
        {
            internal int Width, Height;
            // RGBA, left-to-right / bottom-to-top, as required by Texture2D.
            internal byte[] Pixels;
        }
        private static readonly uint[] CrcTable = CreateCrcTable();

        private static uint[] CreateCrcTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < table.Length; i++)
            {
                uint crc = i;
                for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0u);
                table[i] = crc;
            }
            return table;
        }

        private static void BigEndian(Stream stream, uint value)
        { for (int i = 3; i >= 0; i--) stream.WriteByte((byte)(value >> (i * 8))); }

        private static void Chunk(Stream stream, string type, byte[] bytes, int count)
        {
            BigEndian(stream, (uint)count);
            byte[] tag = Encoding.ASCII.GetBytes(type);
            stream.Write(tag, 0, tag.Length);
            stream.Write(bytes, 0, count);
            uint crc = 0xffffffff;
            foreach (byte value in tag) crc = CrcTable[(crc ^ value) & 255] ^ (crc >> 8);
            for (int i = 0; i < count; i++) crc = CrcTable[(crc ^ bytes[i]) & 255] ^ (crc >> 8);
            BigEndian(stream, crc ^ 0xffffffff);
        }

        // Bound the compressed staging buffer; never assemble a full raw scanline image
        // or duplicate the complete compressed payload. DeflateStream may close this
        // adapter, but the owner still needs to append the zlib checksum afterward.
        private sealed class IdatStream : Stream
        {
            private readonly Stream output;
            private readonly byte[] buffer = new byte[65536];
            private int count;
            internal IdatStream(Stream output) { this.output = output; }
            public override void Write(byte[] bytes, int offset, int length)
            {
                while (length > 0)
                {
                    int copy = Math.Min(length, buffer.Length - count);
                    Buffer.BlockCopy(bytes, offset, buffer, count, copy);
                    count += copy; offset += copy; length -= copy;
                    if (count == buffer.Length) Flush();
                }
            }
            public override void WriteByte(byte value)
            {
                buffer[count++] = value;
                if (count == buffer.Length) Flush();
            }
            public override void Flush()
            {
                if (count == 0) return;
                Chunk(output, "IDAT", buffer, count);
                count = 0;
            }
            protected override void Dispose(bool disposing) { }
            public override bool CanRead { get { return false; } }
            public override bool CanSeek { get { return false; } }
            public override bool CanWrite { get { return true; } }
            public override long Length { get { throw new NotSupportedException(); } }
            public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
            public override int Read(byte[] bytes, int offset, int length) { throw new NotSupportedException(); }
            public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
            public override void SetLength(long value) { throw new NotSupportedException(); }
        }

        internal static byte[] EncodePng(Image image, Action checkCancellation = null)
        {
            if (image == null || image.Width <= 0 || image.Height <= 0 || image.Pixels == null ||
                (long)image.Width * image.Height * 4 != image.Pixels.Length)
                throw new ArgumentException("Expected a complete RGBA image.");
            int stride = checked(image.Width * 4);
            using (var png = new MemoryStream())
            using (var header = new MemoryStream())
            {
                byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
                png.Write(signature, 0, signature.Length);
                BigEndian(header, (uint)image.Width); BigEndian(header, (uint)image.Height);
                byte[] format = { 8, 6, 0, 0, 0 };
                header.Write(format, 0, format.Length);
                Chunk(png, "IHDR", header.GetBuffer(), (int)header.Length);
                var idat = new IdatStream(png);
                idat.WriteByte(0x78); idat.WriteByte(0x9c);
                uint a = 1, b = 0;
                byte[] filter = { 0 };
                using (var compressor = new DeflateStream(idat, CompressionMode.Compress))
                {
                    // PNG rows run top-to-bottom; the decoder/Unity buffer runs bottom-to-top.
                    for (int y = image.Height - 1; y >= 0; y--)
                    {
                        if (checkCancellation != null) checkCancellation();
                        compressor.Write(filter, 0, 1);
                        b = (b + a) % 65521;
                        int offset = y * stride, end = offset + stride;
                        compressor.Write(image.Pixels, offset, stride);
                        // Adler-32 can accumulate 5552 bytes safely before reducing modulo.
                        while (offset < end)
                        {
                            int blockEnd = Math.Min(offset + 5552, end);
                            for (; offset < blockEnd; offset++) { a += image.Pixels[offset]; b += a; }
                            a %= 65521; b %= 65521;
                        }
                    }
                }
                BigEndian(idat, (b << 16) | a);
                idat.Flush();
                Chunk(png, "IEND", new byte[0], 0);
                return png.ToArray(); // ME owns a standalone encoded byte[] for scene/card persistence.
            }
        }
        internal static Image Decode(byte[] bytes)
        {
            using (var r=new BinaryReader(new MemoryStream(bytes,false)))
            {
                try
                {
                    int id=r.ReadByte(),map=r.ReadByte(),type=r.ReadByte(); r.ReadBytes(9);
                    int width=r.ReadUInt16(),height=r.ReadUInt16(),depth=r.ReadByte(),descriptor=r.ReadByte();
                    bool grey=type==3 || type==11, rle=type==10 || type==11;
                    if (map!=0 || (type!=2 && type!=3 && type!=10 && type!=11) ||
                        (grey ? depth!=8 : depth!=24 && depth!=32) || (descriptor&0xc0)!=0)
                        throw new FormatException("Unsupported TGA: use 24/32-bit true color or 8-bit grayscale, raw/RLE, without a palette.");
                    if (width<=0 || height<=0 || (long)width*height>16777216) throw new FormatException("Invalid/oversized TGA dimensions.");
                    if (r.ReadBytes(id).Length!=id) throw new EndOfStreamException();
                    int count=width*height,n=0,bpp=depth/8;
                    var image=new Image { Width=width, Height=height, Pixels=new byte[count*4] };
                    while (n<count)
                    {
                        int packet=rle ? r.ReadByte() : 0;
                        int run=rle ? (packet&127)+1 : 1;
                        if (run>count-n) throw new FormatException("TGA packet exceeds image size.");
                        byte blue=0,green=0,red=0,alpha=255;
                        for (int i=0;i<run;i++,n++)
                        {
                            if (i==0 || !rle || (packet&128)==0)
                            {
                                blue=r.ReadByte();
                                green=grey ? blue : r.ReadByte();
                                red=grey ? blue : r.ReadByte();
                                alpha=bpp==4 ? r.ReadByte() : (byte)255;
                            }
                            int x=n%width,y=n/width;
                            if ((descriptor&16)!=0) x=width-1-x;
                            if ((descriptor&32)!=0) y=height-1-y;
                            int offset=(y*width+x)*4;
                            image.Pixels[offset]=red; image.Pixels[offset+1]=green; image.Pixels[offset+2]=blue;
                            image.Pixels[offset+3]=(byte)(bpp==4 && (descriptor&15)!=0 ? alpha : 255);
                        }
                    }
                    return image;
                }
                catch (EndOfStreamException) { throw new FormatException("Truncated TGA image."); }
            }
        }
    }
}
