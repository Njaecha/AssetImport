using System.Collections.Generic;
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
        public int dynamicChainLength { get; private set; } = 0;

        private DynamicRootProperties properties;

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

        public DynamicRootProperties getDynamicBoneProperties()
        {
            if (!isDynamicRoot) return null;
            if (properties == null) properties = new DynamicRootProperties(dynamicChainLength);
            return properties;
        }

        public void setDynamic(bool active, bool subBone = false, BoneNode root = null)
        {
            if (parent != null && parent.isDynamic && !subBone) return;
            if (children.Count > 0 && children[0].dynamicBoneInChildren()) return;
            if (!subBone)
            {
                isDynamicRoot = active;
                dynamicChainLength = 1;
                root = this;
            }
            else root.dynamicChainLength++;
            isDynamic = active;
            if (children.Count > 0)
            {
                children[0].setDynamic(active, true, root);
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

        public class DynamicRootProperties
        {
            public readonly int chainLength;

            public List<DynamicBoneCollider> colliders = new List<DynamicBoneCollider>();

            public float weight = 1f;

            [Range(0f, 1f)]
            public float dampening = 0.1f;
            public AnimationCurve dampeningDistrib;

            [Range(0f, 1f)]
            public float elasticity = 0.1f;
            public AnimationCurve elasticityDistrib;

            [Range(0f, 1f)]
            public float stiffness = 0.1f;
            public AnimationCurve stiffnessDistrib;

            [Range(0f, 1f)]
            public float inertia = 0.1f;
            public AnimationCurve inertiaDistrib;

            [Range(0f, 1f)]
            public float radius = 0.1f;
            public AnimationCurve radiusDistrib;

            public Vector3 gravity = Vector3.zero;
            public Vector3 force = Vector3.zero;

            public DynamicRootProperties(int length)
            {
                this.chainLength = length;
                this.dampeningDistrib = new AnimationCurve(createDefaultKeyframeValues());
                this.elasticityDistrib = new AnimationCurve(createDefaultKeyframeValues());
                this.stiffnessDistrib = new AnimationCurve(createDefaultKeyframeValues());
                this.inertiaDistrib = new AnimationCurve(createDefaultKeyframeValues());
                this.radiusDistrib = new AnimationCurve(createDefaultKeyframeValues());
            }

            private Keyframe[] createDefaultKeyframeValues()
            {
                Keyframe[] frames = new Keyframe[chainLength];
                for(int i = 0; i < chainLength; i++)
                {
                    frames[i] = new Keyframe(i, 1);
                }
                return frames;
            }

            public void populateDynamicBone(DynamicBone bone, Transform rootTransform)
            {
                bone.m_Root = rootTransform;
                bone.m_notRolls = new List<Transform>();
                bone.m_Colliders = colliders;
                bone.m_Elasticity = elasticity;
                bone.m_ElasticityDistrib = elasticityDistrib;
                bone.m_Damping = dampening;
                bone.m_DampingDistrib = dampeningDistrib;
                bone.m_Stiffness = stiffness;
                bone.m_StiffnessDistrib = stiffnessDistrib;
                bone.m_Inert = inertia;
                bone.m_InertDistrib = inertiaDistrib;
                bone.m_Radius = radius;
                bone.m_RadiusDistrib = radiusDistrib;
            }
        }
    }
}
