using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.IO;
using LitJson;
using Main = AssetImport.AssetImport;
using System.Text;
using System.Linq;


namespace AssetImport
{
    /// <summary>
    /// The cache is used to make sure that a imported file is available when needed, even if the original was deleted or moved.
    /// </summary>
    public static class RamCacheUtility
    {
        // Cache entries are private runtime state, independent of the serialized card format.
        // Use a named type instead of System.Tuple, which is unavailable in KK's .NET 3.5 runtime.
        private sealed class CacheEntry
        {
            internal readonly string FileName;
            internal readonly byte[] Data;
            internal readonly List<string> AdditionalFileHashes;

            internal CacheEntry(string fileName, byte[] data, List<string> additionalFileHashes)
            {
                FileName = fileName;
                Data = data;
                AdditionalFileHashes = additionalFileHashes;
            }
        }

        private static readonly Dictionary<string, CacheEntry> BlobStorage = new Dictionary<string, CacheEntry>();

        public static string ToCache(string sourcePath)
        {
            if (File.Exists(sourcePath))
            {
                string fileName = Path.GetFileName(sourcePath);
                KeyValuePair<string, CacheEntry> kvp = DoHash(sourcePath);
                if (!BlobStorage.ContainsKey(kvp.Key))
                {
                    // GLTF stores additional files with information
                    if (fileName.ToLower().EndsWith(".gltf"))
                    {
                        List<string> additionalFileHashes = new List<string>();

                        // extra files can only be found from the source file if we are loading the file from disc.
                        GetGltFbufferPaths(kvp.Value.Data, sourcePath).ForEach(path =>
                        {
                            KeyValuePair<string, CacheEntry> kvp2 = DoHash(path);
                            if (!BlobStorage.ContainsKey(kvp2.Key))
                            {
                                BlobStorage.Add(kvp2.Key, kvp2.Value);
                            }
                            additionalFileHashes.Add(kvp2.Key);
                        });
                        BlobStorage.Add(kvp.Key, new CacheEntry(kvp.Value.FileName, kvp.Value.Data, additionalFileHashes));
                    }
                    // OBJ can store a .mtl file with material data
                    else if (fileName.ToLower().EndsWith(".obj"))
                    {
                        string mtlFile = sourcePath.Replace(".obj", ".mtl");
                        if (File.Exists(mtlFile))
                        {
                            KeyValuePair<string,CacheEntry> kvp3 = DoHash(mtlFile);
                            if (!BlobStorage.ContainsKey(kvp3.Key))
                            {
                                BlobStorage.Add(kvp3.Key, kvp3.Value);
                            }
                            BlobStorage.Add(kvp.Key, new CacheEntry(kvp.Value.FileName, kvp.Value.Data, new List<string>(){kvp3.Key}));
                        }
                        else BlobStorage.Add(kvp.Key, kvp.Value);
                    }
                    else
                    {
                        BlobStorage.Add(kvp.Key, kvp.Value);
                    }
                }
                else if (BlobStorage[kvp.Key].FileName != fileName)
                {
                    Main.Logger.LogWarning($"A file with the exact content as {fileName} has already been cached under the name {BlobStorage[kvp.Key].FileName}. This will be used instead");
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
            if (!BlobStorage.ContainsKey(file.Hash))
            {
                BlobStorage.Add(file.Hash, new CacheEntry(file.FileName, file.File, file.RelatedFiles));
            }
            return file.Hash;
        }

        internal static string ToCache(byte[] bytes, string filename, List<string> additionalHashes)
        {
            KeyValuePair<string, CacheEntry> kvp = DoHash(bytes, filename);
            if (!BlobStorage.ContainsKey(kvp.Key))
            {
                BlobStorage.Add(kvp.Key, new CacheEntry(kvp.Value.FileName, kvp.Value.Data, additionalHashes));
            }
            return kvp.Key;
        }

        private static KeyValuePair<string, CacheEntry> DoHash(string sourcePath)
        {
            return DoHash(File.ReadAllBytes(sourcePath), Path.GetFileName(sourcePath));
        }

        private static KeyValuePair<string, CacheEntry> DoHash(byte[] blob, string fileName)
        {
            using (MD5 md5 = MD5.Create())
            {
                string hashString = BitConverter.ToString(md5.ComputeHash(blob)).Replace("-", "");
                return new KeyValuePair<string, CacheEntry>(hashString, new CacheEntry(Path.GetFileName(fileName), blob, null));
            }
        }

        internal static List<string> GetGltFbufferPaths(byte[] blob, string originalPath)
        {
            var paths = new List<string>();
            JsonData data = JsonMapper.ToObject(Encoding.UTF8.GetString(blob));
            if (!data.ContainsKey("buffers")) return paths;
            paths.AddRange(from JsonData buffer in data["buffers"] select originalPath.Replace(Path.GetFileName(originalPath), (string)buffer["uri"]));
            return paths;
        }

        public static void ClearCache()
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
            if (BlobStorage.TryGetValue(hash, out CacheEntry file))
            {
                return file.Data;
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
            if (BlobStorage.TryGetValue(hash, out CacheEntry file))
            {
                return new MemoryStream(file.Data);
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
            if (BlobStorage.TryGetValue(hash, out CacheEntry file))
            {
                return file.FileName;
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
            if(BlobStorage.TryGetValue(hash, out CacheEntry file))
            {
                return file.AdditionalFileHashes;
            }
            else
            {
                Main.Logger.LogError($"File with {hash} is not in cache!");
                return null;
            }
        }
    }
}
