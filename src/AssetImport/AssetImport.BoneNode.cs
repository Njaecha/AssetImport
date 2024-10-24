using System.Collections.Generic;
using UnityEngine;

namespace AssetImport
{
    /// <summary>
    /// Node of the treeview used in the armature tab of the preload UI
    /// </summary>
    public class BoneNode
    {
        public GameObject GameObject { get; private set; }
        public BoneNode Parent { get; private set; }
        public List<BoneNode> Children { get; private set; } = new List<BoneNode>();
        public int Depth { get; set; }
        public bool IsDynamicRoot { get; private set; } = false;
        public bool NodeOpen { get; set; } = true;
        public bool IsDynamic { get; private set; } = false;
        public bool UIActive { get; private set; } = true;
        public int DynamicChainLength { get; private set; } = 0;

        private DynamicRootProperties _properties;

        public BoneNode(GameObject bone, BoneNode parent = null, int depth = 0)
        {
            this.GameObject = bone;
            this.Depth = depth;
            this.Parent = parent;
            parent?.AddChild(this);
        }

        public void AddChild(BoneNode child)
        {
            Children.Add(child);
        }

        public DynamicRootProperties GetDynamicBoneProperties()
        {
            if (!IsDynamicRoot) return null;
            return _properties ?? (_properties = new DynamicRootProperties(DynamicChainLength));
        }

        public void SetDynamic(bool active, bool subBone = false, BoneNode root = null)
        {
            if (Parent != null && Parent.IsDynamic && !subBone) return;
            if (Children.Count > 0 && Children[0].DynamicBoneInChildren()) return;
            if (!subBone)
            {
                IsDynamicRoot = active;
                DynamicChainLength = 1;
                root = this;
            }
            else if (root != null) root.DynamicChainLength++;

            IsDynamic = active;
            if (Children.Count > 0)
            {
                Children[0].SetDynamic(active, true, root);
            }
        }

        public void SetUiActive(bool active)
        {
            if (active && Parent != null && !Parent.NodeOpen) return;
            UIActive = active;
            if (Children.Count <= 0) return;
            foreach(BoneNode child in Children)
            {
                child.SetUiActive(active);
            }
        }

        public void SetNodeOpen(bool open)
        {
            NodeOpen = open;
            if (Children.Count <= 0) return;
            foreach(BoneNode child in Children)
            {
                child.SetUiActive(open);
            }
        }

        public bool DynamicBoneInChildren()
        {
            if (IsDynamicRoot) return true;
            return Children.Count > 0 && Children[0].DynamicBoneInChildren();
        }

        public class DynamicRootProperties
        {
            public readonly int ChainLength;

            public readonly List<DynamicBoneCollider> Colliders = new List<DynamicBoneCollider>();

            public float Weight = 1f;

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
                this.ChainLength = length;
                this.dampeningDistrib = new AnimationCurve(CreateDefaultKeyframeValues());
                this.elasticityDistrib = new AnimationCurve(CreateDefaultKeyframeValues());
                this.stiffnessDistrib = new AnimationCurve(CreateDefaultKeyframeValues());
                this.inertiaDistrib = new AnimationCurve(CreateDefaultKeyframeValues());
                this.radiusDistrib = new AnimationCurve(CreateDefaultKeyframeValues());
            }

            private Keyframe[] CreateDefaultKeyframeValues()
            {
                Keyframe[] frames = new Keyframe[ChainLength];
                for(int i = 0; i < ChainLength; i++)
                {
                    frames[i] = new Keyframe(i, 1);
                }
                return frames;
            }

            public void PopulateDynamicBone(DynamicBone bone, Transform rootTransform)
            {
                bone.m_Root = rootTransform;
                bone.m_notRolls = new List<Transform>();
                bone.m_Colliders = Colliders;
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
