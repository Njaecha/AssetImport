using System;

namespace AssetImport
{
    // Single 2D DDS, first mip: BC1/2/3 (DXT1/3/5), RGB(A) and their DX10 forms.
    internal static class DdsDecoder
    {
        private static uint Read(byte[] b, int p) { return BmpDecoder.U32(b, p); }
        internal static TgaDecoder.Image Decode(byte[] b)
        {
            BmpDecoder.Require(b, 0, 128);
            if (Read(b, 0) != 0x20534444 || Read(b, 4) != 124 || Read(b, 76) != 32)
                throw new FormatException("Invalid DDS header.");
            int height = checked((int)Read(b, 12)), width = checked((int)Read(b, 16));
            if (Read(b, 24) > 1 || (Read(b, 112) & 0xfe200) != 0 || (Read(b, 112) & 0x200000) != 0)
                throw new FormatException("DDS cubemaps/volume textures are not supported as material images.");
            uint flags = Read(b, 80), fourcc = Read(b, 84), rm = Read(b, 92), gm = Read(b, 96), bm = Read(b, 100), am = Read(b, 104);
            int bits = checked((int)Read(b, 88)), offset = 128, bc = 0;
            bool dx10 = false, opaque = false;
            if ((flags & 4) != 0)
            {
                if (fourcc == 0x31545844) bc = 1;
                else if (fourcc == 0x33545844) bc = 2;
                else if (fourcc == 0x35545844) bc = 3;
                else if (fourcc == 0x30315844)
                {
                    BmpDecoder.Require(b, 128, 20); offset = 148; dx10 = true;
                    uint format = Read(b, 128), alphaMode = Read(b, 144) & 7;
                    if (Read(b, 132) != 3 || (Read(b, 136) & 4) != 0 || Read(b, 140) != 1)
                        throw new FormatException("DDS must contain one 2D texture, not an array/cubemap.");
                    if (alphaMode == 2 || alphaMode == 4) throw new FormatException("DDS premultiplied/custom alpha is not supported.");
                    opaque = alphaMode == 3;
                    if (format == 71 || format == 72) bc = 1;
                    else if (format == 74 || format == 75) bc = 2;
                    else if (format == 77 || format == 78) bc = 3;
                    else if (format == 28 || format == 29 || format == 87 || format == 91 || format == 88 || format == 93)
                    {
                        bits = 32; gm = 0xff00;
                        bool rgba = format == 28 || format == 29;
                        rm = rgba ? 0xffu : 0xff0000u; bm = rgba ? 0xff0000u : 0xffu;
                        am = format == 88 || format == 93 ? 0u : 0xff000000u;
                    }
                    else throw new FormatException("Unsupported DDS DXGI format " + format + ". Use BC1/BC2/BC3 or RGBA8; BC7/HDR are not supported.");
                }
                else throw new FormatException("Unsupported DDS FourCC. Use DXT1/DXT3/DXT5 or RGB(A).");
            }
            else if ((flags & 0x40) == 0) throw new FormatException("Unsupported DDS pixel format.");
            var image = BmpDecoder.NewImage(width, height);
            if (bc == 0)
            {
                if (bits != 16 && bits != 24 && bits != 32) throw new FormatException("Unsupported DDS RGB bit depth.");
                if (!dx10 && (flags & 1) == 0) am = 0;
                if (rm == 0 || gm == 0 || bm == 0 || ((rm | gm | bm) & am) != 0 || (rm & gm) != 0 || (rm & bm) != 0 || (gm & bm) != 0)
                    throw new FormatException("Invalid DDS RGB masks.");
                int stride = width * (bits / 8);
                if ((Read(b, 8) & 8) != 0) stride = checked((int)Read(b, 20));
                if (stride < width * (bits / 8)) throw new FormatException("Invalid DDS pitch.");
                BmpDecoder.Require(b, offset, (long)stride * height);
                for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
                {
                    int p = offset + y * stride + x * (bits / 8), d = ((height - 1 - y) * width + x) * 4;
                    uint pixel = bits == 16 ? BmpDecoder.U16(b, p) : bits == 24 ? (uint)(b[p] | b[p + 1] << 8 | b[p + 2] << 16) : Read(b, p);
                    image.Pixels[d] = BmpDecoder.Channel(pixel, rm, 0); image.Pixels[d + 1] = BmpDecoder.Channel(pixel, gm, 0);
                    image.Pixels[d + 2] = BmpDecoder.Channel(pixel, bm, 0); image.Pixels[d + 3] = opaque ? (byte)255 : BmpDecoder.Channel(pixel, am, 255);
                }
            }
            else
            {
                int blocksX = (width + 3) / 4, blocksY = (height + 3) / 4, blockBytes = bc == 1 ? 8 : 16;
                BmpDecoder.Require(b, offset, (long)blocksX * blocksY * blockBytes);
                for (int by = 0; by < blocksY; by++) for (int bx = 0; bx < blocksX; bx++)
                {
                    int p = offset + (by * blocksX + bx) * blockBytes, color = p + (bc == 1 ? 0 : 8);
                    ushort c0 = BmpDecoder.U16(b, color), c1 = BmpDecoder.U16(b, color + 2);
                    byte[,] palette = new byte[4, 4];
                    for (int i = 0; i < 2; i++)
                    {
                        ushort c = i == 0 ? c0 : c1;
                        palette[i, 0] = (byte)(((c >> 11) << 3) | (c >> 13));
                        palette[i, 1] = (byte)((((c >> 5) & 63) << 2) | ((c >> 9) & 3));
                        palette[i, 2] = (byte)(((c & 31) << 3) | ((c & 31) >> 2)); palette[i, 3] = 255;
                    }
                    for (int k = 0; k < 3; k++)
                    {
                        palette[2, k] = (byte)(bc == 1 && c0 <= c1 ? (palette[0, k] + palette[1, k]) / 2 : (2 * palette[0, k] + palette[1, k]) / 3);
                        palette[3, k] = (byte)(bc == 1 && c0 <= c1 ? 0 : (palette[0, k] + 2 * palette[1, k]) / 3);
                    }
                    palette[2, 3] = 255; palette[3, 3] = (byte)(bc == 1 && c0 <= c1 ? 0 : 255);
                    byte[] alpha = new byte[8]; ulong alphaIndices = 0;
                    if (bc == 3)
                    {
                        alpha[0] = b[p]; alpha[1] = b[p + 1]; int n = alpha[0] > alpha[1] ? 7 : 5;
                        for (int i = 1; i < n; i++) alpha[i + 1] = (byte)(((n - i) * alpha[0] + i * alpha[1]) / n);
                        if (n == 5) { alpha[6] = 0; alpha[7] = 255; }
                        for (int i = 0; i < 6; i++) alphaIndices |= (ulong)b[p + 2 + i] << (8 * i);
                    }
                    uint indices = Read(b, color + 4);
                    for (int i = 0; i < 16; i++)
                    {
                        int x = bx * 4 + i % 4, y = by * 4 + i / 4;
                        if (x >= width || y >= height) continue;
                        int d = ((height - 1 - y) * width + x) * 4, ci = (int)((indices >> (2 * i)) & 3);
                        for (int k = 0; k < 4; k++) image.Pixels[d + k] = palette[ci, k];
                        if (bc == 2) image.Pixels[d + 3] = (byte)(((b[p + i / 2] >> (4 * (i % 2))) & 15) * 17);
                        if (bc == 3) image.Pixels[d + 3] = alpha[(int)((alphaIndices >> (3 * i)) & 7)];
                        if (opaque) image.Pixels[d + 3] = 255;
                    }
                }
            }
            return image;
        }
    }
}
