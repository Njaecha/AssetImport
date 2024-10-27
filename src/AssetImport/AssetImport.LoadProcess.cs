using UnityEngine;
using Studio;

namespace AssetImport
{
    /// <summary>
    /// Representation of a LoadProcess from import to finished Accessory/StudioItem
    /// </summary>
    public class LoadProcess
    {
        public GameObject BaseGameObject { get; }
        public object Component { get; }
        public Import Import { get; }
        public Vector3 Scale { get; }
        public LoadProcessKind Kind { get; }

        public LoadProcess(GameObject baseGameObject, OCIItem ociitem, Import import, Vector3 scale, LoadProcessKind kind)
        {
            this.BaseGameObject = baseGameObject;
            this.Component = ociitem;
            this.Import = import;
            this.Scale = scale;
            this.Kind = kind;
        }
        public LoadProcess(GameObject baseGameObject, AccessoryHelper accessory, Import import, Vector3 scale, LoadProcessKind kind)
        {
            this.BaseGameObject = baseGameObject;
            this.Component = accessory;
            this.Import = import;
            this.Scale = scale;
            this.Kind = kind;
        }

        public enum LoadProcessKind
        {
            Normal,
            Load,
            Import,
            Copy
        }
    }
}
