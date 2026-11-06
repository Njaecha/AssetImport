using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

// Metadata only: no Unity/game/plugin assembly is loaded into the CLR.
internal sealed class MetadataAssembly : IDisposable
{
    private readonly FileStream stream;
    internal readonly PEReader Pe;
    internal readonly MetadataReader Reader;
    internal readonly TypeNames Names = new();
    internal MetadataAssembly(string path)
    {
        stream = File.OpenRead(path);
        Pe = new PEReader(stream);
        Reader = Pe.GetMetadataReader();
    }
    internal string Name(TypeDefinitionHandle handle)
    {
        return Names.GetTypeFromDefinition(Reader, handle, 0);
    }
    internal TypeDefinitionHandle FindType(string name) => Reader.TypeDefinitions.Single(t => Name(t) == name);
    internal MethodDefinitionHandle RequireMethod(string typeName, string methodName, params string[] parameters)
    {
        var matches = Reader.GetTypeDefinition(FindType(typeName)).GetMethods().Where(handle =>
        {
            var method = Reader.GetMethodDefinition(handle);
            return Reader.GetString(method.Name) == methodName &&
                method.DecodeSignature(Names, null).ParameterTypes.SequenceEqual(parameters);
        }).ToList();
        if (matches.Count != 1) throw new Exception($"Missing or ambiguous hook target: {typeName}.{methodName}({string.Join(", ", parameters)})");
        return matches[0];
    }
    public void Dispose() { Pe.Dispose(); stream.Dispose(); }
}

internal sealed class TypeNames : ISignatureTypeProvider<string, object>
{
    internal static string Join(string ns, string name) => ns.Length == 0 ? name : ns + "." + name;
    public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[" + new string(',', shape.Rank - 1) + "]";
    public string GetByReferenceType(string elementType) => elementType + "&";
    public string GetFunctionPointerType(MethodSignature<string> signature) => "methodptr";
    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => genericType + "<" + string.Join(",", typeArguments) + ">";
    public string GetGenericMethodParameter(object genericContext, int index) => "!!" + index;
    public string GetGenericTypeParameter(object genericContext, int index) => "!" + index;
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
    public string GetPinnedType(string elementType) => elementType;
    public string GetPointerType(string elementType) => elementType + "*";
    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => "System." + typeCode;
    public string GetSZArrayType(string elementType) => elementType + "[]";
    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        var type = reader.GetTypeDefinition(handle);
        var parent = type.GetDeclaringType();
        return parent.IsNil ? Join(reader.GetString(type.Namespace), reader.GetString(type.Name)) :
            GetTypeFromDefinition(reader, parent, 0) + "+" + reader.GetString(type.Name);
    }
    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var type = reader.GetTypeReference(handle);
        return type.ResolutionScope.Kind == HandleKind.TypeReference
            ? GetTypeFromReference(reader, (TypeReferenceHandle)type.ResolutionScope, 0) + "+" + reader.GetString(type.Name)
            : Join(reader.GetString(type.Namespace), reader.GetString(type.Name));
    }
    public string GetTypeFromSpecification(MetadataReader reader, object genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
        reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
}
