using System;
using UnityEngine;

namespace AssetImport
{
    [Serializable]
    internal sealed class PhysicsJointBinding
    {
        public DynamicBone Chain;
        public PhysicsAngularJoint[] Joints = new PhysicsAngularJoint[0];
    }
    [Serializable]
    internal sealed class PhysicsAngularJoint
    {
        public Transform Parent, Bone;
        public Quaternion Frame;
        public Vector3 Min, Max, SpringRate;
        public bool Reverse;
    }
}
