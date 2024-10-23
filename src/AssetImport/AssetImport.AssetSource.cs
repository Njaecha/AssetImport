using System.Collections.Generic;
using System.IO;
using MessagePack;

namespace AssetImport
{
    /// <summary>
    /// Representation of a file used by one or multiple Assets.
    /// </summary>
    [MessagePackObject]
    public class AssetSource
    {
        [Key("Data")]
        public byte[] file { get; set; }
        [Key("Name")]
        public string fileName { get; set; }
        [Key("ExtraFileData")]
        public List<byte[]> extraFiles { get; set; } = new List<byte[]>();
        [Key("ExtraFileNames")]
        public List<string> extraFileNames { get; set; } = new List<string>();

        /// <summary>
        /// As of version 3.0.0 this is a stub
        /// </summary>
        /// <param name="path"></param>
        /// <returns>true</returns>
        public bool AutoFill(string path)
        {
            /*
            AssetImport.Logger.LogDebug("Filling Source with path:" + path);
            if (!File.Exists(path))
            {
                AssetImport.Logger.LogError($"Saving source file [{path}] failed: File does not exist");
                return false;
            }
            file = File.ReadAllBytes(path);
            fileName = Path.GetFileName(path);
            if (fileName.ToLower().EndsWith(".gltf"))
            {
                foreach (string s in CacheUtility.getGLTFbufferPaths(path))
                {
                    extraFileNames.Add(s);
                    extraFiles.Add(File.ReadAllBytes(path.Replace(fileName, s)));
                }
            }
            */
            return true;
        }
    }

    [MessagePackObject]
    public class AssetFile
    {
        [Key("Data")]
        public byte[] file { get; set; }
        [Key("Name")]
        public string fileName { get; set; }
        [Key("Hash")]
        public string hash { get; set; }

        [Key("Related")]
        public List<string> relatedFiles { get; set; }

        public bool AutoFill(string hashIdentifier)
        {
            file = RAMCacheUtility.GetFileBlob(hashIdentifier);
            if (file == null)
            {
                return false;
            }
            fileName = RAMCacheUtility.GetFileName(hashIdentifier);
            relatedFiles = RAMCacheUtility.GetFileAdditionalFileHashes(hashIdentifier);
            hash = hashIdentifier;
            return true;
        }
    }
}
