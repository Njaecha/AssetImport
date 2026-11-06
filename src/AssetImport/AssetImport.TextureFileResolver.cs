using System;
using System.IO;

namespace AssetImport
{
    // Resolve only explicit paths and a neighbouring file with the same name.
    // Never search unrelated folders for an arbitrary matching texture.
    internal static class TextureFileResolver
    {
        internal static string Resolve(string reference, string modelPath)
        {
            if (string.IsNullOrEmpty(reference)) return reference ?? "";
            string path = reference.Replace('\\', '/');
            if (path.StartsWith("*", StringComparison.Ordinal)) return path; // Assimp embedded texture, not a disk file.
            if (string.IsNullOrEmpty(modelPath)) return path;
            string directory = Path.GetDirectoryName(Path.GetFullPath(modelPath));
            bool rooted = Path.IsPathRooted(path) ||
                (path.Length > 2 && char.IsLetter(path[0]) && path[1] == ':' && path[2] == '/');
            string candidate = rooted ? path : Path.Combine(directory, path);
            if (File.Exists(candidate)) return Path.GetFullPath(candidate).Replace('\\', '/');
            // Exporters often retain paths from the author's machine. A texture
            // supplied next to the model is an unambiguous portable fallback.
            string besideModel = Path.Combine(directory, Path.GetFileName(path));
            if (File.Exists(besideModel)) return Path.GetFullPath(besideModel).Replace('\\', '/');
            return candidate.Replace('\\', '/');
        }

        internal static bool IsSupportedImage(string path)
        {
            string extension = Path.GetExtension(path);
            return string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".tga", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".bmp", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".dds", StringComparison.OrdinalIgnoreCase);
        }
    }
}
