using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace AssetImport
{
    // Only explicitly owned chains enter this registry. Serialized Unity
    // references remap when an accessory is cloned; settings are value data.
    internal sealed class AssetPhysicsEnvironment : MonoBehaviour
    {
        public Transform ImportedRoot;
        public DynamicBone[] Chains = new DynamicBone[0];
        public PhysicsEnvironmentSettings Settings = new PhysicsEnvironmentSettings();
        public PhysicsJointBinding[] JointBindings = new PhysicsJointBinding[0];
        private readonly Dictionary<DynamicBone, PhysicsJointSolver> _joints = new Dictionary<DynamicBone, PhysicsJointSolver>();
        private readonly Dictionary<DynamicBone, PhysicsChainSettings> _preferences = new Dictionary<DynamicBone, PhysicsChainSettings>();
        private static readonly Dictionary<DynamicBone, AssetPhysicsEnvironment> Owners = new Dictionary<DynamicBone, AssetPhysicsEnvironment>();
        private readonly List<DynamicBone> _registered = new List<DynamicBone>();
        private readonly List<SettleWork> _settling = new List<SettleWork>();
        private int _cursor;
        private readonly Stopwatch _budget = new Stopwatch();
        internal bool IsSettling { get { return _settling.Count != 0; } }
        internal const int DrapeSettleSteps = 900;
        internal static event Action<DynamicBone> ResetRequested;

        private sealed class SettleWork
        {
            internal DynamicBone Chain;
            internal Vector3 RootPosition;
            internal Quaternion ParentRotation;
            internal int Remaining, Stable;
        }

        internal static void Attach(GameObject host, Transform root, DynamicBone[] chains, PhysicsEnvironmentSettings settings, PhysicsJointBinding[] joints = null)
        {
            var environment = host.GetComponent<AssetPhysicsEnvironment>() ?? host.AddComponent<AssetPhysicsEnvironment>();
            if (joints != null) environment.JointBindings = joints;
            else if (environment.ImportedRoot != root) environment.JointBindings = new PhysicsJointBinding[0];
            environment.Configure(root, chains, settings);
        }

        internal void Configure(Transform root, DynamicBone[] chains, PhysicsEnvironmentSettings settings)
        {
            Unregister();
            ImportedRoot = root; Chains = chains; Settings = settings.Copy();
            Register();
            if (Settings.Settle) RequestSettle();
        }

        internal void Apply(PhysicsEnvironmentSettings settings)
        {
            Settings = settings.Copy();
            RebuildPreferences();
            foreach (var chain in _registered)
                if (chain && Mode(chain) == PhysicsChainMode.Animation)
                { chain.InitTransforms(); chain.ResetParticlesPosition(); }
            _settling.Clear();
            if (Settings.Settle) RequestSettle();
        }

        private void OnEnable()
        {
            Settings = (Settings ?? new PhysicsEnvironmentSettings()).Copy();
            Register();
            if (Settings.Settle) RequestSettle();
        }

        private void OnDisable() { Unregister(); }
        private void OnDestroy() { Unregister(); }

        private bool Owns(DynamicBone chain)
        {
            return chain && ImportedRoot && ImportedRoot.IsChildOf(transform) && chain.gameObject == gameObject &&
                chain.m_Root && chain.m_Root.IsChildOf(ImportedRoot);
        }

        private void Register()
        {
            foreach (var chain in Chains ?? new DynamicBone[0])
                if (Owns(chain) && !_registered.Contains(chain)) { Owners[chain] = this; _registered.Add(chain); }
            foreach (var binding in JointBindings ?? new PhysicsJointBinding[0])
                if (binding != null && binding.Chain && _registered.Contains(binding.Chain)) _joints[binding.Chain] = new PhysicsJointSolver(binding);
            RebuildPreferences();
        }

        private void RebuildPreferences()
        {
            _preferences.Clear();
            foreach (var chain in _registered)
                if (chain && chain.m_Root)
                { var settings = Settings.ForRoot(chain.m_Root.name); if (settings != null) _preferences[chain] = settings; }
        }

        internal PhysicsChainMode Mode(DynamicBone chain)
        {
            PhysicsChainSettings settings;
            if (_preferences.TryGetValue(chain, out settings) && settings.Mode != 0) return (PhysicsChainMode)settings.Mode;
            return (PhysicsChainMode)Settings.DefaultMode;
        }

        internal int JointCount(DynamicBone chain)
        { PhysicsJointSolver solver; return _joints.TryGetValue(chain,out solver) ? solver.Count : 0; }

        private void Unregister()
        {
            foreach (var chain in _registered)
            {
                AssetPhysicsEnvironment owner;
                if (Owners.TryGetValue(chain, out owner) && owner == this) Owners.Remove(chain);
            }
            _registered.Clear(); _settling.Clear();
            _joints.Clear(); _preferences.Clear();
        }

        private static AssetPhysicsEnvironment Find(DynamicBone chain)
        {
            AssetPhysicsEnvironment owner;
            if (!chain || !Owners.TryGetValue(chain, out owner)) return null;
            if (!owner || !owner.isActiveAndEnabled || !owner.Owns(chain)) { Owners.Remove(chain); return null; }
            return chain.enabled && chain.m_Weight > 0f && !chain.m_DistantDisabled &&
                chain.gameObject.activeInHierarchy && chain.m_Root.gameObject.activeInHierarchy ? owner : null;
        }

        private float Scale
        {
            get
            {
                float scale = ImportedRoot ? Math.Abs(ImportedRoot.lossyScale.x) : 0f;
                return PhysicsEnvironmentMath.Finite(scale) ? scale : 0f;
            }
        }

        internal static bool UsesNaturalDrape(DynamicBone chain)
        {
            if (!NaturalDrapePatch.Available) return false;
            var owner = Find(chain);
            return owner && owner.Mode(chain) == PhysicsChainMode.FreeHanging;
        }

        internal static float ShapeRetention(DynamicBone chain)
        {
            if (!NaturalDrapePatch.Available) return 1f;
            var owner = Find(chain); if (!owner) return 1f;
            if (owner.Mode(chain) == PhysicsChainMode.FreeHanging) return 0f;
            PhysicsChainSettings settings;
            return owner._preferences.TryGetValue(chain, out settings) ? settings.ShapeRetention : 1f;
        }

        internal static bool ShouldSimulate(DynamicBone chain)
        { var owner = Find(chain); return !owner || owner.Mode(chain) != PhysicsChainMode.Animation; }

        internal static void AfterIntegrate(DynamicBone chain)
        {
            var owner = Find(chain);
            if (!owner || owner.Mode(chain) == PhysicsChainMode.Animation || owner.Settings.DownwardForce <= 0f || chain.m_Particles == null) return;
            float displacement = owner.Settings.DownwardForce * owner.Scale;
            PhysicsChainSettings settings;
            if (owner._preferences.TryGetValue(chain, out settings)) displacement *= settings.ForceScale;
            // Verlet external acceleration is an additive displacement in this
            // solver. Add it before constraints without writing m_Force or
            // m_Gravity, so editor and interaction-plugin values stay intact.
            foreach (var p in chain.m_Particles)
                if (p.m_ParentIndex >= 0) p.m_Position += new Vector3(0f, -displacement, 0f);
        }

        internal static void AfterConstraints(DynamicBone chain, bool integrated = true)
        {
            var owner = Find(chain);
            if (!owner || owner.Mode(chain) == PhysicsChainMode.Animation || chain.m_Particles == null) return;
            PhysicsJointSolver solver;
            if (!UsesNaturalDrape(chain) && owner._joints.TryGetValue(chain, out solver))
                solver.Apply(chain, owner.ImportedRoot, integrated);
            ApplyFloor(chain, owner);
        }

        private static void ApplyFloor(DynamicBone chain, AssetPhysicsEnvironment owner)
        {
            if (!owner.Settings.Floor) return;
            float scale = owner.Scale;
            float floor = owner.ImportedRoot.position.y + owner.Settings.FloorOffset * scale;
            float particleScale = Math.Abs(chain.transform.lossyScale.x);
            if (scale <= 0f || !PhysicsEnvironmentMath.Finite(floor) || !PhysicsEnvironmentMath.Finite(particleScale)) return;
            var particles = chain.m_Particles;
            for (int i = 1; i < particles.Count; i++)
            {
                var p = particles[i];
                if (p.m_ParentIndex < 0 || p.m_ParentIndex >= i) continue;
                var parent = particles[p.m_ParentIndex];
                if (!parent.m_Transform) continue;
                float height = floor + Math.Max(0f, p.m_Radius) * particleScale;
                Vector3 rest = p.m_Transform ? p.m_Transform.position - parent.m_Transform.position : parent.m_Transform.TransformVector(p.m_EndOffset);
                // A preceding contact may have moved this particle's parent.
                // Restore the link length before constraining the child too.
                var direction = p.m_Position - parent.m_Position;
                float length = rest.magnitude;
                if (direction.sqrMagnitude > 1e-20f) p.m_Position = parent.m_Position + direction.normalized * length;
                else if (length > 0f) p.m_Position = parent.m_Position + rest;
                if (p.m_Position.y >= height) continue;
                var velocity = p.m_Position - p.m_PrevPosition;
                Vector3 fallback = rest;
                if (rest.x * rest.x + rest.z * rest.z < 1e-12f * length * length)
                    fallback = parent.m_Position - owner.ImportedRoot.position;
                p.m_Position = PhysicsEnvironmentMath.Contact(p.m_Position, parent.m_Position, fallback, length, height);
                // Contact correction is not an impulse. Preserve tangential
                // velocity without launching a particle sideways on contact.
                if (p.m_Position.y >= height - .000001f * scale) velocity.y = Math.Max(0f, velocity.y);
                p.m_PrevPosition = p.m_Position - velocity;
            }
        }

        internal static void AfterReset(DynamicBone chain)
        {
            if (ResetRequested != null) ResetRequested(chain);
            var owner = Find(chain);
            if (owner && owner.Settings.Settle) owner.Queue(chain);
        }

        private void Queue(DynamicBone chain)
        {
            if (Mode(chain) == PhysicsChainMode.Animation) return;
            int steps = UsesNaturalDrape(chain) ? DrapeSettleSteps : 90;
            foreach (var work in _settling) if (work.Chain == chain)
            {
                work.Remaining = steps; work.Stable = 0; work.RootPosition = chain.m_Root.position;
                work.ParentRotation = chain.m_Root.parent ? chain.m_Root.parent.rotation : Quaternion.identity;
                return;
            }
            _settling.Add(new SettleWork { Chain = chain, Remaining = steps, RootPosition = chain.m_Root.position,
                ParentRotation = chain.m_Root.parent ? chain.m_Root.parent.rotation : Quaternion.identity });
        }

        internal int RequestSettle()
        {
            _settling.Clear(); _cursor = 0;
            foreach (var chain in Chains ?? new DynamicBone[0]) if (Find(chain) == this) Queue(chain);
            return _settling.Count;
        }

        private void LateUpdate()
        {
            _budget.Reset(); _budget.Start();
            int count = 0;
            // Cooperative work with both a time and a substep limit. No Unity
            // objects are touched off-thread, and animation time never advances.
            while (_settling.Count > 0 && count++ < 128 && _budget.Elapsed.TotalMilliseconds < 2)
            {
                if (_cursor >= _settling.Count) _cursor = 0;
                var work = _settling[_cursor]; var chain = work.Chain;
                if (Find(chain) != this || --work.Remaining <= 0) { _settling.RemoveAt(_cursor); continue; }
                if (chain.m_Particles == null || chain.m_Particles.Count < 2) { _cursor++; break; }
                Quaternion parentRotation = chain.m_Root.parent ? chain.m_Root.parent.rotation : Quaternion.identity;
                if ((chain.m_Root.position - work.RootPosition).sqrMagnitude > .000001f * Scale * Scale ||
                    Quaternion.Angle(parentRotation, work.ParentRotation) > .1f)
                { _settling.RemoveAt(_cursor); continue; }
                float rate = chain.m_UpdateRate;
                if (!PhysicsEnvironmentMath.Finite(rate) || rate <= 0f) rate = 60f;
                try
                {
                    chain.InitTransforms();
                    chain.UpdateDynamicBones(1f / Math.Max(1f, Math.Min(240f, rate)));
                    float motion = 0f;
                    foreach (var p in chain.m_Particles) motion = Math.Max(motion, (p.m_Position - p.m_PrevPosition).sqrMagnitude);
                    work.Stable = motion < 1e-10f * Scale * Scale ? work.Stable + 1 : 0;
                    if (work.Stable >= 5) { _settling.RemoveAt(_cursor); continue; }
                }
                catch (Exception error)
                {
                    AssetImport.Logger.LogWarning("Could not settle physics for " + chain.m_Root.name + ": " + error.Message);
                    _settling.RemoveAt(_cursor); continue;
                }
                _cursor++;
            }
            _budget.Stop();
        }
    }
}
