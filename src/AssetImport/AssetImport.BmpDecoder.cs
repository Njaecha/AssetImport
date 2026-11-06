using System;

namespace AssetImport
{
    // Windows BMP: indexed 1/4/8-bit, RGB 16/24/32-bit and bitfields.
    // Decode into the same bottom-up RGBA buffer used by the PNG writer.
    internal static class BmpDecoder
    {
        internal static ushort U16(byte[] b, int p)
        { Require(b, p, 2); return (ushort)(b[p] | b[p + 1] << 8); }
        internal static uint U32(byte[] b, int p)
        { Require(b, p, 4); return (uint)(b[p] | b[p + 1] << 8 | b[p + 2] << 16 | b[p + 3] << 24); }
        internal static void Require(byte[] b, long start, long count)
        { if (start < 0 || count < 0 || start > b.Length - count) throw new FormatException("Truncated texture data."); }
        internal static byte Channel(uint pixel, uint mask, byte absent)
        {
            if (mask == 0) return absent;
            int shift = 0; while ((mask & 1) == 0) { mask >>= 1; shift++; }
            if ((mask & (mask + 1)) != 0) throw new FormatException("Non-contiguous texture channel mask.");
            return (byte)((((ulong)(pixel >> shift) & mask) * 255 + mask / 2) / mask);
        }
        internal static TgaDecoder.Image NewImage(int width, int height)
        {
            if (width <= 0 || height <= 0 || (long)width * height > 16777216)
                throw new FormatException("Invalid/oversized texture dimensions (maximum 16M pixels).");
            return new TgaDecoder.Image { Width = width, Height = height, Pixels = new byte[width * height * 4] };
        }
        internal static TgaDecoder.Image Decode(byte[] b)
        {
            Require(b, 0, 54);
            if (U16(b, 0) != 0x4d42) throw new FormatException("Invalid BMP signature.");
            uint offset = U32(b, 10), header = U32(b, 14);
            if (header != 40 && header != 52 && header != 56 && header != 108 && header != 124)
                throw new FormatException("Unsupported BMP header. Use Windows RGB/indexed/bitfield BMP.");
            Require(b, 14, header);
            int width = (int)U32(b, 18), signedHeight = (int)U32(b, 22);
            if (signedHeight == int.MinValue || U16(b, 26) != 1) throw new FormatException("Invalid BMP dimensions/planes.");
            int height = Math.Abs(signedHeight), bits = U16(b, 28);
            uint compression = U32(b, 30), colors = U32(b, 46);
            if ((compression != 0 && compression != 3 && compression != 6) ||
                (bits != 1 && bits != 4 && bits != 8 && bits != 16 && bits != 24 && bits != 32) ||
                (compression != 0 && bits != 16 && bits != 32))
                throw new FormatException("Unsupported BMP compression. RLE/JPEG/PNG-in-BMP are not supported.");
            var image = NewImage(width, height);
            uint rm = bits == 16 ? 0x7c00u : 0xff0000u, gm = bits == 16 ? 0x3e0u : 0xff00u,
                 bm = bits == 16 ? 0x1fu : 0xffu, am = 0;
            int palette = checked(14 + (int)header);
            if (compression != 0)
            {
                rm = U32(b, 54); gm = U32(b, 58); bm = U32(b, 62);
                if (header >= 56 || compression == 6) am = U32(b, 66);
                if (header == 40) palette += compression == 6 ? 16 : 12;
                if (rm == 0 || gm == 0 || bm == 0 || (rm & gm) != 0 || (rm & bm) != 0 || (gm & bm) != 0 || ((rm | gm | bm) & am) != 0)
                    throw new FormatException("Invalid BMP channel masks.");
            }
            int colorCount = bits <= 8 ? (colors == 0 ? 1 << bits : checked((int)colors)) : 0;
            if (colorCount > (1 << Math.Min(bits, 8))) throw new FormatException("Invalid BMP palette size.");
            Require(b, palette, colorCount * 4L);
            if (offset < palette + colorCount * 4L) throw new FormatException("BMP pixels overlap the header/palette.");
            long stride = (((long)width * bits + 31) / 32) * 4;
            Require(b, offset, stride * height);
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                int src = checked((int)(offset + y * stride)), dst = ((signedHeight < 0 ? height - 1 - y : y) * width + x) * 4;
                if (bits <= 8)
                {
                    int bit = x * bits, index = (b[src + bit / 8] >> (8 - bits - bit % 8)) & ((1 << bits) - 1);
                    if (index >= colorCount) throw new FormatException("BMP palette index out of range.");
                    int p = palette + index * 4;
                    image.Pixels[dst] = b[p + 2]; image.Pixels[dst + 1] = b[p + 1]; image.Pixels[dst + 2] = b[p]; image.Pixels[dst + 3] = 255;
                }
                else
                {
                    src += x * (bits / 8);
                    uint pixel = bits == 16 ? U16(b, src) : bits == 24 ? (uint)(b[src] | b[src + 1] << 8 | b[src + 2] << 16) : U32(b, src);
                    image.Pixels[dst] = Channel(pixel, rm, 0); image.Pixels[dst + 1] = Channel(pixel, gm, 0);
                    image.Pixels[dst + 2] = Channel(pixel, bm, 0); image.Pixels[dst + 3] = Channel(pixel, am, 255);
                }
            }
            return image;
        }
    }
}
