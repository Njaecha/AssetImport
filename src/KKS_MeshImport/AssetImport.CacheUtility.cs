using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using IllusionUtility.GetUtility;
using BepInEx;
using BepInEx.Logging;
using KKAPI;
using Studio;
using KKAPI.Studio.SaveLoad;
using KKAPI.Utilities;
using ExtensibleSaveFormat;
using MessagePack;
using System.IO;
using System.Xml;
using LitJson;
using HSPE;
using HSPE.AMModules;
using MaterialEditorAPI;
using Main = AssetImport.AssetImport;


namespace AssetImport
{
    /// <summary>
    /// The cache is used to make sure that a imported file is available when needed, even if the original was deleted or moved.
    /// </summary>
    public class CacheUtility
    {
        public static void toCache(string cache, string sourcePath)
        {
            string cacheFilePath = cache + Path.GetFileName(sourcePath);
            if (sourcePath != cacheFilePath)
            {
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
                while (File.Exists(destinationCache + copyName)) 
                {
                    copyName = $"{Path.GetFileNameWithoutExtension(sourceCache + fileName)}_Copy{num}{Path.GetExtension(sourceCache + fileName)}";
                    num++;
                }
                File.Copy(sourceCache + fileName, destinationCache + copyName, true);
                return copyName;
            }
            return null;
        }
    }
}
