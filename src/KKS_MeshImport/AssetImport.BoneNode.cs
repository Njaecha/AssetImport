using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace AssetImport
{
    /// <summary>
    /// Node of the treeview used in the armature tab of the preload UI
    /// </summary>
    public class BoneNode
    {
        public GameObject gameObject { get; private set; }
        public BoneNode parent { get; private set; }
        public List<BoneNode> children { get; private set; } = new List<BoneNode>();
        public int depth { get; set; }
        public bool isDynamicRoot { get; private set; } = false;
        public bool nodeOpen { get; set; } = true;
        public bool isDynamic { get; private set; } = false;
        public bool uiActive { get; private set; } = true;

        public BoneNode(GameObject bone, BoneNode parent = null, int depth = 0)
        {
            this.gameObject = bone;
            this.depth = depth;
            this.parent = parent;
            if (parent != null)
            {
                parent.addChild(this);
            }
        }

        public void addChild(BoneNode child)
        {
            children.Add(child);
        }

        public void setDynamic(bool active, bool subBone = false)
        {
            if (parent != null && parent.isDynamic && !subBone) return;
            if (children.Count > 0 && children[0].dynamicBoneInChildren()) return;
            if (!subBone) isDynamicRoot = active;
            this.isDynamic = active;
            if (children.Count > 0)
            {
                children[0].setDynamic(active, true);
            }
        }

        public void setUiActive(bool active)
        {
            if (active && parent != null && !parent.nodeOpen) return;
            uiActive = active;
            if (children.Count > 0)
            {
                foreach(BoneNode child in children)
                {
                    child.setUiActive(active);
                }
            }
        }

        public void setNodeOpen(bool open)
        {
            nodeOpen = open;
            if (children.Count > 0)
            {
                foreach(BoneNode child in children)
                {
                    child.setUiActive(open);
                }
            }
        }

        public bool dynamicBoneInChildren()
        {
            if (isDynamicRoot) return true;
            if (children.Count > 0)
            {
                return children[0].dynamicBoneInChildren();
            }
            return false;
        }
    }
}
