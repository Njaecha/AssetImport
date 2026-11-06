using System;
using System.IO;

namespace AssetImport
{
    internal static class TextureImageData
    {
        internal static byte[] Read(string path)
        {
            byte[] data=File.ReadAllBytes(path);
            TgaDecoder.Image decoded = Decode(path, data);
            // ME receives PNG and stores it in the scene/card. No ME patch required.
            return decoded == null ? data : TgaDecoder.EncodePng(decoded);
        }

        // Managed decoders can feed color conversion directly, without a PNG/Unity round trip.
        internal static TgaDecoder.Image Decode(string path, byte[] data)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".tga": return TgaDecoder.Decode(data);
                case ".bmp": return BmpDecoder.Decode(data);
                case ".dds": return DdsDecoder.Decode(data);
                default: return null;
            }
        }
    }
}
