using System;
using System.Collections.Generic;
using UnityEngine;

namespace AssetImport
{
    // Map each body obstacle to the closest point on the contact's segment.
    // The native solver still owns ONE spherical particle and spring state.
    // This is a discrete closest-point approximation, refreshed before each
    // solver step, not a second simulation or a sampled row of independent balls.
    internal sealed class NativeContactCapsule : IDisposable
    {
        internal readonly GameObject Host;
        internal readonly List<DynamicBoneCollider> Colliders = new List<DynamicBoneCollider>();
        private readonly List<DynamicBoneCollider> _sources;

        internal NativeContactCapsule(Transform owner, List<DynamicBoneCollider> sources)
        {
            _sources = sources; Host = new GameObject("AI_BreastCapsuleObstacles");
            try
            {
                Host.transform.SetParent(owner, false);
                var scale = owner.lossyScale;
                Host.transform.localScale = new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z);
                foreach (var source in sources)
                {
                    var obj = new GameObject(source.name); obj.transform.SetParent(Host.transform, false);
                    Colliders.Add(obj.AddComponent<DynamicBoneCollider>());
                }
            }
            catch { Dispose(); throw; }
        }

        internal void Update(Vector3 center, Vector3 halfAxis)
        {
            for (int i = 0; i < _sources.Count; i++)
            {
                var source = _sources[i]; var proxy = Colliders[i];
                if (!source) throw new InvalidOperationException("Capsule body obstacle was removed.");
                float scale = Math.Abs(source.transform.lossyScale.x);
                var other = source.transform.position;
                var otherHalf = NativeContactGeometry.HalfAxis(source.m_Radius * scale, source.m_Height * scale,
                    (int)source.m_Direction, source.transform.rotation);
                Vector3 point, unused;
                NativeContactGeometry.Closest(center - halfAxis, center + halfAxis, other - otherHalf, other + otherHalf, out point, out unused);
                // Subtract the contact point's offset: a single native sphere
                // now has the same gap/normal as the capsule at this sample pose.
                proxy.transform.position = other - (point - center);
                proxy.transform.rotation = source.transform.rotation;
                proxy.m_Radius = source.m_Radius * scale; proxy.m_Height = source.m_Height * scale;
                proxy.m_Direction = source.m_Direction; proxy.m_Bound = source.m_Bound;
            }
        }
        public void Dispose() { if (Host) { Host.SetActive(false); UnityEngine.Object.Destroy(Host); } }
    }
}
