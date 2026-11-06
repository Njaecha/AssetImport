using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Studio;

namespace AssetImport
{
    public partial class AssetSceneController
    {
        // Explicit user switches also update KKPE's saved/current value. Changing
        // only the component leaves a stale value that its next list refresh can
        // replay. Initial import does not call this: defaults must remain clean.
        internal static void SyncPhysicsEditor(OCIItem item, bool enabled)
        {
            if (item == null || !item.objectItem ||
                !Hooks.IsOptionalIntegrationAvailable(Hooks.PoseEditorAssembly)) return;
            Hooks.RunOptionalIntegration(Hooks.PoseEditorAssembly, "physics editor switch",
                () => SyncPhysicsEditorLoaded(item, enabled));
        }

        // Keep every PoseEditor type reference beyond the optional assembly
        // guard so the ordinary physics switch still works without KKPE/KKSPE.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void SyncPhysicsEditorLoaded(OCIItem item, bool enabled)
        {
            var pose = item.objectItem.GetComponent<HSPE.PoseController>();
            if (!pose) return;
            var editorField = AccessTools.Field(pose.GetType(), "_dynamicBonesEditor");
            if (editorField == null)
                throw new MissingFieldException(pose.GetType().FullName, "_dynamicBonesEditor");
            var editor = editorField.GetValue(pose);
            if (editor == null) return;
            var setEnabled = AccessTools.Method(editor.GetType(), "SetDynamicBoneEnabled",
                new[] { typeof(DynamicBone), typeof(bool) });
            if (setEnabled == null)
                throw new MissingMethodException(editor.GetType().FullName, "SetDynamicBoneEnabled");

            // A list refresh briefly moves all existing edits into a pending
            // cache. Creating an Enabled edit during that interval would stop
            // KKPE from restoring the other settings. Let its own reconstruction
            // finish once, before recording this explicit switch.
            var dirtyField = AccessTools.Field(editor.GetType(), "_dirtyDynamicBones");
            var pendingField = AccessTools.Field(editor.GetType(), "_headlessDirtyDynamicBones");
            if (dirtyField == null || pendingField == null)
                throw new MissingFieldException(editor.GetType().FullName,
                    "_dirtyDynamicBones / _headlessDirtyDynamicBones");
            var dirty = dirtyField.GetValue(editor) as IDictionary;
            var pending = pendingField.GetValue(editor) as IDictionary;
            if (dirty == null || pending == null)
                throw new InvalidOperationException("PoseEditor physics caches are unavailable.");
            foreach (var bone in item.dynamicBones ?? new DynamicBone[0])
            {
                if (!bone || !bone.m_Root || dirty.Contains(bone) ||
                    (!pending.Contains(bone.m_Root) && !pending.Contains(bone.transform))) continue;
                var restore = AccessTools.Method(editor.GetType(), "LateUpdate", Type.EmptyTypes);
                if (restore == null)
                    throw new MissingMethodException(editor.GetType().FullName, "LateUpdate");
                restore.Invoke(editor, null);
                break;
            }

            var visited = new HashSet<DynamicBone>();
            foreach (var bone in item.dynamicBones ?? new DynamicBone[0])
                if (bone && bone.m_Root && visited.Add(bone))
                    setEnabled.Invoke(editor, new object[] { bone, enabled });
        }
    }
}
