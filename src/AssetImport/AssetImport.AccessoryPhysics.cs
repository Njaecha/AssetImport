using System.Collections.Generic;
using UnityEngine;

namespace AssetImport
{
    // References are serialized by Unity when an accessory is duplicated. This
    // component is only an ownership record; it runs no update or simulation.
    internal sealed class AssetAccessoryPhysics : MonoBehaviour
    {
        public string SourceIdentifier;
        public Transform ImportedRoot;
        public DynamicBone[] Chains = new DynamicBone[0];

        internal List<DynamicBone> GetOwnedChains()
        {
            var result = new List<DynamicBone>();
            if (!ImportedRoot || !ImportedRoot.IsChildOf(transform) || Chains == null) return result;
            foreach (var chain in Chains)
                if (chain && chain.gameObject == gameObject && chain.m_Root &&
                    chain.m_Root.IsChildOf(ImportedRoot)) result.Add(chain);
            return result;
        }

        internal int SetEnabled(bool enabled)
        {
            var chains = GetOwnedChains();
            foreach (var chain in chains) chain.enabled = enabled;
            return chains.Count;
        }

        internal void CaptureState(Asset asset)
        {
            var chains = GetOwnedChains();
            if (chains.Count == 0) return;
            bool first = chains[0].enabled;
            // A per-chain editor owns mixed states; do not reinterpret those as
            // a request to change the accessory's global switch.
            foreach (var chain in chains) if (chain.enabled != first) return;
            asset.PhysicsEnabled = first;
        }

        internal static bool RestoreEnabled(Asset asset)
        {
            return asset == null || !asset.PhysicsEnabled.HasValue || asset.PhysicsEnabled.Value;
        }
    }
}
