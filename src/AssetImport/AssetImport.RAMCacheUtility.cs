using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.IO;
using LitJson;
using Main = AssetImport.AssetImport;
using System.Text;
using System.Linq;
using BepInEx.Logging;


namespace AssetImport
{
    /// <summary>
    /// The cache is used to make sure that a imported file is available when needed, even if the original was deleted or moved.
    /// </summary>
    public class RAMCacheUtility
    {
        // hash -> name, blob, [additional file hashes]
        private static Dictionary<string, Tuple<string, byte[], List<string>>> BlobStorage = new Dictionary<string, Tuple<string, byte[], List<string>>>();

        public static string ToCache(string sourcePath)
        {
            if (File.Exists(sourcePath))
            {
                KeyValuePair<string, Tuple<string, byte[], List<string>>> kvp = DoHash(sourcePath);
                if (!BlobStorage.ContainsKey(kvp.Key))
                {
                    if (Path.GetFileName(sourcePath).ToLower().EndsWith(".gltf"))
                    {
                        List<string> additionalFileHashes = new List<string>();

                        // extra files can only be found from the source file if we are loading the file from disc.
                        getGLTFbufferPaths(kvp.Value.Item2, sourcePath).ForEach(path =>
                        {
                            KeyValuePair<string, Tuple<string, byte[], List<string>>> kvp2 = DoHash(path);
                            if (!BlobStorage.ContainsKey(kvp2.Key))
                            {
                                BlobStorage.Add(kvp2.Key, kvp2.Value);
                            }
                            additionalFileHashes.Add(kvp2.Key);
                        });
                        BlobStorage.Add(kvp.Key, new Tuple<string, byte[], List<string>>(kvp.Value.Item1, kvp.Value.Item2, additionalFileHashes));
                    }
                    else
                    {
                        BlobStorage.Add(kvp.Key, kvp.Value);
                    }
                }
                else if (BlobStorage[kvp.Key].Item1 != Path.GetFileName(sourcePath))
                {
                    Main.Logger.LogWarning($"A file with the exact content as {Path.GetFileName(sourcePath)} has already been cached under the name {BlobStorage[kvp.Key].Item1}. This will be used instead");
                }
                return kvp.Key;
            }
            else
            {
                Main.Logger.LogError($"Failed to cache Asset: File {sourcePath} does not exist!");
                return null;
            }
        }

        internal static string ToCache(AssetFile file)
        {
            if (!BlobStorage.ContainsKey(file.hash))
            {
                BlobStorage.Add(file.hash, new Tuple<string, byte[], List<string>>(file.fileName, file.file, file.relatedFiles));
            }
            return file.hash;
        }

        internal static string ToCache(byte[] bytes, string filename, List<string> additionalHashes)
        {
            KeyValuePair<string, Tuple<string, byte[], List<string>>> kvp = DoHash(bytes, filename);
            if (!BlobStorage.ContainsKey(kvp.Key))
            {
                BlobStorage.Add(kvp.Key, new Tuple<string, byte[], List<string>>(kvp.Value.Item1, kvp.Value.Item2, additionalHashes));
            }
            return kvp.Key;
        }

        private static KeyValuePair<string, Tuple<string, byte[], List<string>>> DoHash(string sourcePath)
        {
            return DoHash(File.ReadAllBytes(sourcePath), Path.GetFileName(sourcePath));
        }

        private static KeyValuePair<string, Tuple<string, byte[], List<string>>> DoHash(byte[] blob, string fileName)
        {
            using (MD5 md5 = MD5.Create())
            {
                string hashString = BitConverter.ToString(md5.ComputeHash(blob)).Replace("-", "");
                return new KeyValuePair<string, Tuple<string, byte[], List<string>>>(hashString, new Tuple<string, byte[], List<string>>(Path.GetFileName(fileName), blob, null));
            }
        }

        internal static List<string> getGLTFbufferPaths(byte[] blob, string originalPath)
        {
            List<string> paths = new List<string>();
            JsonData data = JsonMapper.ToObject(Encoding.UTF8.GetString(blob));
            if (!data.ContainsKey("buffers")) return paths;
            foreach (JsonData buffer in data["buffers"]) paths.Add(originalPath.Replace(Path.GetFileName(originalPath),(string)buffer["uri"]));
            return paths;
        }

        public static void clearCache()
        {
            BlobStorage.Clear();
        }

        /// <summary>
        /// Returns a MemoryStream for a file in the RAM cache, identified by its hash.
        /// </summary>
        /// <param name="hash"></param>
        /// <returns></returns>
        public static byte[] GetFileBlob(string hash)
        {
            if (BlobStorage.ContainsKey(hash))
            {
                return BlobStorage[hash].Item2;
            }
            else
            {
                Main.Logger.LogError($"File with {hash} is not in cache!");
                return null;
            }
        }

        /// <summary>
        /// Returns a MemoryStream for a file in the RAM cache, identified by its hash.
        /// </summary>
        /// <param name="hash"></param>
        /// <returns></returns>
        public static MemoryStream GetFileStream(string hash)
        {
            if (BlobStorage.ContainsKey(hash))
            {
                return new MemoryStream(BlobStorage[hash].Item2);
            }
            else
            {
                Main.Logger.LogError($"File with {hash} is not in cache!");
                return null;
            }
        }

        /// <summary>
        /// Returns the name of a file in the RAM cache, identified by its hash.
        /// </summary>
        /// <param name="hash"></param>
        /// <returns></returns>
        public static string GetFileName(string hash)
        {
            if (BlobStorage.ContainsKey(hash))
            {
                return BlobStorage[hash].Item1;
            }
            else
            {
                Main.Logger.LogError($"File with {hash} is not in cache!");
                return null;
            }
        }

        /// <summary>
        /// Returns the list of additional file hashes belonging to a file in the RAM cache, identified by its hash.
        /// </summary>
        /// <param name="hash"></param>
        /// <returns></returns>
        public static List<string> GetFileAdditionalFileHashes(string hash)
        {
            if(BlobStorage.ContainsKey(hash))
            {
                return BlobStorage[hash].Item3;
            }
            else
            {
                Main.Logger.LogError($"File with {hash} is not in cache!");
                return null;
            }
        }
    }
}
