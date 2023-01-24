using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace AssetImport
{
    /// <summary>
    /// AssetImport's representation of an accessory. 
    /// </summary>
    public class AccessoryHelper
    {
        private ChaFileCoordinate coordiante;
        private ChaAccessoryComponent accessoryComponent;
        public int slot { get; private set; }
        public Vector3[,] addMove { get => coordiante.accessory.parts[slot].addMove; } 
        public Renderer[] rendNormal { get => accessoryComponent.rendNormal; set => accessoryComponent.rendNormal = value; }
        public Renderer[] rendAlpha { get => accessoryComponent.rendAlpha; set => accessoryComponent.rendAlpha = value; }
        public ChaAccessoryComponent accessory { get => accessoryComponent; }

        public AccessoryHelper(ChaControl chaControl, ChaAccessoryComponent accessoryComponent, int slot)
        {
            this.coordiante = chaControl.nowCoordinate;
            this.accessoryComponent = accessoryComponent;
            this.slot = slot;
        }

        public void changeSlot(int slot)
        {
            this.slot = slot;
        }
    }
}
