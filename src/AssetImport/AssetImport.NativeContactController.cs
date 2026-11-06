using System;
using System.Collections.Generic;
using UnityEngine;

namespace AssetImport
{
    [DefaultExecutionOrder(-32000)]
    internal sealed partial class NativeContactController : MonoBehaviour
    {
        public NativeContactDefinition Definition;
        [SerializeField] private NativeContactShape[] _resolvedBreasts;
        private AssetPhysicsEnvironment _environment;
        private NativeContactEngine[] _engines;
        private List<DynamicBoneCollider> _obstacles;
        private List<NativeContactShape> _obstacleShapes;
        private int[] _pairCounts;
        private string _currentProbe;
        // Unity cloning remaps these references. A cloned controller restores
        // the input pose and removes cloned helpers before making its own rig.
        [SerializeField] private List<GameObject> _objects = new List<GameObject>();
        [SerializeField] private Vector3[] _baseLocal, _writtenLocal;
        [SerializeField] private bool _written;
        private Vector3[] _baseWorld, _displacements, _inputCenters, _outputCenters;
        [SerializeField] private NativeContactLateUpdate _lateUpdater;
        private bool _failed, _reported;
        private int _frame = -1;
        private float _scale;
        internal LocalizedText Status { get; private set; } = L.Message("Breast contacts: Off");
        internal int EligibleCount { get { return Definition == null ? 0 : ContactShapes.Length; } }
        internal NativeContactShape[] ContactShapes
        {
            get
            {
                if (_resolvedBreasts == null)
                {
                    if (!_environment) _environment = GetComponent<AssetPhysicsEnvironment>();
                    _resolvedBreasts = NativeContactSelection.Resolve(Definition, _environment ? _environment.Chains : null,
                        _environment ? _environment.Settings : null);
                }
                return _resolvedBreasts;
            }
        }
        internal NativeContactShape[] Preview(PhysicsEnvironmentSettings settings)
        {
            if (!_environment) _environment = GetComponent<AssetPhysicsEnvironment>();
            return NativeContactSelection.Resolve(Definition, _environment ? _environment.Chains : null, settings);
        }

        internal static void Attach(GameObject host, NativeContactDefinition definition)
        {
            if (definition == null) return;
            var controller = host.GetComponent<NativeContactController>() ?? host.AddComponent<NativeContactController>();
            controller.ResetContacts();
            controller.Definition = definition;
            controller._environment = host.GetComponent<AssetPhysicsEnvironment>();
            controller.Status = L.Message("Breast contacts: Off ({0} eligible)", controller.EligibleCount);
            if (AssetDebug.Enabled) AssetImport.Logger.LogInfo("PMX native breast candidates: model=" + definition.Model.name + "; breasts=" +
                definition.Breasts.Length + "; bodyObstacles=" + definition.Obstacles.Length + ". Disabled until explicitly enabled.");
            controller.DiagnosticEvent("attached; " + controller.DiagnosticSummary.English.Replace('\n', ';'));
            if (AssetDebug.Enabled) AssetImport.Logger.LogInfo("PMX breast recognition: model=" + definition.Model.name + "\n" + definition.RecognitionReport);
        }
        private void OnEnable()
        {
            ResetContacts();
            _lateUpdater = GetComponent<NativeContactLateUpdate>() ?? gameObject.AddComponent<NativeContactLateUpdate>();
            _lateUpdater.Controller = this; _lateUpdater.enabled = true;
            SyncDiagnostics(); AssetPhysicsEnvironment.ResetRequested += OnPhysicsReset;
        }
        private void OnDisable()
        {
            StopDiagnosticObservation(); AssetPhysicsEnvironment.ResetRequested -= OnPhysicsReset;
            if (_lateUpdater) _lateUpdater.enabled = false;
            ResetContacts();
        }
        private void OnDestroy() { ResetContacts(); }
        private void OnPhysicsReset(DynamicBone chain) { if (chain && chain.gameObject == gameObject) ResetContacts(); }
        // Restore before animation/ordinary physics, then a separate, late
        // component writes the result before Unity updates skinned meshes.
        private void Update() { Restore(); SyncDiagnostics(); }
        private void LateUpdate()
        { if (!_lateUpdater || !_lateUpdater.isActiveAndEnabled) DiagnosticHeartbeat(); }
        internal void ApplyLateUpdate()
        {
            if (_frame == Time.frameCount || !isActiveAndEnabled) return;
            _frame = Time.frameCount;
            _lateUpdates++;
            Tick(Time.deltaTime);
            DiagnosticHeartbeat();
        }

