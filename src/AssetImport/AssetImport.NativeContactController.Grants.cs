using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace AssetImport
{
    internal sealed partial class NativeContactController
    {
        [Serializable]
        private sealed class GrantPose
        {
            public Transform Bone, Source;
            public float Rate;
            public int ContactIndex = -1;
            public Vector3 SourceInput, Input, Written;
        }
        // Unity remaps these references when cloning a model with a written
        // contact pose, allowing the clone to restore all inherited offsets.
        [SerializeField] private GrantPose[] _grantPoses;
        private float _followerRenderWriteError;

        private void BindGrantPoses()
        {
            var affected = new Dictionary<Transform, DynamicBone>();
            var contacts = new Dictionary<Transform, int>();
            for (int i = 0; i < ContactShapes.Length; i++)
            {
                var shape = ContactShapes[i];
                affected[shape.Bone] = NativeContactSelection.Owner(shape.Bone, _environment.Chains); contacts.Add(shape.Bone, i);
            }
            var result = new List<GrantPose>();
            foreach (var grant in Definition.PositionGrants ?? new NativeContactGrant[0])
            {
                DynamicBone owner;
                if (grant == null || !grant.Bone || !grant.Source || !grant.Bone.IsChildOf(Definition.Model) ||
                    !affected.TryGetValue(grant.Source, out owner) ||
                    NativeContactSelection.Owner(grant.Bone, _environment.Chains) != owner ||
                    !PhysicsEnvironmentMath.Finite(grant.Rate) || grant.Rate == 0) continue;
                affected[grant.Bone] = owner;
                int index;
                result.Add(new GrantPose { Bone = grant.Bone, Source = grant.Source, Rate = grant.Rate,
                    ContactIndex = contacts.TryGetValue(grant.Bone, out index) ? index : -1 });
            }
            _grantPoses = result.ToArray();
        }

        private void CaptureGrantInputs()
        {
            foreach (var pose in _grantPoses)
            {
                if (!pose.Bone || !pose.Source || !pose.Bone.IsChildOf(Definition.Model) || !pose.Source.IsChildOf(Definition.Model))
                    throw new InvalidOperationException("Contact append binding was removed or changed owner.");
                pose.SourceInput = pose.Source.localPosition;
                pose.Input = pose.Written = pose.Bone.localPosition;
            }
        }

        private void ApplyContactGrants()
        {
            // Propagate only this frame's contact delta, in the same local
            // coordinates and dependency order as non-local PMX position grants.
            // The ordinary animation/physics pose is never reevaluated here.
            foreach (var pose in _grantPoses)
            {
                var delta = (pose.Source.localPosition - pose.SourceInput) * pose.Rate;
                if (!PhysicsEnvironmentMath.Finite(delta.sqrMagnitude))
                    throw new InvalidOperationException("Contact append delta is not finite.");
                pose.Written = pose.Bone.localPosition + delta;
                pose.Bone.localPosition = pose.Written;
                if (pose.ContactIndex >= 0) _writtenLocal[pose.ContactIndex] = pose.Written;
            }
        }

        private void RestoreContactGrants()
        {
            foreach (var pose in _grantPoses ?? new GrantPose[0])
                if (pose.ContactIndex < 0)
                {
                    if (pose.Bone && (pose.Bone.localPosition - pose.Written).sqrMagnitude < 1e-16f) pose.Bone.localPosition = pose.Input;
                    else if (pose.Bone) _changedAfterWrite++;
                }
        }

        private void AppendGrantDiagnostics(StringBuilder report)
        {
            report.AppendLine("Contact append followers=" + (_grantPoses == null ? 0 : _grantPoses.Length) + "; mode=PMX position delta; no independent solvers");
            foreach (var grant in Definition.PositionGrants ?? new NativeContactGrant[0])
                report.AppendLine("Position grant: bone=" + (grant.Bone ? grant.Bone.name : "missing") +
                    "; source=" + (grant.Source ? grant.Source.name : "missing") + "; rate=" + Number(grant.Rate));
            foreach (var pose in _grantPoses ?? new GrantPose[0])
                report.AppendLine("Follower writeback: bone=" + (pose.Bone ? pose.Bone.name : "missing") +
                    "; source=" + (pose.Source ? pose.Source.name : "missing") + "; rate=" + Number(pose.Rate) +
                    "; frame=" + _lastWriteFrame + "; localDelta=" + Vector(pose.Written - pose.Input));
        }

        private void ObserveContactGrants()
        {
            _followerRenderWriteError = 0;
            foreach (var pose in _grantPoses ?? new GrantPose[0])
                if (pose.Bone)
                    _followerRenderWriteError = Math.Max(_followerRenderWriteError,
                        (pose.Bone.localPosition - pose.Written).magnitude *
                        (pose.Bone.parent ? Math.Abs(pose.Bone.parent.lossyScale.x) : 1f));
        }
    }
}
