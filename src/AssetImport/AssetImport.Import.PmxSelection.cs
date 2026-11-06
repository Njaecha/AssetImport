using System;
using System.Linq;

namespace AssetImport
{
    public partial class Import
    {
        private PmxPhysicsRootSuggestions _pmxRootSuggestions;

        internal LocalizedText SelectPmxHairRoots()
        {
            return SelectPmxSuggestedRoots(true);
        }

        internal LocalizedText SelectPmxPhysicsRoots()
        {
            return SelectPmxSuggestedRoots(false);
        }

        private LocalizedText SelectPmxSuggestedRoots(bool hairOnly)
        {
            if (!IsPmx || !ImportBones || _pmx == null)
                return L.Message("Enable Import Armature to select PMX physics roots.");
            if (_pmxRootSuggestions == null) _pmxRootSuggestions = PmxPhysicsRootSuggestions.Build(_pmx);
            var roots = hairOnly ? _pmxRootSuggestions.HairRoots : _pmxRootSuggestions.PhysicsRoots;
            LocalizedText label = L.Message(hairOnly ? "Hair" : "Physics");
            var names = BoneNodes.ToDictionary(n => n.GameObject.name, StringComparer.Ordinal);
            int added = 0, existing = 0, skipped = 0;
            foreach (int index in roots)
            {
                BoneNode node;
                string name = PmxModel.BoneName(index, _pmx.Bones[index].Name);
                if (!names.TryGetValue(name, out node))
                {
                    skipped++;
                    Logger.LogInfo("PMX auto physics root skipped: " + name + "; imported bone node is unavailable.");
                    continue;
                }
                if (node.IsDynamicRoot) { existing++; continue; }
                node.SetDynamic(true);
                if (!node.IsDynamicRoot)
                {
                    skipped++;
                    Logger.LogInfo("PMX auto physics root skipped: " + name + "; overlaps an existing parent/child root. Existing selection preserved.");
                    continue;
                }
                added++;
                for (var parent = node; parent != null; parent = parent.Parent) parent.SetNodeOpen(true);
                if (AssetDebug.Enabled) Logger.LogInfo("PMX auto " + (hairOnly ? "hair" : "physics") + " root selected: " + node.GameObject.name);
            }
            if (AssetDebug.Enabled)
                foreach (string diagnostic in _pmxRootSuggestions.Diagnostics)
                    Logger.LogInfo("PMX root suggestions: " + diagnostic);
            LocalizedText status = roots.Length == 0
                ? (hairOnly ? L.Message("No confident hair roots found. Select the hair chain roots manually.") : L.Message("No usable PMX dynamic chains found; bone names alone do not create physics."))
                : L.Message("{0} roots: {1} added, {2} already selected, {3} skipped. Other selections kept.", label, added, existing, skipped);
            if (!hairOnly && (_pmxRootSuggestions.SkippedRootCount != 0 || _pmxRootSuggestions.IgnoredBodyCount != 0))
                status += L.Message(" {0} unsupported roots and {1} unbound bodies excluded. Enable Debug for details.", _pmxRootSuggestions.SkippedRootCount, _pmxRootSuggestions.IgnoredBodyCount);
            if (skipped > 0) status += L.Message(" Check overlapping parent/child roots.");
            Logger.LogInfo("PMX auto " + (hairOnly ? "hair" : "physics") + " selection: " + status.English);
            return status;
        }

        internal void ClearPhysicsRoots()
        {
            foreach (var root in BoneNodes.Where(n => n.IsDynamicRoot).ToArray()) root.SetDynamic(false);
        }
    }
}
