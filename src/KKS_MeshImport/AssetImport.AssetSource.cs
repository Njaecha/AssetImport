using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using MessagePack;
using UnityEngine;

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

        public void AutoFill(string path)
        {
            AssetImport.Logger.LogDebug("Filling Source with path:" + path);
            if (!File.Exists(path))
            {
                AssetImport.Logger.LogError($"Saving source file [{path}] failed: File does not exist");
                return;
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
        }
    }
}
