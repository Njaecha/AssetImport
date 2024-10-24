using UnityEngine;

namespace AssetImport
{
    /// <summary>
    /// AssetImport's representation of an accessory. 
    /// </summary>
    public class AccessoryHelper
    {
        private readonly ChaFileCoordinate _coordiante;
        public int Slot { get; private set; }
        public Vector3[,] AddMove => _coordiante.accessory.parts[Slot].addMove;
        public Renderer[] RendNormal { get => Accessory.rendNormal; set => Accessory.rendNormal = value; }
        public Renderer[] RendAlpha { get => Accessory.rendAlpha; set => Accessory.rendAlpha = value; }
        public ChaAccessoryComponent Accessory { get; }

        public AccessoryHelper(ChaControl chaControl, ChaAccessoryComponent accessoryComponent, int slot)
        {
            this._coordiante = chaControl.nowCoordinate;
            this.Accessory = accessoryComponent;
            this.Slot = slot;
        }

        public void ChangeSlot(int slot)
        {
            this.Slot = slot;
        }
    }
}
