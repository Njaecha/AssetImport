using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using UnityEngine;
using Assimp;

namespace AssetImport
{
    /// <summary>
    /// Representation of a referenced texture. Used in the preload UI
    /// </summary>
    public class TexturePath
    {
        private string _path = "";
        public UnityEngine.Material material { get; set; }
        public Assimp.TextureType type { get; set; }
        public string file { get; private set; }
        public bool use { get; set; } = false;
        public string path { set => setPath(value); get => _path; }

        public TexturePath(UnityEngine.Material _material, Assimp.TextureType _type, string texturePath)
        {
            type = _type;
            material = _material;
            setPath(texturePath);
        }

        private void setPath(string texturePath)
        {
            string oldPath = _path;
            _path = texturePath.Replace("\\", "/");
            file = Path.GetFileName(texturePath);
            if (oldPath != texturePath && pathOkay()) use = true;
        }

        public bool pathOkay()
        {
            return File.Exists(_path) && (file.EndsWith(".png") || file.EndsWith(".jpg"));
        }
    }
}
