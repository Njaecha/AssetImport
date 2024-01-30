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
    [MessagePackObject]
    public class Package
    {
        [Key("Data")]
        public byte[] file { get; set; }
        [Key("Name")]
        public string fileName { get; set; }
        [Key("BoneIndices")]
        public List<int> dynamicBoneIndices { get; set; }
        [Key("ObjectKey")]
        public int objectKey { get; set; }
        [Key("Scale")]
        public float[] scale { get; set; } = new float[3];
        [Key("HasBones")]
        public bool hasBones { get; set; }
        [Key("ExtraFileData")]
        public List<byte[]> extraFiles { get; set; } = new List<byte[]>();
        [Key("ExtraFileNames")]
        public List<string> extraFileNames { get; set; } = new List<string>();
    }
}
