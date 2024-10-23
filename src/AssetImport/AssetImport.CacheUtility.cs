/* No longer used since Version 3.0.0
using System;
using System.Collections.Generic;
using System.IO;
using LitJson;
using Main = AssetImport.AssetImport;


namespace AssetImport
{
    /// <summary>
    /// The cache is used to make sure that a imported file is available when needed, even if the original was deleted or moved.
    /// </summary>
    public class CacheUtility
    {
        /// <summary>
        /// Initially copies a file to cache
        /// </summary>
        /// <param name="cache">Path of cache folder</param>
        /// <param name="sourcePath">Filepath of source file</param>
        public static void toCache(string cache, string sourcePath)
        {
            string cacheFilePath = cache + Path.GetFileName(sourcePath);
            if (sourcePath != cacheFilePath)
            {
                if (File.Exists(cacheFilePath))
                {
                    Main.Logger.LogDebug($"[{Path.GetFileName(sourcePath)}] already exists in cache, and will be overwritten.");
                }
                File.Copy(sourcePath, cacheFilePath, true);
                if (sourcePath.EndsWith(".gltf"))
                {
                    foreach (string s in getGLTFbufferPaths(sourcePath))
                    {
                        File.Copy(sourcePath.Replace(Path.GetFileName(sourcePath), s), cacheFilePath.Replace(Path.GetFileName(sourcePath), s), true);
                    }
                }
            }
        }
        internal static List<string> getGLTFbufferPaths(string gltfPath)
        {
            List<string> paths = new List<string>();
            if (!File.Exists(gltfPath)) return paths;
            JsonData data = JsonMapper.ToObject(new JsonReader(File.ReadAllText(gltfPath)));
            if (!data.ContainsKey("buffers")) return paths;
            foreach (JsonData buffer in data["buffers"]) paths.Add((string)buffer["uri"]);
            return paths;
        }

        public static void clearCache()
        {
            if (Directory.Exists("./UserData/AssetImport/cache")) Directory.Delete("./UserData/AssetImport/cache", true);
            Directory.CreateDirectory("./UserData/AssetImport/cache");
        }
        internal static string copyFile(string sourceCache, string destinationCache, string fileName)
        {
            AssetImport.Logger.LogInfo($"copy file {fileName} from {sourceCache} to {destinationCache}");
            if (File.Exists(sourceCache + fileName))
            {
                if (!Directory.Exists(destinationCache)) Directory.CreateDirectory(destinationCache);
                string copyName = fileName;
                int num = 1;
                if (File.Exists(destinationCache + fileName) && FilesEqual(new FileInfo(sourceCache + fileName), new FileInfo(destinationCache + fileName)))
                {
                    return copyName;
                }
                else
                {
                    while (File.Exists(destinationCache + copyName))
                    {
                        copyName = $"{Path.GetFileNameWithoutExtension(sourceCache + fileName)}_Copy{num}{Path.GetExtension(sourceCache + fileName)}";
                        num++;
                    }
                    File.Copy(sourceCache + fileName, destinationCache + copyName, true);
                    return copyName;
                }
            }
            return null;
        }

        // thanks https://stackoverflow.com/questions/1358510/how-to-compare-2-files-fast-using-net
        private static bool FilesEqual(FileInfo first, FileInfo second)
        {
            if (first.Length != second.Length)
                return false;

            if (string.Equals(first.FullName, second.FullName, StringComparison.OrdinalIgnoreCase))
                return true;

            int iterations = (int)Math.Ceiling((double)first.Length / sizeof(Int64));

            using (FileStream fs1 = first.OpenRead())
            using (FileStream fs2 = second.OpenRead())
            {
                byte[] one = new byte[sizeof(Int64)];
                byte[] two = new byte[sizeof(Int64)];

                for (int i = 0; i < iterations; i++)
                {
                    fs1.Read(one, 0, sizeof(Int64));
                    fs2.Read(two, 0, sizeof(Int64));

                    if (BitConverter.ToInt64(one, 0) != BitConverter.ToInt64(two, 0))
                        return false;
                }
            }

            return true;
        }
    }
}
*/