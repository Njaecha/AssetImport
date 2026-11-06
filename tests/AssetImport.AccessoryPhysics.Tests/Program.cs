using System;
using System.Collections.Generic;
using System.Reflection;
using AssetImport;
using UnityEngine;

internal static class Program
{
    private static int checks;
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        checks++;
    }

    private static void Main()
    {
        var oldAsset = new Asset();
        Check(oldAsset.PhysicsEnabled == null && AssetAccessoryPhysics.RestoreEnabled(oldAsset), "Old cards with an absent switch remain ON");
        Check(AssetAccessoryPhysics.RestoreEnabled(null), "Absent legacy metadata remains ON");
        var field = typeof(Asset).GetProperty("PhysicsEnabled");
        var key = field.GetCustomAttribute<MessagePack.KeyAttribute>();
        Check(field.PropertyType == typeof(bool?) && key.Key == "PhysicsEnabled", "Optional named MessagePack field keeps false distinguishable from absence");
        var offAsset = new Asset { PhysicsEnabled = false };
        Check(!AssetAccessoryPhysics.RestoreEnabled(offAsset), "An explicit saved OFF restores OFF");

        var accessory = new GameObject();
        var root = new GameObject(accessory.transform);
        var ownA = new DynamicBone(accessory, new GameObject(root.transform).transform);
        var ownB = new DynamicBone(accessory, new GameObject(root.transform).transform);
        var otherPlugin = new DynamicBone(accessory, new GameObject(root.transform).transform);
        var otherAccessory = new GameObject();
        var otherCharacterBone = new DynamicBone(otherAccessory, new GameObject(otherAccessory.transform).transform);
        var staleBone = new DynamicBone(accessory, new GameObject().transform);
        var marker = new AssetAccessoryPhysics
        {
            gameObject = accessory,
            SourceIdentifier = "fixture-source",
            ImportedRoot = root.transform,
            Chains = new[] { ownA, ownB, otherCharacterBone, staleBone, null }
        };
        object collider = new object();
        ownA.m_Colliders.Add(collider);
        var originalRoot = ownA.m_Root;
        Check(marker.SetEnabled(false) == 2, "Only exact owned live chains are switched");
        Check(!ownA.enabled && !ownB.enabled, "All selected accessory chains switch OFF");
        Check(otherPlugin.enabled && otherCharacterBone.enabled && staleBone.enabled, "Other plugins/characters and stale references are untouched");
        Check(ownA.m_Root == originalRoot && ownA.m_Colliders.Count == 1 && ownA.m_Colliders[0] == collider, "OFF preserves root and collider configuration");
        marker.CaptureState(oldAsset);
        Check(oldAsset.PhysicsEnabled == false, "All-OFF runtime state is retained for saving");
        Check(marker.SetEnabled(AssetAccessoryPhysics.RestoreEnabled(oldAsset)) == 2 && !ownA.enabled, "Reapply saved OFF keeps instantiated chains disabled");
        marker.SetEnabled(true);
        marker.CaptureState(oldAsset);
        Check(oldAsset.PhysicsEnabled == true && ownA.enabled && ownB.enabled, "All-ON runtime state is retained for saving");
        ownB.enabled = false;
        marker.CaptureState(offAsset);
        Check(offAsset.PhysicsEnabled == false, "Mixed editor states do not rewrite a saved global OFF");
        marker.CaptureState(oldAsset);
        Check(oldAsset.PhysicsEnabled == true, "Mixed editor states do not rewrite a saved global ON");
        marker.ImportedRoot = new GameObject().transform;
        Check(marker.SetEnabled(false) == 0 && ownA.enabled, "Detached/changed accessory roots reject stale ownership");
        marker.CaptureState(oldAsset);
        Check(oldAsset.PhysicsEnabled == true, "No live chains do not erase the saved switch");
        marker.ImportedRoot = root.transform;
        marker.Chains = null;
        Check(marker.GetOwnedChains().Count == 0, "Missing legacy runtime ownership data is safe");

        Console.WriteLine(checks + " accessory physics state and ownership checks passed (Unity doubles; no game simulation).");
    }
}

// These doubles check ownership and enable/disable behavior only. MessagePack
// field metadata is checked above; this is not a serializer round-trip test.
namespace MessagePack
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class MessagePackObjectAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public sealed class KeyAttribute : Attribute
    {
        public readonly string Key;
        public KeyAttribute(string key) { Key = key; }
    }
}

namespace UnityEngine
{
    public class Object
    {
        public static implicit operator bool(Object value) { return value != null; }
    }
    public class MonoBehaviour : Object
    {
        public GameObject gameObject;
        public Transform transform { get { return gameObject.transform; } }
    }
    public class GameObject : Object
    {
        public readonly Transform transform;
        public GameObject(Transform parent = null) { transform = new Transform { parent = parent }; }
    }
    public class Transform : Object
    {
        public Transform parent;
        public bool IsChildOf(Transform candidate)
        {
            for (var current = this; current != null; current = current.parent)
                if (current == candidate) return true;
            return false;
        }
    }
}

internal sealed class DynamicBone : UnityEngine.MonoBehaviour
{
    public bool enabled = true;
    public Transform m_Root;
    public readonly List<object> m_Colliders = new List<object>();
    public DynamicBone(GameObject owner, Transform root) { gameObject = owner; m_Root = root; }
}
