using System;
using System.Collections.Generic;
using UnityEngine;

namespace AssetImport
{
    public partial class Import
    {
        internal PhysicsJointBinding[] JointPhysicsBindings = new PhysicsJointBinding[0];

        internal void PreparePmxJointPhysics(IList<DynamicBone> chains)
        {
            JointPhysicsBindings = new PhysicsJointBinding[0];
            if (!IsPmx || !ImportBones || _pmx == null || chains.Count == 0) return;
            var names = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (var bone in GameObject.GetComponentsInChildren<Transform>(true)) names[bone.name] = bone;
            var plan = PmxJointPlan.Build(_pmx);
            var bindings = new List<PhysicsJointBinding>();
            foreach (var chain in chains)
            {
                if (!chain || !chain.m_Root || chain.m_Root == GameObject.transform || !chain.m_Root.IsChildOf(GameObject.transform)) continue;
                var binding = new PhysicsJointBinding { Chain = chain };
                var joints = new List<PhysicsAngularJoint>();
                foreach (var joint in plan.Angular)
                {
                    Transform parent, bone;
                    if (!names.TryGetValue(PmxModel.BoneName(joint.Parent, _pmx.Bones[joint.Parent].Name), out parent) ||
                        !names.TryGetValue(PmxModel.BoneName(joint.Bone, _pmx.Bones[joint.Bone].Name), out bone) ||
                        bone != chain.m_Root && !bone.IsChildOf(chain.m_Root)) continue;
                    var q = PmxCollisionPlan.ConvertRotation(joint.Rotation);
                    joints.Add(new PhysicsAngularJoint
                    {
                        Parent = parent,
                        Bone = bone,
                        Reverse = joint.Reverse,
                        Frame = new Quaternion(q[0], q[1], q[2], q[3]),
                        Min = new Vector3(joint.Min.X, joint.Min.Y, joint.Min.Z),
                        Max = new Vector3(joint.Max.X, joint.Max.Y, joint.Max.Z),
                        SpringRate = new Vector3(joint.SpringRate.X, joint.SpringRate.Y, joint.SpringRate.Z)
                    });
                }
                binding.Joints = joints.ToArray(); bindings.Add(binding);
                if (AssetDebug.Enabled) Logger.LogInfo("PMX angular constraints: root=" + chain.m_Root.name + "; mapped=" + joints.Count + ". Translational/coupled joint dynamics require a rigid-body solver.");
            }
            JointPhysicsBindings = bindings.ToArray();
            if (AssetDebug.Enabled) foreach (var warning in plan.Warnings) Logger.LogInfo("PMX joints: " + warning);
            if (plan.Warnings.Count > 0) Logger.LogWarning("PMX joints: " + plan.Warnings.Count + " unsupported/invalid bindings; keeping shape retention for these parts. Enable Debug for joint IDs.");
        }
    }
}
