using System;
using System.Collections.Generic;
using UnityEngine;

namespace AssetImport
{
    public partial class Import
    {
        internal NativeContactDefinition BuildNativeContacts()
        {
            if (!IsPmx || !ImportBones || _pmx == null) return null;
            try
            {
                var plan = PmxNativeContactPlan.Build(_pmx);
                var animation = PmxAnimationPlan.Build(_pmx);
                var bones = new Dictionary<string, Transform>(StringComparer.Ordinal);
                foreach (var bone in GameObject.GetComponentsInChildren<Transform>(true)) bones[bone.name] = bone;
                return new NativeContactDefinition { Model = GameObject.transform,
                    RecognitionReport = plan.Describe(_pmx),
                    Breasts = BindNativeShapes(plan.Breasts, bones, plan.MeshBounds, animation),
                    ManualCandidates = BindNativeShapes(plan.ManualCandidates, bones, plan.MeshBounds, animation),
                    Obstacles = BindNativeShapes(plan.Obstacles, bones, plan.MeshBounds, animation),
                    PositionGrants = BindContactGrants(bones, animation) };
            }
            catch (Exception error)
            {
                Logger.LogWarning("Optional breast contact metadata could not be prepared; ordinary import continues. " + error.Message);
                return null;
            }
        }

        private NativeContactGrant[] BindContactGrants(Dictionary<string, Transform> bones, PmxAnimationPlan animation)
        {
            var pending = new List<NativeContactGrant>();
            for (int i = 0; i < _pmx.Bones.Length; i++)
            {
                var data = _pmx.Bones[i]; Transform target, source;
                // A positional contact cannot generate a rotation grant. Local
                // grants are not implemented by the PMX animation bridge either.
                if (!animation.Grant[i] || (data.Flags & 0x200) == 0 || (data.Flags & 0x80) != 0 ||
                    data.GrantParent < 0 || data.GrantParent >= _pmx.Bones.Length ||
                    float.IsNaN(data.GrantWeight) || float.IsInfinity(data.GrantWeight) ||
                    !bones.TryGetValue(PmxModel.BoneName(i, data.Name), out target) ||
                    !bones.TryGetValue(PmxModel.BoneName(data.GrantParent, _pmx.Bones[data.GrantParent].Name), out source)) continue;
                pending.Add(new NativeContactGrant { Bone = target, Source = source, Rate = data.GrantWeight });
            }
            var ordered = new List<NativeContactGrant>();
            var targets = new HashSet<Transform>();
            foreach (var grant in pending) targets.Add(grant.Bone);
            while (pending.Count > 0)
            {
                bool progressed = false;
                for (int i = 0; i < pending.Count;)
                {
                    var grant = pending[i];
                    if (targets.Contains(grant.Source)) { i++; continue; }
                    ordered.Add(grant); targets.Remove(grant.Bone); pending.RemoveAt(i); progressed = true;
                }
                if (!progressed) break; // Cyclic optional metadata cannot block import.
            }
            return ordered.ToArray();
        }

        private NativeContactShape[] BindNativeShapes(List<PmxBodyCollider> shapes, Dictionary<string, Transform> bones,
            Dictionary<int, PmxContactBounds> meshBounds, PmxAnimationPlan animation)
        {
            var result = new List<NativeContactShape>();
            foreach (var shape in shapes)
            {
                Transform bone;
                if (!bones.TryGetValue(PmxModel.BoneName(shape.Bone, _pmx.Bones[shape.Bone].Name), out bone))
                { Logger.LogWarning("PMX breast diagnostics: binding missing for bone=" + _pmx.Bones[shape.Bone].Name + "; body=" + shape.Index); continue; }
                var reference = shape.Index >= 0 ? _pmx.RigidBodyData[shape.Index].Position : _pmx.Bones[shape.Bone].Position;
                var bound = new NativeContactShape { Bone = bone, HasReferencePose = true,
                    ReferenceCenter = new Vector3(-reference.X * PmxCollisionPlan.UnitScale,
                        reference.Y * PmxCollisionPlan.UnitScale, -reference.Z * PmxCollisionPlan.UnitScale),
                    SourceBody = shape.Index >= 0 ? "[" + shape.Index + "]" + _pmx.RigidBodyData[shape.Index].Name : "Bone spacing sphere",
                    Center = new Vector3(shape.LocalPosition.X, shape.LocalPosition.Y, shape.LocalPosition.Z),
                    Radius = shape.Radius, Height = shape.Height, Direction = shape.Direction,
                    Rotation = new Quaternion(shape.Rotation[0], shape.Rotation[1], shape.Rotation[2], shape.Rotation[3]) };
                // Weighted append followers may be separate leaf transforms at
                // every joint. Resolve their authored driving bone so those
                // leaves do not all masquerade as chain endpoints.
                int driver = shape.Bone;
                for (int depth = 0; depth < _pmx.Bones.Length && animation.Grant[driver]; depth++)
                {
                    int next = _pmx.Bones[driver].GrantParent;
                    if (next < 0 || next >= _pmx.Bones.Length || next == driver) break;
                    driver = next;
                }
                Transform driverBone;
                if (bones.TryGetValue(PmxModel.BoneName(driver, _pmx.Bones[driver].Name), out driverBone)) bound.Driver = driverBone;
                PmxContactBounds bounds;
                if (meshBounds.TryGetValue(shape.Bone, out bounds))
                {
                    bound.HasMeshBounds = true;
                    // Negated X/Z swap the two bounds; all values use the same
                    // import units and reference coordinates as PMX bodies.
                    bound.MeshMin = new Vector3(-bounds.Max.X * PmxCollisionPlan.UnitScale,
                        bounds.Min.Y * PmxCollisionPlan.UnitScale, -bounds.Max.Z * PmxCollisionPlan.UnitScale);
                    bound.MeshMax = new Vector3(-bounds.Min.X * PmxCollisionPlan.UnitScale,
                        bounds.Max.Y * PmxCollisionPlan.UnitScale, -bounds.Min.Z * PmxCollisionPlan.UnitScale);
                }
                result.Add(bound);
            }
            return result.ToArray();
        }
    }
}
