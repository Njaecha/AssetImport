using System;
using System.Collections.Generic;

namespace AssetImport
{
    internal sealed class PhysicsResetResult
    {
        internal int ResetCount { get; set; }
        internal int InactiveCount { get; set; }
        internal int InvalidCount { get; set; }
        internal int FailedCount { get; set; }
        internal List<string> FailureMessages { get; } = new List<string>();
    }

    internal static class PhysicsReset
    {
        // Callers supply only their owned chains. Use the native particle reset;
        // rebuilding particles or restoring transforms here could overwrite the
        // current animation pose or another editor's changes to the rest setup.
        internal static PhysicsResetResult Reset(IEnumerable<DynamicBone> chains)
        {
            var result = new PhysicsResetResult();
            if (chains == null) return result;
            var seen = new HashSet<DynamicBone>();
            foreach (var chain in chains)
            {
                if (!chain)
                {
                    result.InvalidCount++;
                    continue;
                }
                if (!seen.Add(chain)) continue;
                if (!chain.m_Root)
                {
                    result.InvalidCount++;
                    continue;
                }
                if (!chain.enabled || !chain.gameObject.activeInHierarchy || !chain.m_Root.gameObject.activeInHierarchy)
                {
                    result.InactiveCount++;
                    continue;
                }
                string rootName = chain.m_Root.name;
                try
                {
                    chain.ResetParticlesPosition();
                    result.ResetCount++;
                }
                catch (Exception error)
                {
                    result.FailedCount++;
                    result.FailureMessages.Add(rootName + ": " + error.Message);
                }
            }
            return result;
        }
    }
}
