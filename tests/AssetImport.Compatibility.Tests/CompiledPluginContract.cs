using System.Reflection;
using System.Reflection.Metadata;

// Read the built plugin's references and attributes, so a newer optional overload
// or an accidentally raised dependency version is detected even if Hooks.cs works.
internal static class CompiledPluginContract
{
    private const string MaterialEditorGuid = "com.deathweasel.bepinex.materialeditor";

    internal static void ValidateLegacyFrameworkReferences(MetadataAssembly plugin)
    {
        var reader = plugin.Reader;
        var core = reader.AssemblyReferences.Select(reader.GetAssemblyReference)
            .Single(reference => reader.GetString(reference.Name) == "mscorlib");
        Require(core.Version == new Version(2, 0, 0, 0), "KK must target the CLR 2 core library.");
        foreach (var handle in reader.TypeReferences)
        {
            string name = plugin.Names.GetTypeFromReference(reader, handle, 0);
            // Desktop net35 has this type, but the reported KK Unity/Mono profile
            // cannot load it. Its presence in an untaken validation branch can
            // prevent the entire transform method from being JIT-compiled.
            Require(name != "System.IO.InvalidDataException",
                "KK references System.IO.InvalidDataException, which fails to load in the reported Unity 5.6 runtime.");
            if (name == "System.ArgumentException")
                Require(ReferenceAssembly(reader, handle) == "mscorlib", "Use the CLR 2 core ArgumentException.");
        }
        Console.WriteLine("  KK CLR 2 contract: no unavailable InvalidDataException reference");
    }

