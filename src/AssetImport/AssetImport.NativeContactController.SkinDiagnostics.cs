using System;
using System.Text;
using UnityEngine;

namespace AssetImport
{
    internal sealed partial class NativeContactController
    {
        private void RecordWriteback()
        {
            if (!AssetDebug.Enabled) return;
            _lastWriteFrame = Time.frameCount;
            // Capture input/output against the same obstacle pose. Reports can
            // be requested in Update, after the input pose has been restored.
            if (Time.realtimeSinceStartup < _nextGeometrySample) return;
            _nextGeometrySample = Time.realtimeSinceStartup + 5f;
            try
            {
                var text = new StringBuilder();
                for (int i = 0; i < EligibleCount; i++)
                {
                    var shape = ContactShapes[i];
                    float before = float.PositiveInfinity, after = float.PositiveInfinity;
                    string nearestBefore = "none", nearestAfter = "none";
                    foreach (var obstacle in Definition.Obstacles)
                    {
                        if (NativeContactPairPolicy.Exclusion(shape, obstacle) != null) continue;
                        float a = ContactGap(shape, _inputCenters[i], obstacle);
                        float b = ContactGap(shape, _outputCenters[i], obstacle);
                        if (a < before) { before = a; nearestBefore = obstacle.SourceBody; }
                        if (b < after) { after = b; nearestAfter = obstacle.SourceBody; }
                    }
                    text.AppendLine("Contact geometry: frame=" + _lastWriteFrame + "; t=" + Number(Time.realtimeSinceStartup) +
                        "; bone=" + shape.Bone.name + "; inputGap=" + Number(before) + "; inputNearest=" + nearestBefore +
                        "; outputGap=" + Number(after) + "; outputNearest=" + nearestAfter + "; geometric estimate only");
                }
                _writebackGeometry = text.ToString();
            }
            catch (Exception error) { _writebackGeometry = "Contact geometry capture failed: " + error.Message; }
        }

        private void AppendSkinBindings(StringBuilder report)
        {
            if (Definition == null || !Definition.Model) return;
            try
            {
                var renderers = Definition.Model.gameObject.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                report.AppendLine("Skin bindings: renderers=" + renderers.Length + "; exact Transform references, not a rendered-vertex measurement");
                var direct = new int[EligibleCount]; var inherited = new int[EligibleCount];
                foreach (var renderer in renderers)
                {
                    var bones = renderer.bones;
                    report.AppendLine("Renderer: " + renderer.name + "; enabled=" + renderer.enabled +
                        "; active=" + renderer.gameObject.activeInHierarchy + "; mesh=" +
                        (renderer.sharedMesh ? renderer.sharedMesh.name : "missing") + "; vertices=" +
                        (renderer.sharedMesh ? renderer.sharedMesh.vertexCount : 0) + "; boneSlots=" + bones.Length);
                    for (int i = 0; i < EligibleCount; i++)
                    {
                        var probe = ContactShapes[i].Bone;
                        int own = 0, children = 0;
                        foreach (var bone in bones)
                            if (bone && probe)
                            { if (bone == probe) own++; else if (bone.IsChildOf(probe)) children++; }
                        direct[i] += own; inherited[i] += children;
                        if (own + children > 0)
                            report.AppendLine("  Skin probe: " + probe.name + "; directSlots=" + own + "; descendantSlots=" + children);
                    }
                }
                for (int i = 0; i < EligibleCount; i++)
                    report.AppendLine("Skin binding result: bone=" + (ContactShapes[i].Bone ? ContactShapes[i].Bone.name : "missing") +
                        "; directSlots=" + direct[i] + "; descendantSlots=" + inherited[i] +
                        "; result=" + (direct[i] + inherited[i] > 0 ? "bound" : "NO_RENDERER_REFERENCE"));
            }
            catch (Exception error) { report.AppendLine("Skin binding capture failed: " + error.Message); }
        }
    }
}
