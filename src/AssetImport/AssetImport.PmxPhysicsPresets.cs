using System;
using UnityEngine;

namespace AssetImport
{
    internal sealed class PmxPhysicsPreset
    {
        internal string Name { get; private set; }
        private readonly float _damping, _elasticity, _stiffness, _inertia;
        private readonly float? _dampingEnd;
        private readonly float _elasticityEnd, _stiffnessEnd;

        internal PmxPhysicsPreset(string name, float damping, float elasticity, float stiffness, float inertia,
            float? dampingEnd, float elasticityEnd, float stiffnessEnd)
        {
            Name = name;
            _damping = damping;
            _elasticity = elasticity;
            _stiffness = stiffness;
            _inertia = inertia;
            _dampingEnd = dampingEnd;
            _elasticityEnd = elasticityEnd;
            _stiffnessEnd = stiffnessEnd;
        }

        // Apply once to newly created components, before an optional editor
        // restores user settings. This is a demo-derived DynamicBone baseline,
        // not a conversion of PMX rigid-body coefficients or joint constraints.
        internal bool Apply(DynamicBone chain, int profile)
        {
            if (profile != 1 || !chain) return false;
            chain.m_UpdateRate = 60f;
            chain.m_Damping = _damping;
            chain.m_Elasticity = _elasticity;
            chain.m_Stiffness = _stiffness;
            chain.m_Inert = _inertia;
            chain.m_DampingDistrib = _dampingEnd.HasValue ? Distribution(_dampingEnd.Value) : null;
            chain.m_ElasticityDistrib = Distribution(_elasticityEnd);
            chain.m_StiffnessDistrib = Distribution(_stiffnessEnd);
            chain.m_InertDistrib = null;
            return true;
        }

        private static AnimationCurve Distribution(float end)
        {
            float slope = end - 1f;
            // DynamicBone samples normalized path length (0..1), not bone
            // indices. Explicit equal tangents keep this curve linear.
            return new AnimationCurve(new Keyframe(0f, 1f, slope, slope), new Keyframe(1f, end, slope, slope));
        }
    }

    internal static class PmxPhysicsPresets
    {
        // Sample-derived medians and simplified distributions from the supplied
        // native KK demo resources. Hair uses
        // the long-hair baseline: short chain length alone does not justify
        // applying a much stiffer short-hair preset to an unknown PMX rig.
        private static readonly PmxPhysicsPreset Hair = new PmxPhysicsPreset("hair", .18f, .07f, .10f, .50f, null, .38f, .203f);
        private static readonly PmxPhysicsPreset Skirt = new PmxPhysicsPreset("skirt", .36f, .15f, .84f, .343f, .507f, .303f, .25f);
        private static readonly PmxPhysicsPreset Accessory = new PmxPhysicsPreset("accessory", .40f, .30f, .10f, .50f, .507f, .303f, .073f);

        internal static PmxPhysicsPreset For(PmxModel model, int root, PmxPhysicsRootSuggestions suggestions)
        {
            if (model == null || !ValidBone(root, model.Bones)) return null;
            if (suggestions == null) suggestions = PmxPhysicsRootSuggestions.Build(model);
            foreach (int hairRoot in suggestions.HairRoots)
                if (Below(root, hairRoot, model.Bones)) return Hair;
            for (int i = 0; i < model.Bones.Length; i++)
                if (Below(i, root, model.Bones) && SkirtName(model.Bones[i].Name)) return Skirt;
            foreach (var body in model.RigidBodyData ?? new PmxModel.RigidBody[0])
                if (body != null && (body.Mode == 1 || body.Mode == 2) &&
                    Below(body.Bone, root, model.Bones) && SkirtName(body.Name)) return Skirt;
            return Accessory;
        }

        private static bool SkirtName(string name)
        {
            string text = name ?? "";
            if (text.IndexOf("skirt", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("スカート", StringComparison.Ordinal) >= 0 ||
                text.IndexOf("裙", StringComparison.Ordinal) >= 0 ||
                text.IndexOf("裾", StringComparison.Ordinal) >= 0) return true;
            // A whole word accepts dress_01/dress1 but avoids headdress and
            // generic DressBone names, whose location/function is ambiguous.
            for (int at = text.IndexOf("dress", StringComparison.OrdinalIgnoreCase); at >= 0;
                at = text.IndexOf("dress", at + 1, StringComparison.OrdinalIgnoreCase))
            {
                int end = at + 5;
                if ((at == 0 || !char.IsLetter(text[at - 1])) && (end == text.Length || !char.IsLetter(text[end])))
                    return true;
            }
            return false;
        }

        private static bool ValidBone(int index, PmxModel.Bone[] bones)
        {
            return bones != null && index >= 0 && index < bones.Length && bones[index] != null;
        }

        private static bool Below(int index, int ancestor, PmxModel.Bone[] bones)
        {
            for (int steps = 0; ValidBone(index, bones) && steps < bones.Length; steps++, index = bones[index].Parent)
                if (index == ancestor) return true;
            return false;
        }
    }
}