        internal void ResetContacts()
        {
            Restore(); Release(); _failed = false; _reported = false; _frame = -1;
            _resolvedBreasts = null;
            _resets++; _lastDisplacement = 0; _diagnosticState = "awaiting-update";
            _nextGeometrySample = 0;
            _nextDiagnostic = Time.realtimeSinceStartup + 5f;
        }

        private bool EnabledFor(NativeContactShape shape)
        {
            var owner = NativeContactSelection.Owner(shape.Bone, _environment.Chains);
            if (owner) return owner.enabled && owner.m_Weight > 0 && _environment.Mode(owner) != PhysicsChainMode.Animation;
            bool any = false;
            foreach (var chain in _environment.Chains)
            {
                if (!chain || !chain.m_Root) continue;
                bool enabled = chain.enabled && chain.m_Weight > 0 && _environment.Mode(chain) != PhysicsChainMode.Animation;
                any |= enabled;
            }
            return any;
        }

        internal void Tick(float elapsed)
        {
            SyncDiagnostics();
            _ticks++; _lastElapsed = elapsed;
            Restore();
            if (!_environment) _environment = GetComponent<AssetPhysicsEnvironment>();
            if (!_environment) { Blocked("missing-environment", "Breast contacts: physics component missing"); return; }
            if (!_environment.isActiveAndEnabled) { Blocked("environment-disabled", "Breast contacts: physics component disabled"); return; }
            if (Definition == null || !Definition.Model) { Blocked("missing-definition", "Breast contacts: model data missing"); return; }
            if (!Definition.Model.IsChildOf(transform)) { Blocked("model-owner-mismatch", "Breast contacts: model ownership changed"); return; }
            if (!Definition.Model.gameObject.activeInHierarchy) { Blocked("model-inactive", "Breast contacts: model inactive"); return; }
            if (!_environment.Settings.NativeBreastContacts)
            { Release(); _failed = false; _reported = false; Status = L.Message("Breast contacts: Off ({0} eligible)", EligibleCount); DiagnosticState("off"); return; }
            if (_failed) return;
            try
            {
                float scale = UniformScale(Definition.Model);
                UniformScale(transform);
                if (EligibleCount == 0 || Definition.Obstacles.Length == 0)
                {
                    Status = L.Message("Breast contacts: no usable PMX bodies");
                    DiagnosticState(EligibleCount == 0 ? "no-breast-bodies" : "no-body-obstacles");
                    if (!_reported) AssetImport.Logger.LogWarning("PMX native breast unavailable: no matching weighted breast body or stable PMX body obstacles.");
                    _reported = true; return;
                }
                bool enabled = false;
                foreach (var breast in ContactShapes) if (breast.Bone && EnabledFor(breast)) enabled = true;
                if (!enabled) { Blocked("physics-off", "Breast contacts: physics is off"); return; }
                if (_engines != null && Math.Abs(scale - _scale) > .00001f * scale) Release();
                if (_engines == null) Initialize(scale);
                // Capture every input before writing any parent or child bone.
                int active = 0; float maximum = 0;
                for (int i = 0; i < EligibleCount; i++)
                {
                    var shape = ContactShapes[i];
                    if (!shape.Bone || !shape.Bone.IsChildOf(Definition.Model)) throw new InvalidOperationException("Breast bone is no longer owned by the model.");
                    _baseLocal[i] = shape.Bone.localPosition; _baseWorld[i] = shape.Bone.position;
                    _inputCenters[i] = shape.Bone.TransformPoint(shape.Center);
                    _displacements[i] = Vector3.zero;
                    if (!EnabledFor(shape) || _engines[i] == null) continue;
                    _currentProbe = shape.Bone.name;
                    _displacements[i] = _engines[i].Step(_inputCenters[i], shape.Bone.rotation, elapsed);
                    if (elapsed > 0) _solverSteps++;
                    maximum = Math.Max(maximum, _displacements[i].magnitude); active++;
                }
                if (active == 0)
                { Status = L.Message("Breast contacts: no independent contact pairs"); DiagnosticState("no-contact-pairs"); return; }
                // Import binding is sorted by ancestry. Real bone rotations and
                // scales remain the original animation/ordinary physics output.
                CaptureGrantInputs();
                _written = true;
                for (int i = 0; i < EligibleCount; i++)
                {
                    // A probe with no response must inherit its parent's
                    // movement instead of undoing it at every child bone.
                    if (_displacements[i].sqrMagnitude > 1e-12f * scale * scale)
                        ContactShapes[i].Bone.position = _baseWorld[i] + _displacements[i];
                    _writtenLocal[i] = ContactShapes[i].Bone.localPosition;
                }
                ApplyContactGrants();
                for (int i = 0; i < EligibleCount; i++)
                {
                    _writtenLocal[i] = ContactShapes[i].Bone.localPosition;
                    _outputCenters[i] = ContactShapes[i].Bone.TransformPoint(ContactShapes[i].Center);
                }
                RecordWriteback();
                Status = L.Message("Breast contacts: {0} active / {1}", active, EligibleCount);
                _poseWrites++; _lastDisplacement = maximum; _maximumDisplacement = Math.Max(_maximumDisplacement, maximum);
                DiagnosticState(elapsed <= 0 ? "zero-delta-time" : "running");
                if (!_reported && maximum > .0001f * scale)
                {
                    _reported = true;
                    if (AssetDebug.Enabled) AssetImport.Logger.LogInfo("PMX native breast response: active=" + active + "; displacement=" + maximum +
                        "; obstacles=" + _obstacles.Count + ". Native response only; visible contact requires in-game verification.");
                }
            }
            catch (Exception error)
            {
                var detail = error.InnerException ?? error;
                _lastFailure = "probe=" + _currentProbe + "\n" + detail;
                // Preserve native counters before disposing a failed rig; the
                // next automatic report must still explain how it stopped.
                if (_engines != null)
                    for (int i = 0; i < _engines.Length; i++)
                        if (_engines[i] != null) _lastFailure += "\nfailure snapshot; " + ProbeState(i);
                Restore(); Release(); _failed = true; _lastDisplacement = 0;
                Status = L.Message("Breast contacts unavailable; see log");
                DiagnosticState("failed");
                AssetImport.Logger.LogWarning("PMX native breast adapter stopped; ordinary physics remains active. " + detail);
            }
        }

