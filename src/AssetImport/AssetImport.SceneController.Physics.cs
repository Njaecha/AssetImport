using System;
using System.Collections.Generic;
using Studio;

namespace AssetImport
{
    public partial class AssetSceneController
    {
        internal static bool PhysicsEnabledForLoad(LoadProcess loadProcess, OCIItem item)
        {
            return loadProcess.Kind == LoadProcess.LoadProcessKind.Normal
                ? loadProcess.Import.PhysicsEnabledAfterImport
                : item.itemInfo.enableDynamicBone;
        }

        internal static void ApplyImportedPhysicsState(OCIItem item, bool enabled, bool syncEditor = false)
        {
            // Preserve the native saved preference, then set the actual chains.
            // Studio can suppress its item-level switch while FK is active;
            // imported PMX Auto mappings deliberately leave physics chains to DB.
            // Explicit UI changes also update KKPE's cache before touching state.
            if (syncEditor) SyncPhysicsEditor(item, enabled);
            item.ActiveDynamicBone(enabled);
            item.itemInfo.enableDynamicBone = enabled;
            foreach (var bone in item.dynamicBones ?? new DynamicBone[0])
                if (bone && bone.m_Root) bone.enabled = enabled;
        }

        internal LocalizedText GetSelectedPhysicsStatus()
        {
            return SelectedPhysicsStatus(null);
        }

        internal LocalizedText SetSelectedPhysicsEnabled(bool enabled)
        {
            return SelectedPhysicsStatus(enabled);
        }

        internal LocalizedText ResetSelectedPhysics()
        {
            return SelectedPhysicsStatus(null, true);
        }

        private LocalizedText SelectedPhysicsStatus(bool? enabled, bool reset = false)
        {
            var studio = Studio.Studio.Instance;
            var selected = studio.treeNodeCtrl.selectNodes;
            if (selected == null || selected.Length == 0)
                return L.Message("Select imported items in the Studio workspace to control physics.");

            int items = 0, chains = 0, on = 0, inactive = 0, fk = 0, skipped = 0, failed = 0;
            var visited = new HashSet<int>();
            var resetChains = new List<DynamicBone>();
            foreach (var node in selected)
            {
                ObjectCtrlInfo selectedObject;
                if (!studio.dicInfo.TryGetValue(node, out selectedObject) || !(selectedObject is OCIItem))
                {
                    skipped++;
                    continue;
                }
                var item = (OCIItem)selectedObject;
                if (!visited.Add(item.itemInfo.dicKey)) continue;
                if (!LoadedObjects.ContainsKey(item.itemInfo.dicKey))
                {
                    skipped++;
                    continue;
                }
                int liveChains = 0;
                foreach (var bone in item.dynamicBones ?? new DynamicBone[0])
                    if (bone && bone.m_Root) liveChains++;
                if (liveChains == 0)
                {
                    skipped++;
                    continue;
                }
                if (enabled.HasValue)
                {
                    try
                    {
                        // Keep roots, collision lists, mappings and FK state intact.
                        ApplyImportedPhysicsState(item, enabled.Value, true);
                    }
                    catch (Exception e)
                    {
                        failed++;
                        Logger.LogWarning("Cannot switch imported item physics: " + e.Message);
                    }
                }
                if (reset) resetChains.AddRange(item.dynamicBones);
                items++;
                chains += liveChains;
                foreach (var bone in item.dynamicBones ?? new DynamicBone[0])
                {
                    if (!bone || !bone.m_Root) continue;
                    if (bone.enabled) on++;
                    if (bone.enabled && !bone.gameObject.activeInHierarchy) inactive++;
                }
                if (item.isFK) fk++;
            }
            if (items == 0)
                return L.Message("No selected AssetImport items have physics chains. Select the item itself; choose physics roots during import first.");
            LocalizedText state = L.Message(on == 0 ? "OFF" : on == chains ? "ON" : "MIXED");
            LocalizedText status = L.Message("Physics {0}: {1} item(s), {2} chain(s), {3} enabled.", state, items, chains, on);
            if (reset)
            {
                var result = PhysicsReset.Reset(resetChains);
                foreach (string message in result.FailureMessages) Logger.LogWarning("Cannot reset imported physics: " + message);
                status = L.Message("Physics reset: {0} chain(s); {1} inactive, {2} invalid, {3} failed. Settings retained.", result.ResetCount, result.InactiveCount, result.InvalidCount, result.FailedCount);
            }
            if (skipped > 0) status += L.Message(" Skipped {0} selection(s) without imported physics.", skipped);
            if (failed > 0) status += L.Message(" Failed to switch {0} item(s); check the log.", failed);
            if (inactive > 0) status += L.Message(" {0} enabled chain(s) are on inactive objects.", inactive);
            if (fk > 0) status += L.Message(" FK is ON for {0} item(s). Use PMX Auto to keep VMD off physics chains.", fk);
            return status;
        }
    }
}
