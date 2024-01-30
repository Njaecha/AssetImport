using UnityEngine;
using Studio;

namespace AssetImport
{
    /// <summary>
    /// Representation of a LoadProcess from import to finished Accessory/StudioItem
    /// </summary>
    public class LoadProcess
    {
        public GameObject _base { get; set; }
        public System.Object component { get; set; }
        public Import import { get; set; }
        public Vector3 scale { get; set; }
        public loadProcessKind kind { get; set; }
        public bool assetEnabled { get => _base.activeInHierarchy; set => _base.SetActive(value); }
        
        public LoadProcess(GameObject _base, OCIItem ociitem, Import import, Vector3 scale, loadProcessKind kind)
        {
            this._base = _base;
            this.component = ociitem;
            this.import = import;
            this.scale = scale;
            this.kind = kind;
        }
        public LoadProcess(GameObject _base, AccessoryHelper accessory, Import import, Vector3 scale, loadProcessKind kind)
        {
            this._base = _base;
            this.component = accessory;
            this.import = import;
            this.scale = scale;
            this.kind = kind;
        }

        public enum loadProcessKind
        {
            NORMAL,
            LOAD,
            IMPORT,
            COPY
        }
    }
}
