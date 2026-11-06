using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AssetImport
{
    public partial class Import
    {
        private readonly List<DynamicBoneCollider> _pmxOwnedColliders = new List<DynamicBoneCollider>();
        private readonly List<DynamicBone> _pmxCollisionChains = new List<DynamicBone>();

        // Called after physics roots are restored/selected, before enabling their
        // DynamicBone components. Generated nodes are never added to BoneNodes:
        // saved DynamicBoneIndices and MMDD targets must keep their old identity.
        internal void ApplyPmxBodyCollisions(IList<DynamicBone> chains, Vector3 importScale, bool enabled, int physicsProfile = 0, bool hairCollisionAssist = false)
        {
            if (!IsPmx || !ImportBones || _pmx == null || chains.Count == 0) return;
            ClearPmxBodyCollisions();
            if (!enabled)
            {
                Logger.LogInfo("PMX body collisions disabled by configuration: " + SourceFileName);
                return;
            }
            try
            {
                var names = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int i = 0; i < _pmx.Bones.Length; i++) names.Add(PmxModel.BoneName(i, _pmx.Bones[i].Name), i);
                var bones = new Transform[_pmx.Bones.Length];
                foreach (var transform in GameObject.GetComponentsInChildren<Transform>(true))
                {
                    int index;
                    if (names.TryGetValue(transform.name, out index)) bones[index] = transform;
                }
                if (bones.Any(b => !b)) throw new FormatException("The imported PMX skeleton is incomplete.");
                var chainBones = new List<int[]>();
                foreach (var chain in chains)
                {
                    if (!chain || !chain.m_Root) throw new FormatException("Missing DynamicBone root.");
                    if (GameObject.transform == chain.m_Root || GameObject.transform.IsChildOf(chain.m_Root))
                        throw new FormatException("The whole model is a physics root. Select only individual hair/skirt roots.");
                    chainBones.Add(Enumerable.Range(0, bones.Length).Where(i => bones[i] == chain.m_Root || bones[i].IsChildOf(chain.m_Root)).ToArray());
                }
                var plan = PmxCollisionPlan.Build(_pmx, chainBones, hairCollisionAssist);
                foreach (var warning in plan.Warnings) Logger.LogWarning("PMX body collisions: " + warning);
                var colliders = new Dictionary<int, DynamicBoneCollider>();
                var used = new HashSet<int>(plan.Chains.SelectMany(c => c.ColliderIndices));
                foreach (int index in used.OrderBy(i => i))
                {
                    var body = plan.Colliders[index];
                    var obj = new GameObject("AI_PMX_Collider_" + body.Index.ToString("D4") + "_" + body.Name);
                    obj.transform.SetParent(body.Bone < 0 ? GameObject.transform : bones[body.Bone], false);
                    obj.transform.localPosition = new Vector3(body.LocalPosition.X, body.LocalPosition.Y, body.LocalPosition.Z);
                    obj.transform.localRotation = new Quaternion(body.Rotation[0], body.Rotation[1], body.Rotation[2], body.Rotation[3]);
                    obj.transform.localScale = Vector3.one;
                    var collider = obj.AddComponent<DynamicBoneCollider>();
                    _pmxOwnedColliders.Add(collider);
                    collider.m_Center = Vector3.zero;
                    collider.m_Radius = body.Radius;
                    collider.m_Height = body.Height;
                    collider.m_Direction = (DynamicBoneCollider.Direction)body.Direction;
                    collider.m_Bound = DynamicBoneCollider.Bound.Outside;
                    colliders.Add(index, collider);
                    if (AssetDebug.Enabled) Logger.LogInfo($"PMX collider: {obj.name}; bone={body.Bone}; radius={body.Radius}; height={body.Height}; boxApproximation={body.BoxApproximation}");
                }
                for (int i = 0; i < chains.Count; i++)
                {
                    var chain = chains[i]; var binding = plan.Chains[i];
                    _pmxCollisionChains.Add(chain);
                    if (chain.m_Colliders == null) chain.m_Colliders = new List<DynamicBoneCollider>();
                    foreach (int index in binding.ColliderIndices) chain.m_Colliders.Add(colliders[index]);
                    // DynamicBone scales particle radii by its host transform,
                    // while the colliders live under the imported model. The
                    // final model scale must be assigned before this method.
                    // Maker's N_move/addmove hierarchy can add another scale
                    // between those transforms, beyond the import scale alone.
                    if (binding.ParticleRadius > 0 && chain.m_Radius <= 0)
                    {
                        if (physicsProfile != 1)
                            chain.m_Radius = binding.ParticleRadius * Math.Abs(importScale.x);
                        else
                        {
                            float modelScale = Math.Abs(GameObject.transform.lossyScale.x);
                            float hostScale = Math.Abs(chain.transform.lossyScale.x);
                            float radius = binding.ParticleRadius * (modelScale / hostScale);
                            if (PmxCollisionFinitePositive(modelScale) && PmxCollisionFinitePositive(hostScale) && PmxCollisionFinitePositive(radius))
                                chain.m_Radius = radius;
                            else
                                Logger.LogWarning("PMX particle radius could not be converted for " + chain.m_Root.name +
                                    "; model/physics host scale must be finite and nonzero. Existing radius was preserved.");
                        }
                    }
                    if (AssetDebug.Enabled) Logger.LogInfo($"PMX collision binding: root={chain.m_Root.name}; bodyColliders={binding.ColliderIndices.Length}; particleRadius={chain.m_Radius}; metadata={binding.HasPhysicsMetadata}; mixedFilters={binding.MixedFilters}; hairAssistAdded={binding.AssistedColliderIndices.Length}");
                    if (AssetDebug.Enabled && binding.AssistedColliderIndices.Length != 0)
                        Logger.LogInfo("PMX hair collision assist: root=" + chain.m_Root.name + "; PMX group override added bodies=" +
                            string.Join(", ", binding.AssistedColliderIndices.Select(index => plan.Colliders[index].Index + ":" + plan.Colliders[index].Name).ToArray()));
                    if (!binding.HasPhysicsMetadata)
                        Logger.LogWarning("No PMX dynamic rigid-body metadata for " + chain.m_Root.name + "; no body collisions were guessed for this chain.");
                    else if (binding.ColliderIndices.Length == 0)
                        Logger.LogWarning("No permitted body colliders for " + chain.m_Root.name + ". PMX collision masks and chain ownership were preserved.");
                    if (binding.MixedFilters)
                        Logger.LogWarning("PMX chain uses mixed collision filters: " + chain.m_Root.name + ". Original bindings require every dynamic body to allow the obstacle; enable Debug to inspect hair-assist additions.");
                }
                var collisionScale = physicsProfile == 1 ? GameObject.transform.lossyScale : importScale;
                if (Math.Abs(Math.Abs(collisionScale.x) - Math.Abs(collisionScale.y)) > .0001f ||
                    Math.Abs(Math.Abs(collisionScale.x) - Math.Abs(collisionScale.z)) > .0001f)
                    Logger.LogWarning("PMX collision approximation expects uniform scale. Use the same X/Y/Z scaling on the model and its parent objects for collision tests.");
                int boxes = used.Count(i => plan.Colliders[i].BoxApproximation);
                Logger.LogInfo($"PMX body collisions ready: {SourceFileName}; {colliders.Count} colliders; {chains.Count} chains; {boxes} box approximations. PMX rigid-body dynamics/joints are not simulated.");
            }
            catch (Exception e)
            {
                ClearPmxBodyCollisions();
                Logger.LogWarning("PMX body collisions could not be created: " + e.Message);
            }
        }

        private static bool PmxCollisionFinitePositive(float value)
        {
            return value > 0 && !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private void ClearPmxBodyCollisions()
        {
            foreach (var chain in _pmxCollisionChains)
                if (chain && chain.m_Colliders != null) chain.m_Colliders.RemoveAll(c => _pmxOwnedColliders.Contains(c));
            foreach (var collider in _pmxOwnedColliders)
                if (collider) UnityEngine.Object.DestroyImmediate(collider.gameObject);
            _pmxOwnedColliders.Clear();
            _pmxCollisionChains.Clear();
        }
    }
}
