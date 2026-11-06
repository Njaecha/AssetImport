using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace AssetImport
{
    internal sealed partial class NativeContactController
    {
        private long _cameraCallbacks, _ticks, _solverSteps, _poseWrites, _changedAfterWrite, _resets, _heartbeatTicks, _lateUpdates, _renderObservations;
        private int _observedRenderFrame = -1;
        private float _renderWriteError, _maximumRenderWriteError;
        private Vector3[] _renderCenters;
        private int _lastWriteFrame = -1;
        private float _nextGeometrySample;
        private string _writebackGeometry = "Contact geometry: not sampled";
        private float _lastElapsed, _lastDisplacement, _maximumDisplacement, _nextDiagnostic;
        private string _diagnosticState = "awaiting-update", _lastFailure = "none";
        private readonly Queue<string> _diagnosticEvents = new Queue<string>();
        private bool _diagnosticFailureReported;
        private bool _observingDebug;

        private void SyncDiagnostics()
        {
            bool enabled = AssetDebug.Enabled;
            if (_observingDebug == enabled) return;
            StopDiagnosticObservation();
            _observingDebug = enabled;
            if (enabled) Camera.onPreRender += BeforeRender;
            _diagnosticEvents.Clear(); _renderCenters = null;
            _cameraCallbacks = _renderObservations = 0;
            _renderWriteError = _maximumRenderWriteError = _followerRenderWriteError = 0;
            _observedRenderFrame = _lastWriteFrame = -1;
            _nextGeometrySample = 0; _heartbeatTicks = _ticks;
            _nextDiagnostic = Time.realtimeSinceStartup + 5f;
            _diagnosticFailureReported = false;
            _writebackGeometry = "Contact geometry: not sampled";
        }
        private void StopDiagnosticObservation()
        { Camera.onPreRender -= BeforeRender; _observingDebug = false; }

        internal LocalizedText DiagnosticSummary
        {
            get { return L.Message("Bodies: {0} breast / {1} obstacles\nUpdates: {2}; shift: {3}",
                EligibleCount, Definition == null ? 0 : Definition.Obstacles.Length, _ticks, Number(_lastDisplacement)); }
        }
        internal static string Number(float value) { return value.ToString("G6", CultureInfo.InvariantCulture); }
        private static string Vector(Vector3 v) { return "(" + Number(v.x) + "," + Number(v.y) + "," + Number(v.z) + ")"; }
        private string ModelName { get { return Definition != null && Definition.Model ? Definition.Model.name : transform.name; } }
        internal void ReportSettingsApplied()
        { DiagnosticEvent("settings-applied; enabled=" + (_environment && _environment.Settings.NativeBreastContacts)); }

        private void Blocked(string state, LocalizedText status)
        { Release(); _lastDisplacement = 0; Status = status; DiagnosticState(state); }
        private void DiagnosticState(string state)
        {
            if (_diagnosticState == state) return;
            _diagnosticState = state;
            if (!AssetDebug.Enabled) return;
            DiagnosticEvent("state=" + state + "; enabled=" + (_environment && _environment.Settings.NativeBreastContacts) +
                "; breasts=" + EligibleCount + "; obstacles=" + (Definition == null ? 0 : Definition.Obstacles.Length));
        }
        private void DiagnosticEvent(string message)
        {
            if (!AssetDebug.Enabled) return;
            // Diagnostics must not change whether the contact layer can run.
            try
            {
                string line = "t=" + Number(Time.realtimeSinceStartup) + "; model=" + ModelName + "; " + message;
                if (_diagnosticEvents.Count >= 64) _diagnosticEvents.Dequeue();
                _diagnosticEvents.Enqueue(line);
                NativeContactDiagnostics.Request("model-state");
                AssetImport.Logger.LogInfo("PMX breast diagnostics: " + line);
            }
            catch { }
        }
        private string Counters()
        {
            return "state=" + _diagnosticState + "; writePhase=LateUpdate; lateUpdates=" + _lateUpdates +
                "; cameraCallbacks=" + _cameraCallbacks + "; renderObservations=" + _renderObservations +
                "; observedRenderFrame=" + _observedRenderFrame + "; renderWriteError=" + Number(_renderWriteError) +
                "; maxRenderWriteError=" + Number(_maximumRenderWriteError) +
                "; followerRenderWriteError=" + Number(_followerRenderWriteError) + "; ticks=" + _ticks +
                "; solverSteps=" + _solverSteps + "; poseWrites=" + _poseWrites + "; dt=" + Number(_lastElapsed) +
                "; lastDisplacement=" + Number(_lastDisplacement) + "; maxDisplacement=" + Number(_maximumDisplacement) +
                "; changedAfterWrite=" + _changedAfterWrite + "; resets=" + _resets;
        }
        private void DiagnosticHeartbeat()
        {
            if (!AssetDebug.Enabled) return;
            if (!_environment) _environment = GetComponent<AssetPhysicsEnvironment>();
            if (!_environment || !_environment.Settings.NativeBreastContacts || Time.realtimeSinceStartup < _nextDiagnostic) return;
            _nextDiagnostic = Time.realtimeSinceStartup + 5f;
            try
            {
                long since = _ticks - _heartbeatTicks; _heartbeatTicks = _ticks;
                if (since == 0 && !_failed) Status = L.Message("Breast contacts: no late update; save diagnostics");
                DiagnosticEvent("summary; ticksSinceSummary=" + since + "; " + Counters());
                if (Definition != null)
                    foreach (var shape in ContactShapes) DiagnosticEvent(ContactSample(shape));
            }
            catch (Exception error)
            {
                if (!_diagnosticFailureReported)
                { _diagnosticFailureReported = true; DiagnosticEvent("diagnostic sampling failed: " + error.Message); }
            }
        }

        // Observe only. Advancing or restoring here could miss the skinning
        // pass or give different cameras different poses in the same frame.
        private void BeforeRender(Camera camera)
        {
            if (!AssetDebug.Enabled) return;
            _cameraCallbacks++;
            if (!_written || _frame != Time.frameCount || !isActiveAndEnabled || Definition == null) return;
            try
            {
                if (_renderCenters == null || _renderCenters.Length != EligibleCount) _renderCenters = new Vector3[EligibleCount];
                _renderWriteError = 0;
                for (int i = 0; i < EligibleCount; i++)
                {
                    var shape = ContactShapes[i];
                    if (!shape.Bone) continue;
                    _renderCenters[i] = shape.Bone.TransformPoint(shape.Center);
                    _renderWriteError = Math.Max(_renderWriteError, (_renderCenters[i] - _outputCenters[i]).magnitude);
                }
                _maximumRenderWriteError = Math.Max(_maximumRenderWriteError, _renderWriteError);
                ObserveContactGrants();
                _observedRenderFrame = Time.frameCount; _renderObservations++;
            }
            catch (Exception error)
            {
                if (!_diagnosticFailureReported)
                { _diagnosticFailureReported = true; DiagnosticEvent("render observation failed: " + error.Message); }
            }
        }

        // Read-only geometric observation, not a collision solver or proof that
        // the native component used a pair. Sample only at report time.
        internal static float SurfaceGap(Vector3 center, float radius, NativeContactShape obstacle)
        {
            float scale = Math.Abs(obstacle.Bone.lossyScale.x);
            return NativeContactPairPolicy.Gap(center, radius, obstacle.Bone.TransformPoint(obstacle.Center),
                obstacle.Bone.rotation * obstacle.Rotation, obstacle.Radius * scale, obstacle.Height * scale, obstacle.Direction);
        }
        internal static float ContactGap(NativeContactShape shape, Vector3 center, NativeContactShape obstacle)
        {
            float scale = Math.Abs(shape.Bone.lossyScale.x), otherScale = Math.Abs(obstacle.Bone.lossyScale.x);
            return NativeContactGeometry.Gap(center, shape.Bone.rotation * NativeContactGeometry.ContactHalfAxis(shape) * scale,
                shape.Radius * scale, obstacle.Bone.TransformPoint(obstacle.Center),
                NativeContactGeometry.HalfAxis(obstacle.Radius, obstacle.Height, obstacle.Direction, obstacle.Bone.rotation * obstacle.Rotation) * otherScale,
                obstacle.Radius * otherScale);
        }
        private string ContactSample(NativeContactShape shape)
        {
            if (!shape.Bone) return "sample: missing breast bone";
            float radius = shape.Radius * Math.Abs(shape.Bone.lossyScale.x);
            Vector3 center = shape.Bone.TransformPoint(shape.Center);
            float gap = float.PositiveInfinity; string nearest = "none"; int overlap = 0, excluded = 0;
            foreach (var obstacle in Definition.Obstacles)
            {
                if (NativeContactPairPolicy.Exclusion(shape, obstacle) != null) { excluded++; continue; }
                float distance = ContactGap(shape, center, obstacle);
                if (distance <= 0) overlap++;
                if (distance < gap) { gap = distance; nearest = obstacle.Bone.name + "/" + obstacle.SourceBody; }
            }
            return "sample bone=" + shape.Bone.name + "; center=" + Vector(center) + "; radius=" + Number(radius) +
                "; nearest=" + nearest + "; surfaceGap=" + Number(gap) + "; estimatedOverlaps=" + overlap +
                "; excludedPairs=" + excluded + "; chainEnabled=" + (_environment && EnabledFor(shape)) + "; geometric estimate only";
        }

        private string ProbeState(int i)
        {
            var bone = ContactShapes[i].Bone;
            return "Probe: " + (bone ? bone.name : "missing") + "; boundObstacles=" + _pairCounts[i] +
                "; nativeColliderCount=" + _engines[i].ColliderCount +
                "; rawDisplacement=" + Number(_engines[i].LastRawDisplacement) +
                "; maxRawDisplacement=" + Number(_engines[i].MaximumRawDisplacement) +
                "; outputRebases=" + _engines[i].OutputRebases;
        }

        internal string BuildDiagnosticReport()
        {
            if (!AssetDebug.Enabled) return "Debug is disabled.";
            if (!_environment) _environment = GetComponent<AssetPhysicsEnvironment>();
            var report = new StringBuilder();
            report.AppendLine("Model: " + ModelName);
            report.AppendLine("Status: " + Status.English);
            report.AppendLine("Controller active: " + isActiveAndEnabled + "; environment present: " + (bool)_environment);
            report.AppendLine("Applied enabled: " + (_environment && _environment.Settings.NativeBreastContacts));
            report.AppendLine(Counters());
            report.AppendLine("Last failure (retained across retries): " + _lastFailure);
            if (Definition != null)
            {
                report.AppendLine("Recognition:\n" + Definition.RecognitionReport);
                report.AppendLine("Contact policy: branch-endpoints; shared-driver deduplicated; authored-reference-overlap excluded");
                report.AppendLine("Capsule method: per-step closest-point obstacle mapping; one native spring per endpoint; no angular response");
                report.AppendLine("Bound breasts=" + EligibleCount + "; obstacles=" + Definition.Obstacles.Length);
                var selected = new HashSet<Transform>();
                foreach (var shape in ContactShapes) selected.Add(shape.Bone);
                foreach (var candidate in NativeContactSelection.Candidates(Definition,
                    _environment ? _environment.Chains : null, _environment ? _environment.Settings : null))
                    report.AppendLine("Endpoint candidate: bone=" + candidate.Bone.name +
                        "; parent=" + (candidate.Bone.parent ? candidate.Bone.parent.name : "none") +
                        "; driver=" + (candidate.Driver ? candidate.Driver.name : "none") +
                        "; endpointNode=" + NativeContactSelection.EndpointNode(candidate).name +
                        "; chain=" + candidate.ChainRoot + "; result=" + (selected.Contains(candidate.Bone) ? "endpoint" :
                            selected.Contains(NativeContactSelection.EndpointNode(candidate)) ? "shared-driver" : "intermediate"));
                for (int i = 0; i < ContactShapes.Length; i++)
                {
                    var shape = ContactShapes[i];
                    report.AppendLine("Contact selection: bone=" + (shape.Bone ? shape.Bone.name : "missing") +
                        "; selection=" + shape.Selection + "; chain=" + shape.ChainRoot + "; source=" + shape.SourceBody +
                        "; radius=" + Number(shape.Radius) + "; localCenter=" + Vector(shape.Center) +
                        "; shape=" + (shape.Capsule ? "capsule" : "sphere") + "; length=" + Number(shape.Height) +
                        "; halfAxis=" + Vector(NativeContactGeometry.ContactHalfAxis(shape)) +
                        "; worldRadius=" + Number(shape.Radius * (shape.Bone ? Math.Abs(shape.Bone.lossyScale.x) : 0)) +
                        "; endpointNode=" + (NativeContactSelection.EndpointNode(shape) ? NativeContactSelection.EndpointNode(shape).name : "missing") +
                        "; parent=" + (shape.Bone && shape.Bone.parent ? shape.Bone.parent.name : "none"));
                    report.AppendLine(ContactSample(shape));
                    if (_engines != null && i < _engines.Length && _engines[i] != null)
                        report.AppendLine(ProbeState(i));
                    if (_inputCenters != null && i < _inputCenters.Length && _lastWriteFrame >= 0)
                    {
                        report.AppendLine("Writeback: bone=" + (shape.Bone ? shape.Bone.name : "missing") +
                            "; frame=" + _lastWriteFrame +
                            "; inputCenter=" + Vector(_inputCenters[i]) + "; outputCenter=" + Vector(_outputCenters[i]) +
                            "; appliedDelta=" + Vector(_outputCenters[i] - _inputCenters[i]) +
                            "; observedRenderCenter=" + (_renderCenters != null && i < _renderCenters.Length ? Vector(_renderCenters[i]) : "not-observed"));
                    }
                    foreach (var obstacle in Definition.Obstacles)
                        report.AppendLine("Pair: breast=" + (shape.Bone ? shape.Bone.name : "missing") + "; obstacle=" + obstacle.SourceBody +
                            "; result=" + (NativeContactPairPolicy.Exclusion(shape, obstacle) ?? "allowed") +
                            "; referenceGap=" + (shape.HasReferencePose && obstacle.HasReferencePose ? Number(NativeContactPairPolicy.ReferenceGap(shape, obstacle)) : "unavailable") +
                            "; liveGap=" + (shape.Bone && obstacle.Bone ? Number(ContactGap(shape, shape.Bone.TransformPoint(shape.Center), obstacle)) : "unavailable"));
                }
                foreach (var shape in Definition.Obstacles)
                    report.AppendLine("Obstacle: " + shape.SourceBody + "; bone=" + (shape.Bone ? shape.Bone.name : "missing") +
                        "; localCenter=" + Vector(shape.Center) + "; radius=" + Number(shape.Radius) + "; height=" + Number(shape.Height));
                AppendGrantDiagnostics(report);
            }
            report.AppendLine(_writebackGeometry);
            AppendSkinBindings(report);
            if (_environment)
                foreach (var chain in _environment.Chains)
                    if (chain)
                    {
                        var setting = _environment.Settings.ForRoot(chain.m_Root ? chain.m_Root.name : "") ?? new PhysicsChainSettings();
                        report.AppendLine("Chain: " + (chain.m_Root ? chain.m_Root.name : "missing root") +
                        "; enabled=" + chain.enabled + "; weight=" + Number(chain.m_Weight) + "; mode=" + _environment.Mode(chain) +
                        "; breastRole=" + setting.BreastRole + "; breastRadiusScale=" + Number(setting.BreastRadiusScale) +
                        "; meshVolume=" + setting.BreastMeshVolume + "; radiusOverride=" + Number(setting.BreastRadiusOverride) +
                        "; capsule=" + setting.BreastCapsule + "; length=" + Number(setting.BreastLength) +
                        "; mirrorX=" + setting.BreastMirrorX + "; offset=" + Vector(new Vector3(setting.BreastOffsetX, setting.BreastOffsetY, setting.BreastOffsetZ)) +
                        "; rotation=" + Vector(new Vector3(setting.BreastRotationX, setting.BreastRotationY, setting.BreastRotationZ)));
                    }
            report.AppendLine("Recent events:");
            foreach (var line in _diagnosticEvents) report.AppendLine(line);
            report.AppendLine("Surface gaps are sampled sphere/capsule approximations. Changed-after-write also includes manual edits.");
            return report.ToString();
        }
    }
}