    internal static void Validate(MetadataAssembly plugin, MetadataAssembly target, MetadataAssembly baseline, string minimum)
    {
        var source = plugin.Reader;
        string assemblyName = target.Reader.GetString(target.Reader.GetAssemblyDefinition().Name);
        var reference = source.AssemblyReferences.Select(source.GetAssemblyReference)
            .Single(item => source.GetString(item.Name) == assemblyName);
        Require(reference.Version == baseline.Reader.GetAssemblyDefinition().Version,
            $"Plugin must compile against baseline {assemblyName} {baseline.Reader.GetAssemblyDefinition().Version}; found {reference.Version}.");
        ValidateDependencyAttribute(plugin, minimum);

        int types = 0, methods = 0;
        foreach (var handle in source.TypeReferences)
        {
            if (ReferenceAssembly(source, handle) != assemblyName) continue;
            string name = plugin.Names.GetTypeFromReference(source, handle, 0);
            var targetHandle = target.FindType(name);
            Require(IsPublic(target.Reader.GetTypeDefinition(targetHandle).Attributes), "Referenced MaterialEditor type is not public: " + name);
            ValidateEnumConstants(name, target, baseline);
            types++;
        }
        foreach (var handle in source.MemberReferences)
        {
            var member = source.GetMemberReference(handle);
            if (member.Parent.Kind != HandleKind.TypeReference ||
                ReferenceAssembly(source, (TypeReferenceHandle)member.Parent) != assemblyName) continue;
            string typeName = plugin.Names.GetTypeFromReference(source, (TypeReferenceHandle)member.Parent, 0);
            string memberName = source.GetString(member.Name);
            var type = target.Reader.GetTypeDefinition(target.FindType(typeName));
            Require(member.GetKind() == MemberReferenceKind.Method, "Unexpected linked MaterialEditor field; add an explicit compatibility check: " + typeName + "." + memberName);
            var wanted = member.DecodeMethodSignature(plugin.Names, null);
            var matches = type.GetMethods().Select(target.Reader.GetMethodDefinition)
                .Where(method => target.Reader.GetString(method.Name) == memberName &&
                    SameSignature(wanted, method.DecodeSignature(target.Names, null))).ToList();
            Require(matches.Count == 1, $"Missing binary-compatible member: {typeName}.{memberName}({string.Join(", ", wanted.ParameterTypes)}) -> {wanted.ReturnType}");
            Require((matches[0].Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public,
                "Directly linked MaterialEditor method is not public: " + typeName + "." + memberName);
            methods++;
        }
        Require(types > 0 && methods > 0, "No compiled MaterialEditor references were checked.");
        Console.WriteLine($"  compiled plugin contract: {types} types, {methods} linked methods, minimum {minimum}");
    }

    private static bool SameSignature(MethodSignature<string> first, MethodSignature<string> second) =>
        first.Header.RawValue == second.Header.RawValue && first.GenericParameterCount == second.GenericParameterCount &&
        first.RequiredParameterCount == second.RequiredParameterCount && first.ReturnType == second.ReturnType &&
        first.ParameterTypes.SequenceEqual(second.ParameterTypes);

    private static string ReferenceAssembly(MetadataReader reader, TypeReferenceHandle handle)
    {
        EntityHandle scope = reader.GetTypeReference(handle).ResolutionScope;
        while (scope.Kind == HandleKind.TypeReference)
            scope = reader.GetTypeReference((TypeReferenceHandle)scope).ResolutionScope;
        return scope.Kind == HandleKind.AssemblyReference
            ? reader.GetString(reader.GetAssemblyReference((AssemblyReferenceHandle)scope).Name) : null;
    }

    private static bool IsPublic(TypeAttributes attributes) =>
        (attributes & TypeAttributes.VisibilityMask) == TypeAttributes.Public ||
        (attributes & TypeAttributes.VisibilityMask) == TypeAttributes.NestedPublic;

    private static void ValidateEnumConstants(string name, MetadataAssembly target, MetadataAssembly baseline)
    {
        var baselineType = baseline.Reader.GetTypeDefinition(baseline.FindType(name));
        if (baselineType.BaseType.Kind != HandleKind.TypeReference ||
            baseline.Names.GetTypeFromReference(baseline.Reader, (TypeReferenceHandle)baselineType.BaseType, 0) != "System.Enum") return;
        var actualFields = target.Reader.GetTypeDefinition(target.FindType(name)).GetFields()
            .Select(target.Reader.GetFieldDefinition).ToDictionary(field => target.Reader.GetString(field.Name));
        foreach (var fieldHandle in baselineType.GetFields())
        {
            var expected = baseline.Reader.GetFieldDefinition(fieldHandle);
            if (expected.GetDefaultValue().IsNil) continue;
            string fieldName = baseline.Reader.GetString(expected.Name);
            Require(actualFields.TryGetValue(fieldName, out var actual) && !actual.GetDefaultValue().IsNil,
                "Missing enum constant: " + name + "." + fieldName);
            var first = baseline.Reader.GetConstant(expected.GetDefaultValue());
            var second = target.Reader.GetConstant(actual.GetDefaultValue());
            Require(first.TypeCode == second.TypeCode &&
                baseline.Reader.GetBlobBytes(first.Value).SequenceEqual(target.Reader.GetBlobBytes(second.Value)),
                "Inlined enum value changed: " + name + "." + fieldName);
        }
    }

    private static void ValidateDependencyAttribute(MetadataAssembly plugin, string minimum)
    {
        var reader = plugin.Reader;
        var type = reader.GetTypeDefinition(plugin.FindType("AssetImport.AssetImport"));
        var versions = new List<string>();
        foreach (var handle in type.GetCustomAttributes())
        {
            var attribute = reader.GetCustomAttribute(handle);
            if (attribute.Constructor.Kind != HandleKind.MemberReference) continue;
            var constructor = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
            if (constructor.Parent.Kind != HandleKind.TypeReference ||
                plugin.Names.GetTypeFromReference(reader, (TypeReferenceHandle)constructor.Parent, 0) != "BepInEx.BepInDependency") continue;
            var blob = reader.GetBlobReader(attribute.Value);
            Require(blob.ReadUInt16() == 1, "Invalid dependency attribute blob.");
            if (blob.ReadSerializedString() != MaterialEditorGuid) continue;
            Require(constructor.DecodeMethodSignature(plugin.Names, null).ParameterTypes.SequenceEqual(new[] { "System.String", "System.String" }),
                "MaterialEditor dependency must specify a minimum version.");
            versions.Add(blob.ReadSerializedString());
        }
        Require(versions.Count == 1 && versions[0] == minimum,
            "Compiled BepInDependency minimum must be " + minimum + "; found " + string.Join(", ", versions));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
