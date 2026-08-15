using System.Security.Cryptography;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

const string Official28Sha256 = "A23F92819F4C8EC6A42115F355C942259C081312A25E47DDDE97B3D6B1C82EE9";
const string MainWindowName = "SMIDIToKey.MainWindow";

if (args.Length == 2 && args[0].Equals("check", StringComparison.OrdinalIgnoreCase))
{
    using var module = ModuleDefMD.Load(Path.GetFullPath(args[1]));
    var patched = IsPatched(module);
    Console.WriteLine(patched ? "PATCHED" : "NOT_PATCHED");
    return patched ? 0 : 1;
}

if (args.Length != 4 || !args[0].Equals("apply", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  Patcher check <SMIDIToKey.exe>");
    Console.Error.WriteLine("  Patcher apply <official.exe> <PatchPayload.dll> <output.exe>");
    return 2;
}

var officialPath = Path.GetFullPath(args[1]);
var payloadPath = Path.GetFullPath(args[2]);
var outputPath = Path.GetFullPath(args[3]);

var officialHash = GetSha256(officialPath);
if (!officialHash.Equals(Official28Sha256, StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException(
        $"Unsupported MIDIToKey executable. Expected official 2.8 hash {Official28Sha256}, got {officialHash}.");
}

using (var targetModule = ModuleDefMD.Load(officialPath))
using (var payloadModule = ModuleDefMD.Load(payloadPath))
{
    if (IsPatched(targetModule))
    {
        throw new InvalidOperationException("The executable already contains this patch.");
    }

    ApplyPatch(targetModule, payloadModule);
    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
    targetModule.Write(outputPath);
}

using (var verificationModule = ModuleDefMD.Load(outputPath))
{
    if (!IsPatched(verificationModule))
    {
        throw new InvalidOperationException("Output verification failed.");
    }
    if (verificationModule.GetAssemblyRefs().Any(reference =>
            reference.Name.String.Equals("PatchPayload", StringComparison.OrdinalIgnoreCase)))
    {
        throw new InvalidOperationException("Output still references the compile-time payload assembly.");
    }
}

Console.WriteLine($"Patched executable created: {outputPath}");
Console.WriteLine($"SHA-256: {GetSha256(outputPath)}");
return 0;

static void ApplyPatch(ModuleDef targetModule, ModuleDef payloadModule)
{
    var targetType = targetModule.Find(MainWindowName, isReflectionName: false)
        ?? throw new InvalidOperationException("Target MainWindow was not found.");
    var payloadType = payloadModule.Find(MainWindowName, isReflectionName: false)
        ?? throw new InvalidOperationException("Patch payload MainWindow was not found.");
    var importer = new Importer(targetModule, ImporterOptions.TryToUseDefs);

    var newFieldNames = new[]
    {
        "pressedOctaveKeys",
        "tapEligibleOctaveKeys",
        "passthroughKeyboardKeys",
        "activeKeyboardNotes",
        "lowerHandBaseNote",
        "upperHandBaseNote"
    };

    foreach (var fieldName in newFieldNames)
    {
        if (targetType.Fields.Any(field => field.Name == fieldName))
        {
            throw new InvalidOperationException($"Target already contains field {fieldName}.");
        }

        var payloadField = payloadType.Fields.Single(field => field.Name == fieldName);
        targetType.Fields.Add(new FieldDefUser(
            payloadField.Name,
            importer.Import(payloadField.FieldSig),
            payloadField.Attributes));
    }

    var addedMethodNames = new[]
    {
        "IsOctaveKey",
        "ShouldPassThroughKeyboardKey",
        "ResolveKeyboardNotes",
        "GetUpperHandOffset",
        "GetLowerHandOffset",
        "ChangeOctave",
        "FormatOctaveRange",
        "UpdateTwoHandIndicator"
    };

    foreach (var methodName in addedMethodNames)
    {
        if (targetType.Methods.Any(method => method.Name == methodName))
        {
            throw new InvalidOperationException($"Target already contains method {methodName}.");
        }

        var payloadMethod = payloadType.Methods.Single(method => method.Name == methodName);
        targetType.Methods.Add(new MethodDefUser(
            payloadMethod.Name,
            importer.Import(payloadMethod.MethodSig),
            payloadMethod.ImplAttributes,
            payloadMethod.Attributes));
    }

    var payloadKeyPress = payloadType.Methods.Single(method => method.Name == "KeyPress");
    var targetKeyPress = targetType.Methods.Single(method => method.Name == "KeyPress");
    CloneBody(
        payloadKeyPress,
        targetKeyPress,
        payloadType,
        targetType,
        payloadModule,
        targetModule,
        importer);

    foreach (var methodName in addedMethodNames)
    {
        var payloadMethod = payloadType.Methods.Single(method => method.Name == methodName);
        var targetMethod = targetType.Methods.Single(method => method.Name == methodName);
        CloneBody(
            payloadMethod,
            targetMethod,
            payloadType,
            targetType,
            payloadModule,
            targetModule,
            importer);
    }

    PatchConstructor(
        payloadType,
        targetType,
        payloadModule,
        targetModule,
        importer,
        new[]
        {
            "pressedOctaveKeys",
            "tapEligibleOctaveKeys",
            "passthroughKeyboardKeys",
            "activeKeyboardNotes",
            "lowerHandBaseNote",
            "upperHandBaseNote"
        });
}

static void PatchConstructor(
    TypeDef payloadType,
    TypeDef targetType,
    ModuleDef payloadModule,
    ModuleDef targetModule,
    Importer importer,
    IReadOnlyCollection<string> initializedFieldNames)
{
    var payloadConstructor = payloadType.Methods.Single(method => method.Name == ".ctor" && method.MethodSig.Params.Count == 0);
    var targetConstructor = targetType.Methods.Single(method => method.Name == ".ctor" && method.MethodSig.Params.Count == 0);
    var targetInstructions = targetConstructor.Body.Instructions;

    var baseConstructorIndex = targetInstructions
        .Select((instruction, index) => (instruction, index))
        .First(pair =>
            pair.instruction.OpCode.Code is Code.Call or Code.Callvirt &&
            pair.instruction.Operand is IMethod method &&
            method.Name == ".ctor")
        .index;
    var insertionIndex = baseConstructorIndex + 1;

    foreach (var fieldName in initializedFieldNames)
    {
        var store = payloadConstructor.Body.Instructions.Single(instruction =>
            instruction.OpCode.Code == Code.Stfld &&
            instruction.Operand is IField field &&
            field.Name == fieldName);
        var storeIndex = payloadConstructor.Body.Instructions.IndexOf(store);
        if (storeIndex < 2)
        {
            throw new InvalidOperationException($"Unexpected initializer shape for {fieldName}.");
        }

        foreach (var sourceInstruction in payloadConstructor.Body.Instructions.Skip(storeIndex - 2).Take(3))
        {
            var cloned = new Instruction(sourceInstruction.OpCode)
            {
                Operand = MapOperand(
                    sourceInstruction.Operand,
                    payloadConstructor,
                    targetConstructor,
                    payloadType,
                    targetType,
                    payloadModule,
                    targetModule,
                    importer,
                    new Dictionary<Local, Local>(),
                    new Dictionary<Instruction, Instruction>())
            };
            targetInstructions.Insert(insertionIndex++, cloned);
        }
    }

    var indicatorMethod = targetType.Methods.Single(method => method.Name == "UpdateTwoHandIndicator");
    var returns = targetInstructions.Where(instruction => instruction.OpCode.Code == Code.Ret).ToList();
    foreach (var returnInstruction in returns)
    {
        var returnIndex = targetInstructions.IndexOf(returnInstruction);
        targetInstructions.Insert(returnIndex, Instruction.Create(OpCodes.Ldarg_0));
        targetInstructions.Insert(returnIndex + 1, Instruction.Create(OpCodes.Call, indicatorMethod));
    }

    targetConstructor.Body.KeepOldMaxStack = true;
}

static void CloneBody(
    MethodDef source,
    MethodDef destination,
    TypeDef sourceType,
    TypeDef destinationType,
    ModuleDef sourceModule,
    ModuleDef destinationModule,
    Importer importer)
{
    if (!source.HasBody)
    {
        throw new InvalidOperationException($"Source method {source.Name} has no body.");
    }

    var sourceBody = source.Body;
    var destinationBody = new CilBody
    {
        InitLocals = sourceBody.InitLocals,
        MaxStack = sourceBody.MaxStack,
        KeepOldMaxStack = true
    };

    var localMap = new Dictionary<Local, Local>();
    foreach (var sourceLocal in sourceBody.Variables)
    {
        var destinationLocal = new Local(MapTypeSig(
            sourceLocal.Type,
            sourceModule,
            destinationModule,
            importer))
        {
            Name = sourceLocal.Name
        };
        destinationBody.Variables.Add(destinationLocal);
        localMap[sourceLocal] = destinationLocal;
    }

    var instructionMap = new Dictionary<Instruction, Instruction>();
    foreach (var sourceInstruction in sourceBody.Instructions)
    {
        var destinationInstruction = new Instruction(sourceInstruction.OpCode);
        destinationBody.Instructions.Add(destinationInstruction);
        instructionMap[sourceInstruction] = destinationInstruction;
    }

    foreach (var sourceInstruction in sourceBody.Instructions)
    {
        instructionMap[sourceInstruction].Operand = MapOperand(
            sourceInstruction.Operand,
            source,
            destination,
            sourceType,
            destinationType,
            sourceModule,
            destinationModule,
            importer,
            localMap,
            instructionMap);
    }

    foreach (var sourceHandler in sourceBody.ExceptionHandlers)
    {
        destinationBody.ExceptionHandlers.Add(new ExceptionHandler(sourceHandler.HandlerType)
        {
            CatchType = sourceHandler.CatchType == null
                ? null
                : MapType(sourceHandler.CatchType, sourceModule, destinationModule, importer),
            TryStart = MapInstruction(sourceHandler.TryStart, instructionMap),
            TryEnd = MapInstruction(sourceHandler.TryEnd, instructionMap),
            HandlerStart = MapInstruction(sourceHandler.HandlerStart, instructionMap),
            HandlerEnd = MapInstruction(sourceHandler.HandlerEnd, instructionMap),
            FilterStart = MapInstruction(sourceHandler.FilterStart, instructionMap)
        });
    }

    destination.Body = destinationBody;
}

static object? MapOperand(
    object? operand,
    MethodDef sourceMethod,
    MethodDef destinationMethod,
    TypeDef sourceType,
    TypeDef destinationType,
    ModuleDef sourceModule,
    ModuleDef destinationModule,
    Importer importer,
    IReadOnlyDictionary<Local, Local> localMap,
    IReadOnlyDictionary<Instruction, Instruction> instructionMap)
{
    return operand switch
    {
        null => null,
        Instruction instruction => instructionMap[instruction],
        IList<Instruction> instructions => instructions.Select(instruction => instructionMap[instruction]).ToArray(),
        Local local => localMap[local],
        Parameter parameter => destinationMethod.Parameters[parameter.Index],
        IField field when field.DeclaringType.FullName == sourceType.FullName =>
            destinationType.Fields.Single(candidate => candidate.Name == field.Name),
        IField field => MapField(field, sourceModule, destinationModule, importer),
        IMethod method when method.DeclaringType.FullName == sourceType.FullName =>
            FindDestinationMethod(method, destinationType),
        IMethod method => MapMethod(method, sourceModule, destinationModule, importer),
        IType type => MapType(type, sourceModule, destinationModule, importer),
        _ => operand
    };
}

static IField MapField(IField sourceField, ModuleDef sourceModule, ModuleDef destinationModule, Importer importer)
{
    var sourceDefinition = sourceModule.Find(sourceField.DeclaringType.FullName, isReflectionName: false);
    if (sourceDefinition == null)
    {
        return importer.Import(sourceField);
    }

    var destinationType = destinationModule.Find(sourceField.DeclaringType.FullName, isReflectionName: false)
        ?? throw new InvalidOperationException($"Could not map payload type {sourceField.DeclaringType.FullName}.");
    return destinationType.Fields.Single(field => field.Name == sourceField.Name);
}

static IMethod MapMethod(IMethod sourceMethod, ModuleDef sourceModule, ModuleDef destinationModule, Importer importer)
{
    var sourceDefinition = sourceModule.Find(sourceMethod.DeclaringType.FullName, isReflectionName: false);
    if (sourceDefinition == null)
    {
        return importer.Import(sourceMethod);
    }

    var destinationType = destinationModule.Find(sourceMethod.DeclaringType.FullName, isReflectionName: false)
        ?? throw new InvalidOperationException($"Could not map payload type {sourceMethod.DeclaringType.FullName}.");
    var parameterCount = sourceMethod.MethodSig?.Params.Count ?? -1;
    var directMatches = destinationType.Methods
        .Where(method => method.Name == sourceMethod.Name && method.MethodSig.Params.Count == parameterCount)
        .ToList();
    if (directMatches.Count == 1)
    {
        return directMatches[0];
    }

    if (sourceMethod.Name == "get_Value" && parameterCount == 0 && destinationType.BaseType != null)
    {
        var signature = importer.Import(sourceMethod.MethodSig!);
        return new MemberRefUser(destinationModule, sourceMethod.Name, signature, destinationType.BaseType);
    }

    throw new InvalidOperationException($"Could not uniquely map method {sourceMethod.FullName}.");
}

static ITypeDefOrRef MapType(IType sourceType, ModuleDef sourceModule, ModuleDef destinationModule, Importer importer)
{
    var sourceDefinition = sourceModule.Find(sourceType.FullName, isReflectionName: false);
    if (sourceDefinition != null)
    {
        return destinationModule.Find(sourceType.FullName, isReflectionName: false)
            ?? throw new InvalidOperationException($"Could not map payload type {sourceType.FullName}.");
    }
    return sourceType switch
    {
        ITypeDefOrRef typeDefOrRef => importer.Import(typeDefOrRef),
        TypeSig typeSig => importer.Import(typeSig).ToTypeDefOrRef(),
        _ => throw new InvalidOperationException($"Unsupported type operand {sourceType.GetType().FullName}.")
    };
}

static TypeSig MapTypeSig(TypeSig sourceType, ModuleDef sourceModule, ModuleDef destinationModule, Importer importer)
{
    var typeDefinition = sourceType.ToTypeDefOrRef();
    if (typeDefinition != null && sourceModule.Find(typeDefinition.FullName, isReflectionName: false) != null)
    {
        var mapped = destinationModule.Find(typeDefinition.FullName, isReflectionName: false)
            ?? throw new InvalidOperationException($"Could not map payload type {typeDefinition.FullName}.");
        return mapped.ToTypeSig();
    }
    return importer.Import(sourceType);
}

static MethodDef FindDestinationMethod(IMethod sourceMethod, TypeDef destinationType)
{
    var parameterCount = sourceMethod.MethodSig?.Params.Count ?? -1;
    var matches = destinationType.Methods
        .Where(candidate => candidate.Name == sourceMethod.Name && candidate.MethodSig.Params.Count == parameterCount)
        .ToList();
    if (matches.Count != 1)
    {
        throw new InvalidOperationException($"Could not uniquely map method {sourceMethod.FullName}.");
    }
    return matches[0];
}

static Instruction? MapInstruction(
    Instruction? source,
    IReadOnlyDictionary<Instruction, Instruction> instructionMap)
{
    return source == null ? null : instructionMap[source];
}

static bool IsPatched(ModuleDef module)
{
    var type = module.Find(MainWindowName, isReflectionName: false);
    if (type == null)
    {
        return false;
    }

    var requiredFields = new[]
    {
        "pressedOctaveKeys",
        "tapEligibleOctaveKeys",
        "passthroughKeyboardKeys",
        "activeKeyboardNotes",
        "lowerHandBaseNote",
        "upperHandBaseNote"
    };
    var requiredMethods = new[]
    {
        "IsOctaveKey",
        "ShouldPassThroughKeyboardKey",
        "ResolveKeyboardNotes",
        "GetUpperHandOffset",
        "GetLowerHandOffset",
        "ChangeOctave",
        "FormatOctaveRange",
        "UpdateTwoHandIndicator"
    };

    return requiredFields.All(name => type.Fields.Any(field => field.Name == name)) &&
           requiredMethods.All(name => type.Methods.Any(method => method.Name == name));
}

static string GetSha256(string path)
{
    using var stream = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(stream));
}
