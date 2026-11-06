using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace AssetImport
{
    // Uses the game's component on a private proxy. No native character, plugin
    // controller, original bone, or ordinary DynamicBone is reconfigured.
    internal sealed class NativeContactEngine : IDisposable
    {
        private readonly GameObject _host;
        private readonly Transform _anchor, _body, _anchorReference, _bodyReference;
        private readonly DynamicBone_Ver02 _engine;
        private readonly MethodInfo _step, _restore, _apply, _reset;
        private readonly object[] _timeArgument = new object[1];
        private readonly float _radius;
        private readonly Vector3 _capsuleHalfAxis;
        private NativeContactCapsule _capsule;
        private Vector3 _lastOutput;
        internal GameObject CapsuleObstacles { get { return _capsule == null ? null : _capsule.Host; } }
        private Vector3 _previousInput;
        private bool _started;
        private readonly Action<string> _diagnostic;
        internal int ParticleCount { get { return _engine.Particles.Count; } }
        internal int ColliderCount { get { return _engine.Colliders == null ? 0 : _engine.Colliders.Count; } }
        internal GameObject Host { get { return _host; } }
        internal int OutputRebases { get; private set; }
        internal float LastRawDisplacement { get; private set; }
        internal float MaximumRawDisplacement { get; private set; }

        internal NativeContactEngine(Transform owner, Vector3 center, Quaternion rotation, float radius, List<DynamicBoneCollider> colliders,
            Action<string> diagnostic = null, Vector3 capsuleHalfAxis = default(Vector3))
        {
            _diagnostic = diagnostic;
            _radius = radius;
            _capsuleHalfAxis = capsuleHalfAxis;
            _host = new GameObject("AI_BreastContactProxy");
            // Keep the component inactive, including Awake/OnEnable/Start. Its
            // configured solver is invoked explicitly after all pose writers.
            _host.SetActive(false);
            try
            {
                _host.transform.SetParent(owner, false);
                _host.transform.position = center - rotation * new Vector3(0, 0, radius * 2f);
                _host.transform.rotation = rotation;
                // World-unit proxies are rebuilt when model scale changes.
                var parentScale = owner.lossyScale;
                _host.transform.localScale = new Vector3(1f / parentScale.x, 1f / parentScale.y, 1f / parentScale.z);
                _anchor = Child("anchor", _host.transform);
                _body = Child("contact", _anchor);
                _anchorReference = Child("anchorInput", _host.transform);
                _bodyReference = Child("contactInput", _anchorReference);
                _body.localPosition = _bodyReference.localPosition = new Vector3(0, 0, radius * 2f);
                _engine = _host.AddComponent<DynamicBone_Ver02>();
                _engine.enabled = false;
                _engine.Comment = "AssetImport contact probe";
                _engine.Root = _anchor;
                _engine.Bones = new List<Transform> { _anchor, _body };
                _engine.Colliders = colliders;
                _engine.Gravity = _engine.Force = Vector3.zero;
                _engine.UpdateRate = 60;
                _engine.ReflectSpeed = 1;
                _engine.HeavyLoopMaxCount = 4;
                _engine.Weight = 1;
                _engine.PtnNo = 0;
                var pattern = new DynamicBone_Ver02.BonePtn { Name = "contact", Gravity = Vector3.zero,
                    EndOffset = new Vector3(0, 0, radius), EndOffsetDamping = .35f, EndOffsetElasticity = .2f,
                    EndOffsetStiffness = .1f, EndOffsetInert = .5f,
                    Params = new List<DynamicBone_Ver02.BoneParameter> {
                        Parameter(_anchor, _anchorReference, 0), Parameter(_body, _bodyReference, radius) },
                    ParticlePtns = new List<DynamicBone_Ver02.ParticlePtn>() };
                _engine.Patterns = new List<DynamicBone_Ver02.BonePtn> { pattern };
                _engine.Particles = new List<DynamicBone_Ver02.Particle>();
                _step = Method("UpdateDynamicBones", typeof(float));
                _restore = Method("InitTransforms"); _apply = Method("ApplyParticlesToTransforms");
                _reset = Method("ResetParticlesPosition");
                Call("InitNodeParticle");
                Call("InitLocalPosition");
                Report("stage=setPtn");
                var patternResult = Method("setPtn", typeof(int), typeof(bool)).Invoke(_engine, new object[] { 0, true });
                Report("setPtn.result=" + patternResult);
                Call("SetupParticles");
                bool movable = false;
                foreach (var particle in _engine.Particles)
                    if (particle != null && particle.Transform == _body && particle.ParentIndex >= 0 && particle.Radius > 0)
                        movable = true;
                if (!movable) throw new InvalidOperationException("Native solver did not create a movable contact particle; count=" + _engine.Particles.Count + ".");
                Report("stage=particles-ready; count=" + _engine.Particles.Count + "; movable=true");
                Follow(center, rotation);
                _reset.Invoke(_engine, null);
                ValidateResponse(center, rotation, colliders);
                if (_capsuleHalfAxis.sqrMagnitude > 1e-12f)
                {
                    _capsule = new NativeContactCapsule(owner, colliders);
                    _engine.Colliders = _capsule.Colliders;
                    ValidateCapsuleResponse(owner, center, rotation);
                }
                OutputRebases = 0; LastRawDisplacement = MaximumRawDisplacement = 0;
            }
            catch { Dispose(); throw; }
        }

        private static DynamicBone_Ver02.BoneParameter Parameter(Transform bone, Transform reference, float radius)
        {
            return new DynamicBone_Ver02.BoneParameter { Name = bone.name, RefTransform = reference,
                Damping = .35f, Elasticity = .2f, Stiffness = .1f, Inert = .5f, CollisionRadius = radius,
                IsRotationCalc = false, NextBoneLength = 1f, IsMoveLimit = false,
                KeepLengthLimitMin = 0f, KeepLengthLimitMax = 2f, IsCrush = false };
        }

        // Run inside the game at activation, before touching the real skeleton.
        // A movable particle alone does not prove native initialization worked.
        private void ValidateResponse(Vector3 center, Quaternion rotation, List<DynamicBoneCollider> obstacles)
        {
            GameObject testObject = null;
            try
            {
                _engine.Colliders = new List<DynamicBoneCollider>();
                Report("stage=self-check-rest");
                float drift = 0;
                for (int i = 0; i < 8; i++) drift = Math.Max(drift, Step(center, rotation, 1f / 60f).magnitude);
                Report("selfCheck.restDrift=" + NativeContactController.Number(drift));
                if (drift > _radius * .01f) throw new InvalidOperationException("Native contact self-check failed: unloaded proxy drifts by " + drift / _radius + " radii.");
                testObject = new GameObject("AI_ContactValidation");
                testObject.transform.position = center + rotation * new Vector3(_radius * .8f, 0, 0);
                var sphere = testObject.AddComponent<DynamicBoneCollider>();
                sphere.m_Radius = _radius * .5f; sphere.m_Height = 0;
                sphere.m_Bound = DynamicBoneCollider.Bound.Outside;
                _engine.Colliders.Add(sphere);
                Report("stage=self-check-sphere");
                float response = 0;
                for (int i = 0; i < 8; i++) response = Math.Max(response, Step(center, rotation, 1f / 60f).magnitude);
                Report("selfCheck.sphereDisplacement=" + NativeContactController.Number(response));
                if (response < _radius * .02f) throw new InvalidOperationException("Native contact self-check failed: the overlapping sphere produced no response.");
            }
            finally
            {
                if (testObject) { testObject.SetActive(false); UnityEngine.Object.Destroy(testObject); }
                _engine.Colliders = obstacles;
                _restore.Invoke(_engine, null); _reset.Invoke(_engine, null); _started = false;
            }
        }
        private static Transform Child(string name, Transform parent)
        { var result = new GameObject(name).transform; result.SetParent(parent, false); return result; }
        private static MethodInfo Method(string name, params Type[] args)
        {
            var method = typeof(DynamicBone_Ver02).GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, args, null);
            if (method == null) throw new MissingMethodException(typeof(DynamicBone_Ver02).Name, name);
            return method;
        }
        private void Report(string message) { if (_diagnostic != null) _diagnostic(message); }
        private void Call(string name) { Report("stage=" + name); Method(name).Invoke(_engine, null); }
        private void Follow(Vector3 center, Quaternion rotation)
        { _host.transform.rotation = rotation; _host.transform.position = center - rotation * new Vector3(0, 0, _radius * 2f); }

        internal Vector3 Step(Vector3 input, Quaternion rotation, float elapsed)
        {
            Follow(input, rotation);
            _restore.Invoke(_engine, null);
            if (!_started || elapsed > .2f || (input - _previousInput).sqrMagnitude > 64f * _radius * _radius)
            { _reset.Invoke(_engine, null); _lastOutput = Vector3.zero; }
            _started = true; _previousInput = input;
            if (_capsule != null) _capsule.Update(input + _lastOutput, rotation * _capsuleHalfAxis);
            if (elapsed > 0)
            {
                _timeArgument[0] = Math.Min(elapsed, 1f / 30f);
                _step.Invoke(_engine, _timeArgument);
                _apply.Invoke(_engine, null);
            }
            var displacement = _body.position - input;
            LastRawDisplacement = displacement.magnitude;
            if (!PhysicsEnvironmentMath.Finite(displacement.sqrMagnitude) || displacement.magnitude > _radius * 4f)
                throw new InvalidOperationException("Native solver returned an invalid or excessive displacement: raw=" +
                    NativeContactController.Number(LastRawDisplacement) + "; radius=" + NativeContactController.Number(_radius) +
                    "; hardLimit=" + NativeContactController.Number(_radius * 4f) + "; dt=" + NativeContactController.Number(elapsed) + ".");
            MaximumRawDisplacement = Math.Max(MaximumRawDisplacement, LastRawDisplacement);
            if (LastRawDisplacement > _radius)
            {
                displacement = displacement.normalized * _radius;
                // Keep the native particle state consistent with the bounded
                // output. Clamping only the real bone leaves a diverging proxy
                // hidden behind an apparently safe one-radius displacement.
                _body.position = input + displacement;
                _reset.Invoke(_engine, null);
                OutputRebases++;
            }
            _lastOutput = displacement;
            return displacement;
        }

        private void ValidateCapsuleResponse(Transform owner, Vector3 center, Quaternion rotation)
        {
            var original = _capsule; NativeContactCapsule test = null; GameObject testObject = null;
            try
            {
                testObject = new GameObject("AI_ContactValidation");
                var axis = _capsuleHalfAxis.normalized;
                var side = Math.Abs(axis.x) < .8f ? new Vector3(1, 0, 0) : new Vector3(0, 1, 0);
                float dot = axis.x * side.x + axis.y * side.y + axis.z * side.z;
                side = (side - axis * dot).normalized;
                testObject.transform.position = center + rotation * (_capsuleHalfAxis + side * (_radius * .8f));
                var sphere = testObject.AddComponent<DynamicBoneCollider>(); sphere.m_Radius = _radius * .5f;
                sphere.m_Height = 0; sphere.m_Bound = DynamicBoneCollider.Bound.Outside;
                test = new NativeContactCapsule(owner, new List<DynamicBoneCollider> { sphere });
                _capsule = test; _engine.Colliders = test.Colliders;
                _restore.Invoke(_engine, null); _reset.Invoke(_engine, null); _started = false;
                float response = 0;
                for (int i = 0; i < 8; i++) response = Math.Max(response, Step(center, rotation, 1f / 60f).magnitude);
                Report("selfCheck.capsuleEndDisplacement=" + NativeContactController.Number(response));
                if (response < _radius * .02f) throw new InvalidOperationException("Native capsule end self-check produced no response.");
            }
            finally
            {
                if (test != null) test.Dispose();
                if (testObject) { testObject.SetActive(false); UnityEngine.Object.Destroy(testObject); }
                _capsule = original; _engine.Colliders = original.Colliders;
                _restore.Invoke(_engine, null); _reset.Invoke(_engine, null); _started = false; _lastOutput = Vector3.zero;
            }
        }

        public void Dispose() { if (_capsule != null) _capsule.Dispose(); if (_host) UnityEngine.Object.Destroy(_host); }
    }
}