        private void Initialize(float scale)
        {
            _scale = scale;
            _obstacles = new List<DynamicBoneCollider>();
            _obstacleShapes = new List<NativeContactShape>();
            foreach (var shape in Definition.Obstacles)
            {
                if (!shape.Bone || !shape.Bone.IsChildOf(Definition.Model)) continue;
                var obj = new GameObject("AI_BreastContactObstacle"); _objects.Add(obj);
                obj.transform.SetParent(shape.Bone, false);
                obj.transform.localPosition = shape.Center; obj.transform.localRotation = shape.Rotation;
                var collider = obj.AddComponent<DynamicBoneCollider>();
                collider.m_Radius = shape.Radius; collider.m_Height = shape.Height;
                collider.m_Direction = (DynamicBoneCollider.Direction)shape.Direction;
                collider.m_Bound = DynamicBoneCollider.Bound.Outside;
                _obstacles.Add(collider);
                _obstacleShapes.Add(shape);
            }
            if (_obstacles.Count == 0) throw new InvalidOperationException("No owned body obstacles remain.");
            int count = EligibleCount;
            _engines = new NativeContactEngine[count];
            _pairCounts = new int[count];
            _baseLocal = new Vector3[count]; _baseWorld = new Vector3[count];
            _writtenLocal = new Vector3[count]; _displacements = new Vector3[count];
            _inputCenters = new Vector3[count]; _outputCenters = new Vector3[count];
            _lastWriteFrame = -1;
            BindGrantPoses();
            for (int i = 0; i < count; i++)
            {
                var shape = ContactShapes[i];
                if (!shape.Bone || !shape.Bone.IsChildOf(Definition.Model)) throw new InvalidOperationException("Invalid breast owner.");
                float radius = shape.Radius * UniformScale(shape.Bone);
                if (!PhysicsEnvironmentMath.Finite(radius) || radius <= .000001f) throw new InvalidOperationException("Invalid breast radius.");
                _currentProbe = shape.Bone.name;
                var pairs = new List<DynamicBoneCollider>();
                for (int j = 0; j < _obstacles.Count; j++)
                {
                    string excluded = NativeContactPairPolicy.Exclusion(shape, _obstacleShapes[j]);
                    if (excluded == null) pairs.Add(_obstacles[j]);
                    else DiagnosticEvent("pair excluded: breast=" + shape.Bone.name + "; obstacle=" + _obstacleShapes[j].SourceBody + "; reason=" + excluded);
                }
                _pairCounts[i] = pairs.Count;
                DiagnosticEvent("initializing bone=" + shape.Bone.name + "; radius=" + Number(radius) + "; obstacles=" + pairs.Count + "/" + _obstacles.Count);
                if (pairs.Count == 0) continue;
                _engines[i] = new NativeContactEngine(transform, shape.Bone.TransformPoint(shape.Center), shape.Bone.rotation, radius, pairs,
                    message => DiagnosticEvent("bone=" + shape.Bone.name + "; " + message),
                    NativeContactGeometry.ContactHalfAxis(shape) * UniformScale(shape.Bone));
                _objects.Add(_engines[i].Host);
                if (_engines[i].CapsuleObstacles) _objects.Add(_engines[i].CapsuleObstacles);
                if (AssetDebug.Enabled) AssetImport.Logger.LogInfo("PMX native breast initialized: bone=" + shape.Bone.name + "; radius=" + radius +
                    "; obstacles=" + pairs.Count + "; nativeParticles=" + _engines[i].ParticleCount +
                    "; sphereSelfCheck=passed; engine=" + typeof(DynamicBone_Ver02).Assembly.GetName().Version + ".");
            }
        }

