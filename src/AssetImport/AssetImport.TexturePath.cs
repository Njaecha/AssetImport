using System.IO;

namespace AssetImport
{
    /// <summary>
    /// Representation of a referenced texture. Used in the preload UI
    /// </summary>
    public class TexturePath
    {
        private string _path = "";
        public UnityEngine.Material Material { get; set; }
        public Assimp.TextureType Type { get; set; }
        public string File { get; private set; }
        public bool Use { get; set; } = false;
        public string Path { set => SetPath(value); get => _path; }

        public TexturePath(UnityEngine.Material material, Assimp.TextureType type, string texturePath)
        {
            Type = type;
            Material = material;
            SetPath(texturePath);
        }

        private void SetPath(string texturePath)
        {
            string oldPath = _path;
            _path = texturePath.Replace("\\", "/");
            File = System.IO.Path.GetFileName(texturePath);
            if (oldPath != texturePath && PathOkay()) Use = true;
        }

        public bool PathOkay()
        {
            return System.IO.File.Exists(_path) && (File.EndsWith(".png") || File.EndsWith(".jpg"));
        }
    }
}
