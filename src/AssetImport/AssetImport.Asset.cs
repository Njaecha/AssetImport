using System.Collections.Generic;
using MessagePack;

namespace AssetImport
{
    /// <summary>
    /// Representation of an Asset (Accessory/StudioItem) for saving.
    /// </summary>
    [MessagePackObject]
    public class Asset
    {
        [Key("SourceFile")]
        public string SourceFile { get; set; }
        [Key("BoneIndices")]
        public List<int> DynamicBoneIndices { get; set; }
        [Key("ObjectKey")]
        public int Identifier { get; set; } // in Studio: dictKey | on Character: AccessorySlot
        [Key("Scale")]
        public float[] Scale { get; set; } = new float[3];
        [Key("HasBones")]
        public bool HasBones { get; set; }
        [Key("perRendererMaterials")]
        public bool PerRendererMaterials { get; set; } = false;
        [Key("doFbxTranslation")]
        public bool DoFbxTranslation { get; set; } = false;
    }
}
