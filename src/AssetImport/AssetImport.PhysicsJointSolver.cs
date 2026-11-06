using System;
using System.Collections.Generic;
using UnityEngine;

namespace AssetImport
{
    // Angular part of ancestor-linked PMX joints only. DynamicBone still owns
    // integration, positions, contacts and lengths; no rigid bodies are created.
    internal sealed class PhysicsJointSolver
    {
        private readonly Dictionary<Transform, PhysicsAngularJoint> _joints = new Dictionary<Transform, PhysicsAngularJoint>();
        private readonly Dictionary<Transform, Quaternion> _frames = new Dictionary<Transform, Quaternion>();
        private int[] _children = new int[0];
        internal int Count { get { return _joints.Count; } }
        internal PhysicsJointSolver(PhysicsJointBinding binding)
        {
            foreach (var joint in binding.Joints ?? new PhysicsAngularJoint[0])
                if (joint != null && joint.Bone && joint.Parent) _joints[joint.Bone] = joint;
        }

        internal void Apply(DynamicBone chain, Transform model, bool integrated)
        {
            // An editor's frozen axis takes priority over imported limits.
            if (_joints.Count == 0 || (int)chain.m_FreezeAxis != 0) return;
            var particles = chain.m_Particles;
            _frames.Clear();
            if (_children.Length != particles.Count) _children = new int[particles.Count];
            else Array.Clear(_children, 0, _children.Length);
            for (int i = 1; i < particles.Count; i++)
                if (particles[i].m_ParentIndex >= 0 && particles[i].m_ParentIndex < i) _children[particles[i].m_ParentIndex]++;
            float rate = PhysicsEnvironmentMath.Finite(chain.m_UpdateRate) && chain.m_UpdateRate > 0 ? chain.m_UpdateRate : 60f;
            float dt = 1f / Math.Max(1f, Math.Min(240f, rate));
            for (int i = 1; i < particles.Count; i++)
            {
                var p = particles[i];
                if (p.m_ParentIndex < 0 || p.m_ParentIndex >= i || _children[p.m_ParentIndex] != 1) continue;
                var parent = particles[p.m_ParentIndex]; var bone = parent.m_Transform;
                if (!bone || !bone.parent) continue;
                var rest = p.m_Transform ? p.m_Transform.position - bone.position : bone.TransformVector(p.m_EndOffset);
                float length = rest.magnitude;
                var delta = p.m_Position - parent.m_Position;
                if (!PhysicsEnvironmentMath.Finite(length) || length < 1e-8f || delta.sqrMagnitude < 1e-16f) continue;
                var localDirection = Quaternion.Inverse(bone.rotation) * (rest / length);
                Quaternion parentFrame;
                if (!_frames.TryGetValue(bone.parent, out parentFrame)) parentFrame = bone.parent.rotation;
                var reference = parentFrame * Quaternion.Inverse(bone.parent.rotation) * bone.rotation;
                var rotation = Quaternion.FromToRotation(reference * localDirection, delta.normalized) * reference;
                PhysicsAngularJoint joint;
                if (_joints.TryGetValue(bone, out joint) && joint.Parent && joint.Parent.IsChildOf(model))
                {
                    Quaternion anchor;
                    if (!_frames.TryGetValue(joint.Parent, out anchor)) anchor = joint.Parent.rotation;
                    var relative = Quaternion.Inverse(joint.Frame) * Quaternion.Inverse(anchor) * rotation * joint.Frame;
                    if (joint.Reverse) relative = Quaternion.Inverse(relative);
                    var limited = Limit(relative, joint.Min, joint.Max, joint.SpringRate, integrated ? dt : 0f);
                    if (joint.Reverse) limited = Quaternion.Inverse(limited);
                    var target = anchor * joint.Frame * limited * Quaternion.Inverse(joint.Frame);
                    rotation = Quaternion.Slerp(rotation, target, Math.Min(1f, chain.m_Weight));
                }
                var corrected = parent.m_Position + rotation * localDirection * length;
                // Angular projection runs after the native constraint pass.
                // Recheck its colliders so a joint cannot undo a body contact.
                if ((corrected - p.m_Position).sqrMagnitude > 1e-16f && chain.m_Colliders != null)
                    foreach (var collider in chain.m_Colliders)
                        if (collider && collider.enabled)
                            collider.Collide(ref corrected, Math.Max(0f, p.m_Radius) * Math.Abs(chain.transform.lossyScale.x));
                var direction = corrected - parent.m_Position;
                if (direction.sqrMagnitude > 1e-16f)
                {
                    corrected = parent.m_Position + direction.normalized * length;
                    rotation = Quaternion.FromToRotation(rotation * localDirection, direction.normalized) * rotation;
                }
                _frames[bone] = rotation;
                // Projection must not become a new Verlet impulse.
                p.m_PrevPosition += corrected - p.m_Position;
                p.m_Position = corrected;
            }
        }

        internal static Quaternion Limit(Quaternion relative, Vector3 min, Vector3 max, Vector3 springRate, float dt)
        {
            var angles = Angles(relative);
            return Rotation(new Vector3(Axis(angles.x, min.x, max.x, springRate.x, dt),
                Axis(angles.y, min.y, max.y, springRate.y, dt), Axis(angles.z, min.z, max.z, springRate.z, dt)));
        }
        private static float Axis(float value, float min, float max, float spring, float dt)
        {
            value /= 1f + Math.Max(0f, spring) * dt * dt;
            return min > max ? value : Math.Max(min, Math.Min(max, value));
        }
        // Match the XYZ angle convention of Bullet's Generic6Dof joint. Unity's
        // Quaternion.eulerAngles uses a different order and cannot be used here.
        internal static Vector3 Angles(Quaternion q)
        {
            double x = q.x, y = q.y, z = q.z, w = q.w;
            double sy = 2 * (x * z - y * w);
            if (sy <= -.999999) return new Vector3((float)-Math.Atan2(2 * (x * y - z * w), 1 - 2 * (x * x + z * z)), (float)(-Math.PI / 2), 0);
            if (sy >= .999999) return new Vector3((float)Math.Atan2(2 * (x * y - z * w), 1 - 2 * (x * x + z * z)), (float)(Math.PI / 2), 0);
            return new Vector3((float)Math.Atan2(-2 * (y * z + x * w), 1 - 2 * (x * x + y * y)), (float)Math.Asin(sy),
                (float)Math.Atan2(-2 * (x * y + z * w), 1 - 2 * (y * y + z * z)));
        }
        internal static Quaternion Rotation(Vector3 a)
        {
            return new Quaternion(0, 0, (float)Math.Sin(-a.z * .5), (float)Math.Cos(a.z * .5)) *
                new Quaternion(0, (float)Math.Sin(-a.y * .5), 0, (float)Math.Cos(a.y * .5)) *
                new Quaternion((float)Math.Sin(-a.x * .5), 0, 0, (float)Math.Cos(a.x * .5));
        }
    }
}
