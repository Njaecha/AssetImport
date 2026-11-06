using System;
using System.Collections.Generic;

namespace AssetImport
{
    public partial class Import
    {
        // Newly constructed components only. External editors restore their
        // saved changes later; no Update/LateUpdate code reapplies this preset.
        internal void ApplyPmxPhysicsDefaults(IList<DynamicBone> chains)
        {
            if (!IsPmx || !ImportBones || _pmx == null || chains == null || PhysicsProfile == 0) return;
            if (PhysicsProfile != 1)
            {
                Logger.LogWarning("Unknown PMX physics profile " + PhysicsProfile + "; keeping component defaults.");
                return;
            }
            if (_pmxRootSuggestions == null) _pmxRootSuggestions = PmxPhysicsRootSuggestions.Build(_pmx);
            var names = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < _pmx.Bones.Length; i++)
                names[PmxModel.BoneName(i, _pmx.Bones[i].Name)] = i;
            foreach (var chain in chains)
            {
                int index;
                if (!chain || !chain.m_Root || !chain.m_Root.IsChildOf(GameObject.transform) || !names.TryGetValue(chain.m_Root.name, out index)) continue;
                var preset = PmxPhysicsPresets.For(_pmx, index, _pmxRootSuggestions);
                if (preset == null || !preset.Apply(chain, PhysicsProfile)) continue;
                if (AssetDebug.Enabled) Logger.LogInfo("PMX physics preset v1: " + chain.m_Root.name + "; " + preset.Name +
                    "; updateRate=" + chain.m_UpdateRate + "; damping=" + chain.m_Damping +
                    "; elasticity=" + chain.m_Elasticity + "; stiffness=" + chain.m_Stiffness +
                    "; inertia=" + chain.m_Inert + ". Reference-based preview; KKPE edits remain available.");
            }
        }
    }
}
