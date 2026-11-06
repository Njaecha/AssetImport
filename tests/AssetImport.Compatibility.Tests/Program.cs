using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using AssetImport;
using HarmonyLib;

internal static class Program
{
    private const string ControllerName = "KK_Plugins.MaterialEditor.MaterialEditorCharaController";
    private static readonly MethodInfo Tongue = typeof(ProbeController).GetMethod(nameof(ProbeController.CorrectTongue));
    private static readonly MethodInfo Continue = typeof(Program).GetMethod(nameof(Record), BindingFlags.Static | BindingFlags.Public);
    private static readonly Dictionary<ushort, OpCode> Opcodes = typeof(OpCodes).GetFields(BindingFlags.Static | BindingFlags.Public)
        .Select(f => (OpCode)f.GetValue(null)).ToDictionary(op => unchecked((ushort)op.Value));
    private static int callbacks;
    private static ProbeController seen;
    private static bool seenAccessories;

    public static void Record(ProbeController controller, bool accessories)
    {
        callbacks++;
        seen = controller;
        seenAccessories = accessories;
    }

    private static int Main()
    {
        try
        {
            Validate("KK", "MaterialEditor.dll", new Version(4, 0, 3, 0), "4.0.3");
            Validate("KK", "MaterialEditor_5.0.dll", new Version(5, 0, 0, 0), "4.0.3");
            Validate("KKS", "MaterialEditor.dll", new Version(3, 13, 5, 0), "3.13.5");
            Console.WriteLine("PASS: compiled dependency minimum and linked API contracts, real KK 4.0.3/5.0 and KKS signatures, production IL rewrite, executed receiver/order/flags, label transfer and unsupported-layout fallback.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void Validate(string game, string materialEditorFile, Version expectedVersion, string minimumVersion)
    {
        string fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures", game);
        using var gameAssembly = new MetadataAssembly(Path.Combine(fixtures, "Assembly-CSharp.dll"));
        gameAssembly.RequireMethod("Studio.SceneInfo", "Load", "System.String");
        using var me = new MetadataAssembly(Path.Combine(fixtures, materialEditorFile));
        Assert(me.Reader.GetAssemblyDefinition().Version == expectedVersion, game + ": exact pinned MaterialEditor fixture " + expectedVersion);
        using var baseline = new MetadataAssembly(Path.Combine(fixtures, "MaterialEditor.dll"));
        using var plugin = new MetadataAssembly(Path.Combine(fixtures, "AssetImport.dll"));
        if (game == "KK") CompiledPluginContract.ValidateLegacyFrameworkReferences(plugin);
        CompiledPluginContract.Validate(plugin, me, baseline, minimumVersion);
        var fourArgs = Enumerable.Repeat("System.Boolean", 4).ToArray();
        var loadData = me.RequireMethod(ControllerName, "LoadData", fourArgs);
        Assert(me.Reader.GetMethodDefinition(loadData).DecodeSignature(me.Names, null).ReturnType == "System.Collections.IEnumerator", "four-argument LoadData returns an iterator");
        me.RequireMethod(ControllerName, "OnCoordinateBeingLoaded", "ChaFileCoordinate", "System.Boolean");
        me.RequireMethod(ControllerName, "OnReload", "KKAPI.GameMode", "System.Boolean");
        string dictionary = "KKAPI.Utilities.ReadOnlyDictionary`2<System.Int32,Studio.ObjectCtrlInfo>";
        me.RequireMethod("KK_Plugins.MaterialEditor.SceneController", "OnSceneLoad", "KKAPI.Studio.SaveLoad.SceneOperationKind", dictionary);
        me.RequireMethod("KK_Plugins.MaterialEditor.SceneController", "OnObjectsCopied", dictionary);
        var tongueHandle = me.RequireMethod(ControllerName, "CorrectTongue");
        var tongueDefinition = me.Reader.GetMethodDefinition(tongueHandle);
        Assert(tongueDefinition.DecodeSignature(me.Names, null).ReturnType == "System.Void" &&
            (tongueDefinition.Attributes & MethodAttributes.Static) == 0, "CorrectTongue is an instance void anchor");
        int tongueToken = MetadataTokens.GetToken(tongueHandle);

        var reader = me.Reader;
        // The four-argument iterator's generated type is identified from the actual
        // assembly's fields and method body, not from a hardcoded compiler suffix.
        var candidates = reader.TypeDefinitions.Where(handle =>
        {
            var type = reader.GetTypeDefinition(handle);
            return reader.GetString(type.Name).StartsWith("<LoadData>") && type.GetFields().Any(field =>
                reader.GetFieldDefinition(field).DecodeSignature(me.Names, null) == ControllerName);
        }).ToList();
        Assert(candidates.Count == 1, game + ": unique LoadData state machine");
        var stateDefinition = reader.GetTypeDefinition(candidates[0]);
        var stateFields = stateDefinition.GetFields().ToDictionary(handle => MetadataTokens.GetToken(handle), handle =>
        {
            var field = reader.GetFieldDefinition(handle);
            return (Name: reader.GetString(field.Name), Type: field.DecodeSignature(me.Names, null));
        });
        Assert(stateFields.Values.Count(f => f.Type == ControllerName) == 1, game + ": captured controller receiver");
        Assert(stateFields.Values.Any(f => f.Name == "accessories" && f.Type == "System.Boolean"), game + ": accessories flag");
        Assert(stateFields.Values.Any(f => f.Name == "body" && f.Type == "System.Boolean"), game + ": body guard");
        var moveNext = stateDefinition.GetMethods().Single(h => reader.GetString(reader.GetMethodDefinition(h).Name) == "MoveNext");
        byte[] il = me.Pe.GetMethodBody(reader.GetMethodDefinition(moveNext).RelativeVirtualAddress).GetILBytes();

        // Project metadata-only field identities onto an executable inert iterator.
        // Instructions/branches still come from each real DLL. No plugin code runs.
        Type stateType = CreateStateType(stateFields.Values.ToArray(), game + expectedVersion);
        var fieldMap = stateFields.ToDictionary(kv => kv.Key, kv => stateType.GetField(kv.Value.Name));
        var method = stateType.GetMethod("MoveNext");
        var labelGenerator = new DynamicMethod("labels", typeof(void), Type.EmptyTypes).GetILGenerator();
        var original = ReadInstructions(il, fieldMap, tongueToken, labelGenerator);
        var snapshot = Snapshot(original);
        Assert(MaterialEditorLoadDataPatch.TryPatch(original, method, typeof(ProbeController), Tongue, Continue,
            out var patched, out var reason), game + ": production patch accepted: " + reason);
        Assert(Snapshot(original) == snapshot, "successful patch must not mutate input instructions");
        Assert(patched.Count == original.Count + 5, "exactly one callback injection");

        int injectionEnd = patched.FindIndex(c => Equals(c.operand, Continue));
        Assert(injectionEnd >= 4 && patched[injectionEnd - 3].opcode == OpCodes.Ldfld &&
            ((FieldInfo)patched[injectionEnd - 3].operand).FieldType == typeof(ProbeController), "callback receiver is captured controller, not state machine");
        foreach (bool body in new[] { false, true })
            foreach (bool accessories in new[] { false, true }) ExecuteAnchor(stateType, patched, body, accessories);

        // A branch to the original anchor must reach the injected callback first.
        var withLabel = original.Select(c => new CodeInstruction(c)).ToList();
        int anchor = injectionEnd - 4;
        Label entry = labelGenerator.DefineLabel();
        withLabel[anchor].labels.Add(entry);
        Assert(MaterialEditorLoadDataPatch.TryPatch(withLabel, method, typeof(ProbeController), Tongue, Continue,
            out var labeledPatch, out reason), "labeled anchor accepted");
        Assert(labeledPatch[anchor].labels.Contains(entry) && !labeledPatch[anchor + 5].labels.Contains(entry), "anchor branch transferred to injection");
        ExecuteAnchor(stateType, labeledPatch, false, true, entry);

        var unknown = original.Select(c => new CodeInstruction(c)).ToList();
        unknown.Single(c => Equals(c.operand, Tongue)).operand = typeof(ProbeController).GetMethod(nameof(ProbeController.Unknown));
        AssertFallback(unknown, method, "unknown call anchor");
        var badBranch = original.Select(c => new CodeInstruction(c)).ToList();
        badBranch[anchor + 2].operand = labelGenerator.DefineLabel();
        AssertFallback(badBranch, method, "unknown branch destination");
        AssertFallback(original, typeof(MissingReceiver).GetMethod(nameof(MissingReceiver.MoveNext)), "missing captured receiver");
        Console.WriteLine($"PASS {game}: MaterialEditor {reader.GetAssemblyDefinition().Version}, {reader.GetString(stateDefinition.Name)}, all hook signatures and 5 execution probes");
    }

    private static Type CreateStateType((string Name, string Type)[] fields, string game)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("IteratorProbe" + game), AssemblyBuilderAccess.Run);
        var type = assembly.DefineDynamicModule("main").DefineType("Iterator", TypeAttributes.Public);
        foreach (var field in fields)
        {
            Type target = field.Type == ControllerName ? typeof(ProbeController) : field.Type switch
            {
                "System.Boolean" => typeof(bool), "System.Int32" => typeof(int), "System.Object" => typeof(object),
                _ => throw new Exception("Unsupported captured field type: " + field.Type)
            };
            type.DefineField(field.Name, target, FieldAttributes.Public);
        }
        var method = type.DefineMethod("MoveNext", MethodAttributes.Public, typeof(bool), Type.EmptyTypes);
        var il = method.GetILGenerator(); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret);
        return type.CreateType();
    }

