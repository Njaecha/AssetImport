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
        public string sourceFile { get; set; }
        [Key("BoneIndices")]
        public List<int> dynamicBoneIndices { get; set; }
        [Key("ObjectKey")]
        public int identifier { get; set; } // in Studio: dictKey | on Character: AccessorySlot
        [Key("Scale")]
        public float[] scale { get; set; } = new float[3];
        [Key("HasBones")]
        public bool hasBones { get; set; }
        [Key("perRendererMaterials")]
        public bool perRendererMaterials { get; set; } = false;
        [Key("doFbxTranslation")]
        public bool doFbxTranslation { get; set; } = false;
    }
}
