namespace MessagePack
{
    [AttributeUsage(AttributeTargets.Class)] public sealed class MessagePackObjectAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)] public sealed class KeyAttribute(string key) : Attribute { public string Key = key; }
}
