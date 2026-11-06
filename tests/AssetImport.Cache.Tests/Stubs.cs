using System;

// These tests exercise cache behavior without loading Unity or BepInEx.
// MessagePack attributes are retained by linked production source; serialization itself
// is outside the scope of this standalone cache test executable.
namespace MessagePack
{
    [AttributeUsage(AttributeTargets.Class)]
    internal sealed class MessagePackObjectAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Property)]
    internal sealed class KeyAttribute : Attribute
    {
        public KeyAttribute(string key) { }
    }
}

namespace AssetImport
{
    internal static class AssetImport
    {
        internal static readonly TestLogger Logger = new TestLogger();
    }

    internal sealed class TestLogger
    {
        public void LogWarning(object message) { }
        public void LogError(object message) { }
    }
}