    private static List<CodeInstruction> ReadInstructions(byte[] bytes, Dictionary<int, FieldInfo> fields, int tongueToken, ILGenerator generator)
    {
        var instructions = new List<(int Offset, CodeInstruction Code)>();
        var labels = new Dictionary<int, Label>();
        Label Target(int offset)
        {
            if (!labels.TryGetValue(offset, out var label)) labels.Add(offset, label = generator.DefineLabel());
            return label;
        }
        for (int p = 0; p < bytes.Length;)
        {
            int offset = p; ushort opcode = bytes[p++];
            if (opcode == 0xfe) opcode = (ushort)(0xfe00 | bytes[p++]);
            var op = Opcodes[opcode]; object value = null;
            switch (op.OperandType)
            {
                case OperandType.InlineNone: break;
                case OperandType.ShortInlineBrTarget: value = Target(p + 1 + (sbyte)bytes[p]); p++; break;
                case OperandType.InlineBrTarget: value = Target(p + 4 + BitConverter.ToInt32(bytes, p)); p += 4; break;
                case OperandType.ShortInlineI: value = (sbyte)bytes[p++]; break;
                case OperandType.ShortInlineVar: value = bytes[p++]; break;
                case OperandType.InlineVar: value = BitConverter.ToUInt16(bytes, p); p += 2; break;
                case OperandType.InlineI8: value = BitConverter.ToInt64(bytes, p); p += 8; break;
                case OperandType.InlineR: value = BitConverter.ToDouble(bytes, p); p += 8; break;
                case OperandType.ShortInlineR: value = BitConverter.ToSingle(bytes, p); p += 4; break;
                case OperandType.InlineSwitch:
                    int count = BitConverter.ToInt32(bytes, p); p += 4;
                    int end = p + count * 4; var targets = new Label[count];
                    for (int i = 0; i < count; i++) { targets[i] = Target(end + BitConverter.ToInt32(bytes, p)); p += 4; }
                    value = targets; break;
                default:
                    int token = BitConverter.ToInt32(bytes, p); p += 4; value = token;
                    if (op.OperandType == OperandType.InlineField && fields.TryGetValue(token, out var field)) value = field;
                    if (op.OperandType == OperandType.InlineMethod && token == tongueToken) value = Tongue;
                    break;
            }
            instructions.Add((offset, new CodeInstruction(op, value)));
        }
        foreach (var instruction in instructions)
            if (labels.TryGetValue(instruction.Offset, out var label)) instruction.Code.labels.Add(label);
        return instructions.Select(i => i.Code).ToList();
    }