        internal void Restore()
        {
            if (!_written || Definition == null) return;
            RestoreContactGrants();
            for (int i = 0; i < ContactShapes.Length; i++)
            {
                var bone = ContactShapes[i].Bone;
                if (bone && (bone.localPosition - _writtenLocal[i]).sqrMagnitude < 1e-16f) bone.localPosition = _baseLocal[i];
                else if (bone) _changedAfterWrite++;
            }
            _written = false;
        }
        private void Release()
        {
            if (_engines != null) foreach (var engine in _engines) if (engine != null) engine.Dispose();
            _engines = null;
            foreach (var obj in _objects) if (obj && obj.transform.IsChildOf(transform)) UnityEngine.Object.Destroy(obj);
            _objects.Clear(); _obstacles = null; _obstacleShapes = null;
            _grantPoses = null;
        }
        private static float UniformScale(Transform bone)
        {
            var s = bone.lossyScale;
            if (!PhysicsEnvironmentMath.Finite(s.sqrMagnitude) || s.x <= .000001f || s.y <= 0 || s.z <= 0 ||
                Math.Abs(s.x - s.y) > s.x * .001f || Math.Abs(s.x - s.z) > s.x * .001f)
                throw new InvalidOperationException("Breast contacts require positive uniform scale.");
            return s.x;
        }
    }
}
