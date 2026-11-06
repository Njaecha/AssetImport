using System;
using System.Collections.Generic;
using UnityEngine;

namespace AssetImport
{
    internal static class NativeContactSelection
    {
        internal static DynamicBone Owner(Transform bone, DynamicBone[] chains)
        {
            DynamicBone owner = null;
            foreach (var chain in chains ?? new DynamicBone[0])
                if (chain && chain.m_Root && bone && bone.IsChildOf(chain.m_Root) &&
                    (!owner || chain.m_Root.IsChildOf(owner.m_Root))) owner = chain;
            return owner;
        }

        internal static NativeContactShape[] Resolve(NativeContactDefinition source, DynamicBone[] chains, PhysicsEnvironmentSettings settings)
        {
            if (source == null) return new NativeContactShape[0];
            var result = Candidates(source, chains, settings);
            KeepEndpoints(result);
            FitMeshRegions(result, settings);
            foreach (var shape in result)
            {
                var choice = Preference(settings, shape.ChainRoot);
                shape.Radius = choice.BreastRadiusOverride > 0 ? choice.BreastRadiusOverride : shape.Radius * choice.BreastRadiusScale;
                float side = choice.BreastMirrorX && shape.ReferenceCenter.x < 0 ? -1f : 1f;
                var offset = new Vector3(choice.BreastOffsetX * side, choice.BreastOffsetY, choice.BreastOffsetZ);
                shape.Center += offset; shape.ReferenceCenter += offset;
                shape.Capsule = choice.BreastCapsule;
                shape.Height = shape.Capsule ? Math.Max(shape.Radius * 2f, choice.BreastLength) : 0f;
                shape.Direction = 1;
                shape.Rotation = Quaternion.Euler(choice.BreastRotationX, choice.BreastRotationY * side, choice.BreastRotationZ * side);
            }
            return result.ToArray();
        }

        internal static List<NativeContactShape> Candidates(NativeContactDefinition source, DynamicBone[] chains, PhysicsEnvironmentSettings settings)
        {
            var result = new List<NativeContactShape>(); var seen = new HashSet<Transform>();
            Add(source.Breasts, false, chains, settings, result, seen);
            Add(source.ManualCandidates, true, chains, settings, result, seen);
            result.Sort((a, b) => Depth(a.Bone).CompareTo(Depth(b.Bone)));
            return result;
        }

        private static PhysicsChainSettings Preference(PhysicsEnvironmentSettings settings, string root)
        {
            var choice = settings == null ? null : settings.ForRoot(root);
            return choice == null ? new PhysicsChainSettings() : choice.Copy();
        }

        internal static Transform EndpointNode(NativeContactShape shape)
        {
            return shape.Driver && (!shape.ContactRoot || shape.Driver.IsChildOf(shape.ContactRoot)) ? shape.Driver : shape.Bone;
        }

        // Only the last deforming target of each branch gets a probe. PMX
        // append followers use their source joint for this topology decision;
        // if that source is itself eligible, sibling followers share its probe.
        private static void KeepEndpoints(List<NativeContactShape> shapes)
        {
            var keep = new List<NativeContactShape>();
            for (int i = 0; i < shapes.Count; i++)
            {
                var candidate = shapes[i]; var node = EndpointNode(candidate); bool terminal = true;
                for (int j = 0; j < shapes.Count; j++)
                {
                    if (i == j || candidate.ChainRoot != shapes[j].ChainRoot) continue;
                    var other = EndpointNode(shapes[j]);
                    if ((other == node && shapes[j].Bone == node && candidate.Bone != node) ||
                        (other != node && other.IsChildOf(node)) ||
                        (other == node && shapes[j].Bone != candidate.Bone && shapes[j].Bone.IsChildOf(candidate.Bone)))
                    { terminal = false; break; }
                }
                if (terminal) keep.Add(candidate);
            }
            shapes.Clear(); shapes.AddRange(keep);
        }

        // Fit only the endpoint's own weighted region, never replace it with
        // a sphere on an intermediate/root bone or encompass the entire chain.
        private static void FitMeshRegions(List<NativeContactShape> shapes, PhysicsEnvironmentSettings settings)
        {
            for (int i = 0; i < shapes.Count; i++)
            {
                var root = shapes[i];
                if (!Preference(settings, root.ChainRoot).BreastMeshVolume) continue;
                if (!root.HasMeshBounds) continue;
                var min = root.MeshMin; var max = root.MeshMax;
                var size = max - min;
                float radius = .5f * Math.Max(size.x, Math.Max(size.y, size.z));
                if (!PhysicsEnvironmentMath.Finite(radius) || radius <= .000001f) continue;
                var center = (min + max) * .5f;
                // Imported PMX rest bones have identity rotation; subtract the
                // original reference center to move the local center by the
                // same amount, independent of the current animation pose.
                root.Center += center - root.ReferenceCenter;
                root.ReferenceCenter = center; root.Radius = radius; root.Height = 0;
                root.SourceBody = "Endpoint weighted mesh region";
            }
        }

        private static void Add(NativeContactShape[] shapes, bool manualOnly, DynamicBone[] chains, PhysicsEnvironmentSettings settings,
            List<NativeContactShape> result, HashSet<Transform> seen)
        {
            foreach (var shape in shapes ?? new NativeContactShape[0])
            {
                if (shape == null || !shape.Bone || seen.Contains(shape.Bone)) continue;
                var owner = Owner(shape.Bone, chains);
                var preference = settings == null || !owner ? null : settings.ForRoot(owner.m_Root.name);
                var clean = preference == null ? new PhysicsChainSettings() : preference.Copy();
                if (clean.BreastRole == (int)BreastContactRole.Exclude ||
                    (manualOnly && clean.BreastRole != (int)BreastContactRole.Breast)) continue;
                seen.Add(shape.Bone);
                result.Add(new NativeContactShape { Bone = shape.Bone, Center = shape.Center, Rotation = shape.Rotation,
                    Driver = shape.Driver, ContactRoot = owner ? owner.m_Root : null,
                    Radius = shape.Radius, Height = shape.Height, Direction = shape.Direction,
                    SourceBody = shape.SourceBody, HasReferencePose = shape.HasReferencePose, ReferenceCenter = shape.ReferenceCenter,
                    HasMeshBounds = shape.HasMeshBounds, MeshMin = shape.MeshMin, MeshMax = shape.MeshMax,
                    Selection = clean.BreastRole == (int)BreastContactRole.Breast ? "manual" : "automatic",
                    ChainRoot = owner ? owner.m_Root.name : "" });
            }
        }
        private static int Depth(Transform bone)
        { int depth = 0; for (; bone; bone = bone.parent) depth++; return depth; }
    }
}