    private static void ExecuteAnchor(Type stateType, List<CodeInstruction> patched, bool body, bool accessories, Label? jumpTo = null)
    {
        int start = patched.FindIndex(c => Equals(c.operand, Continue)) - 4;
        int end = patched.FindIndex(c => Equals(c.operand, Tongue));
        var captured = stateType.GetFields().Single(f => f.FieldType == typeof(ProbeController));
        var dynamic = new DynamicMethod("RunAnchor", typeof(void), new[] { stateType }, typeof(Program).Module, true);
        var il = dynamic.GetILGenerator();
        il.DeclareLocal(typeof(int)); il.DeclareLocal(typeof(ProbeController));
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, captured); il.Emit(OpCodes.Stloc_1);
        var labelMap = new Dictionary<Label, Label>();
        Label Map(Label old) { if (!labelMap.TryGetValue(old, out var mapped)) labelMap.Add(old, mapped = il.DefineLabel()); return mapped; }
        if (jumpTo.HasValue) il.Emit(OpCodes.Br, Map(jumpTo.Value));
        for (int i = start; i <= end; i++)
        {
            var instruction = patched[i];
            foreach (Label label in instruction.labels) il.MarkLabel(Map(label));
            switch (instruction.operand)
            {
                case null: il.Emit(instruction.opcode); break;
                case FieldInfo field: il.Emit(instruction.opcode, field); break;
                case MethodInfo method: il.Emit(instruction.opcode, method); break;
                case Label label: il.Emit(instruction.opcode, Map(label)); break;
                default: throw new Exception("Unexpected operand in executed anchor: " + instruction.operand);
            }
        }
        foreach (Label label in patched[end + 1].labels) il.MarkLabel(Map(label));
        il.Emit(OpCodes.Ret);
        var state = Activator.CreateInstance(stateType);
        var controller = new ProbeController();
        captured.SetValue(state, controller);
        stateType.GetField("body").SetValue(state, body);
        stateType.GetField("accessories").SetValue(state, accessories);
        callbacks = 0; seen = null; seenAccessories = !accessories;
        dynamic.Invoke(null, new[] { state });
        Assert(callbacks == 1 && ReferenceEquals(seen, controller) && seenAccessories == accessories, "correct callback receiver and accessories flag on each body branch");
        Assert(controller.TongueCalls == (body ? 1 : 0), "original body condition preserved");
        Assert(controller.CallbackWasFirst, "import callback precedes original material restoration");
    }

    private static void AssertFallback(List<CodeInstruction> original, MethodInfo method, string context)
    {
        var snapshot = Snapshot(original);
        Assert(!MaterialEditorLoadDataPatch.TryPatch(original, method, typeof(ProbeController), Tongue, Continue, out var result, out var reason), context + " rejected");
        Assert(!string.IsNullOrEmpty(reason) && result.Count == original.Count &&
            result.Zip(original).All(pair => ReferenceEquals(pair.First, pair.Second)) && Snapshot(result) == snapshot, context + " preserves original IL/labels");
    }
    private static string Snapshot(IEnumerable<CodeInstruction> code) => string.Join("|", code.Select(c => c.opcode.Value + ":" + c.operand + ":" + string.Join(",", c.labels.Select(l => l.GetHashCode())) + ":" + c.blocks.Count));
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }

    public class ProbeController
    {
        public int TongueCalls;
        public bool CallbackWasFirst = true;
        public void CorrectTongue() { TongueCalls++; CallbackWasFirst &= callbacks == 1; }
        public void Unknown() { }
    }
    public class MissingReceiver { public bool MoveNext() => false; }
}
