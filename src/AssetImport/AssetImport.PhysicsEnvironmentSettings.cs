using System;
using System.Collections.Generic;

namespace AssetImport
{
    [Serializable]
    internal sealed class PhysicsEnvironmentSettings
    {
        public bool Floor;
        public float FloorOffset;
        public float DownwardForce;
        public bool Settle;
        public bool NativeBreastContacts;
        public int DefaultMode = (int)PhysicsChainMode.KeepShape;
        public PhysicsChainSettings[] Chains = new PhysicsChainSettings[0];
        internal bool NaturalDrape
        {
            get { return DefaultMode == (int)PhysicsChainMode.FreeHanging; }
            set { DefaultMode = (int)(value ? PhysicsChainMode.FreeHanging : PhysicsChainMode.KeepShape); }
        }

        internal static PhysicsEnvironmentSettings NewImport()
        {
            return new PhysicsEnvironmentSettings { Floor = true, DownwardForce = .001f, Settle = true };
        }

        internal PhysicsEnvironmentSettings Copy()
        {
            var chains = new List<PhysicsChainSettings>(); var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var chain in Chains ?? new PhysicsChainSettings[0])
                if (chain != null && !string.IsNullOrEmpty(chain.Root) && seen.Add(chain.Root)) chains.Add(chain.Copy());
            return new PhysicsEnvironmentSettings { Floor = Floor, FloorOffset = Clamp(FloorOffset, -100f, 100f),
                DownwardForce = Clamp(DownwardForce, 0f, .02f), Settle = Settle, NativeBreastContacts = NativeBreastContacts,
                DefaultMode = DefaultMode >= 1 && DefaultMode <= 3 ? DefaultMode : (int)PhysicsChainMode.KeepShape,
                Chains = chains.ToArray() };
        }

        internal PhysicsChainSettings ForRoot(string root)
        {
            foreach (var chain in Chains ?? new PhysicsChainSettings[0]) if (chain != null && chain.Root == root) return chain;
            return null;
        }

        internal PhysicsChainSettings EditRoot(string root)
        {
            var found = ForRoot(root); if (found != null) return found;
            var list = new List<PhysicsChainSettings>(Chains ?? new PhysicsChainSettings[0]);
            found = new PhysicsChainSettings { Root = root }; list.Add(found); Chains = list.ToArray(); return found;
        }

        private static float Clamp(float value, float min, float max)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : Math.Max(min, Math.Min(max, value));
        }
    }
}
