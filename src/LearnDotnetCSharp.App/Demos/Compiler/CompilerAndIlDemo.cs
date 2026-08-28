using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using LearnDotnetCSharp.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LearnDotnetCSharp.Demos.Compiler;

public sealed class CompilerAndIlDemo : IDemo
{
    private const string SampleSource = """
        using System;
        using System.Threading.Tasks;

        namespace DynamicSample;

        public static class LayeredSample
        {
            public static int HighLevel(int[] values)
            {
                using var probe = new DisposeProbe();
                var sum = 0;

                foreach (var value in values)
                {
                    if (value is > 0 and <= 10)
                    {
                        sum = checked(sum + value);
                    }
                }

                return sum;
            }

            public static int LowLevelEquivalent(int[] values)
            {
                var probe = new DisposeProbe();
                try
                {
                    var sum = 0;
                    var index = 0;
                    while (index < values.Length)
                    {
                        var value = values[index];
                        if (value > 0 && value <= 10)
                        {
                            sum = checked(sum + value);
                        }

                        index++;
                    }

                    return sum;
                }
                finally
                {
                    if (probe is not null)
                    {
                        ((IDisposable)probe).Dispose();
                    }
                }
            }

            public static async Task<int> DoubleAfterYieldAsync(int value)
            {
                await Task.Yield();
                return checked(value * 2);
            }

            public static int DisposeCount { get; private set; }

            private sealed class DisposeProbe : IDisposable
            {
                public void Dispose() => DisposeCount++;
            }
        }
        """;

    public DemoMetadata Metadata { get; } = new(
        "compiler.roslyn-il",
        "compiler",
        "从高级 C#、lowering 到元数据与 CIL",
        "对同一份 Roslyn 发射的 PE 比较高级写法与教学用低层等价写法，并追踪 MethodDef token、方法体和 async 状态机。",
        [18, 19, 27],
        ["Roslyn syntax tree", "lowering", "PE metadata", "CIL evaluation stack", "async state machine", "JIT boundary"]);

    public async ValueTask RunAsync(DemoContext context, CancellationToken cancellationToken)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14);
        var syntaxTree = CSharpSyntaxTree.ParseText(
            SampleSource,
            parseOptions,
            path: "LayeredSample.cs",
            cancellationToken: cancellationToken);
        var root = syntaxTree.GetCompilationUnitRoot(cancellationToken);
        var highLevelSyntax = root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "HighLevel");

        var compilation = CSharpCompilation.Create(
            "DynamicSample",
            [syntaxTree],
            GetRuntimeReferences(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                deterministic: true));
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var highLevelSymbol = semanticModel.GetDeclaredSymbol(highLevelSyntax, cancellationToken)
            ?? throw new InvalidOperationException("Roslyn 未能为 HighLevel 创建符号。");

        context.WriteLine("[高级 C# -> 绑定 -> lowering -> PE/CLI]");
        context.WriteProperty("语言版本", parseOptions.LanguageVersion);
        context.WriteProperty("语法节点", highLevelSyntax.Kind());
        context.WriteProperty("绑定后的符号", highLevelSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat));
        context.WriteProperty("高级写法", "using var + 数组 foreach + 关系模式 + checked");
        context.WriteProperty("低层 C#", "手写的语义等价展开；它是教学模型，不是 Roslyn 输出文件");
        context.WriteProperty("Roslyn 实际边界", "内部 bound tree/rewrite 后直接发射 CIL 与元数据");

        using var peStream = new MemoryStream();
        var emitResult = compilation.Emit(peStream, cancellationToken: cancellationToken);
        var errors = emitResult.Diagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        if (!emitResult.Success)
        {
            throw new InvalidOperationException(
                $"Roslyn 发射失败：{string.Join(" | ", errors.Select(error => error.ToString()))}");
        }

        var peBytes = peStream.ToArray();
        var peEvidence = ReadPeEvidence(peBytes, "DynamicSample", "LayeredSample", "HighLevel");
        var executionEvidence = await ExecuteAndInspectAsync(peBytes).ConfigureAwait(false);

        context.WriteLine("\n[PE/CLI 封装证据：同一个 HighLevel 方法]");
        context.WriteProperty("编译诊断总数", emitResult.Diagnostics.Length);
        context.WriteProperty("PE 字节数", peBytes.Length);
        context.WriteProperty("MethodDef token", $"0x{peEvidence.MetadataToken:X8}");
        context.WriteProperty("方法 RVA", $"0x{peEvidence.RelativeVirtualAddress:X8}");
        context.WriteProperty("MaxStack", peEvidence.MaxStack);
        context.WriteProperty("InitLocals", peEvidence.LocalVariablesInitialized);
        context.WriteProperty("CIL 字节数", peEvidence.IlBytes.Length);
        context.WriteProperty("异常区域数", peEvidence.ExceptionRegionCount);
        context.WriteProperty("反射 token", $"0x{executionEvidence.HighLevelMetadataToken:X8}");
        context.WriteProperty("PE/反射 CIL 字节一致", peEvidence.IlBytes.SequenceEqual(executionEvidence.HighLevelIlBytes));

        context.WriteLine("\n[高级写法与教学用低层等价写法]");
        context.WriteProperty("输入", "[-2, 1, 3, 11]");
        context.WriteProperty("HighLevel 结果", executionEvidence.HighLevelResult);
        context.WriteProperty("LowLevelEquivalent 结果", executionEvidence.LowLevelResult);
        context.WriteProperty("Dispose 次数", executionEvidence.DisposeCount);
        context.WriteProperty("HighLevel finally", executionEvidence.HighLevelHasFinally);
        context.WriteProperty("LowLevelEquivalent finally", executionEvidence.LowLevelHasFinally);
        WriteIl(context, "高级写法 HighLevel", executionEvidence.HighLevelIl);
        WriteIl(context, "低层等价写法 LowLevelEquivalent", executionEvidence.LowLevelIl);

        context.WriteLine("\n[CIL 求值栈读法]");
        context.WriteLine("  ldarg/ldloc 把值压入抽象求值栈，比较或 add.ovf 消费操作数并压回结果，stloc 写入局部变量。");
        context.WriteLine("  分支标签描述托管控制流；求值栈不是要求 CPU 必须使用物理栈，JIT 可把值放进寄存器。");

        context.WriteLine("\n[async lowering：同一动态程序集]");
        context.WriteProperty("DoubleAfterYieldAsync(21)", executionEvidence.AsyncResult);
        context.WriteProperty("状态机实现 IAsyncStateMachine", executionEvidence.ImplementsAsyncStateMachine);
        context.WriteProperty("包含 builder 字段", executionEvidence.HasBuilderField);
        context.WriteProperty("包含 awaiter 字段", executionEvidence.HasAwaiterField);
        WriteIl(context, "async 入口 DoubleAfterYieldAsync", executionEvidence.AsyncEntryIl);
        WriteIl(context, "编译器生成的状态机 MoveNext", executionEvidence.MoveNextIl);

        context.WriteLine("\n[JIT/机器码边界]");
        context.WriteLine("  本实验停在可移植 CIL。CoreCLR 再按方法、目标 ISA、tier 与 PGO 状态生成机器码；请按学习指导使用 DOTNET_JitDisasm 观察一次具体编译。");

        DemoAssert.True(peEvidence.HasMetadata, "Roslyn 发射的 PE 应包含 CLI 元数据");
        DemoAssert.True(
            peEvidence.RelativeVirtualAddress > 0 &&
            (peEvidence.MetadataToken & unchecked((int)0xff000000)) == 0x06000000,
            "目标方法应有 MethodDef token 与有效 RVA");
        DemoAssert.True(
            peEvidence.MetadataToken == executionEvidence.HighLevelMetadataToken &&
            peEvidence.IlBytes.SequenceEqual(executionEvidence.HighLevelIlBytes),
            "PEReader 与反射必须定位到同一个方法和同一串 CIL 字节");
        DemoAssert.True(
            executionEvidence.HighLevelResult == 4 &&
            executionEvidence.LowLevelResult == 4 &&
            executionEvidence.DisposeCount == 2,
            "高级写法与低层等价写法应保持结果和释放语义");
        DemoAssert.True(
            executionEvidence.HighLevelHasFinally &&
            executionEvidence.LowLevelHasFinally &&
            executionEvidence.HighLevelIl.Any(line => line.Contains("add.ovf", StringComparison.Ordinal)),
            "两种写法都应保留 finally，且高级写法应发射 checked 加法");
        DemoAssert.True(
            executionEvidence.AsyncResult == 42 &&
            executionEvidence.ImplementsAsyncStateMachine &&
            executionEvidence.HasBuilderField &&
            executionEvidence.HasAwaiterField &&
            executionEvidence.MoveNextIl.Any(line => line.Contains("AwaitUnsafeOnCompleted", StringComparison.Ordinal)) &&
            executionEvidence.MoveNextIl.Any(line => line.Contains("SetResult", StringComparison.Ordinal)) &&
            executionEvidence.MoveNextIl.Any(line => line.Contains("SetException", StringComparison.Ordinal)) &&
            executionEvidence.MoveNextIl.Any(line => line.Contains("mul.ovf", StringComparison.Ordinal)),
            "async 状态机应包含 builder/awaiter 结构、恢复路径和 checked 乘法");
    }

    private static PortableExecutableReference[] GetRuntimeReferences()
    {
        var trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrWhiteSpace(trustedAssemblies))
        {
            throw new PlatformNotSupportedException("当前运行时未公开可信平台程序集列表。");
        }

        return trustedAssemblies
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToArray();
    }

    private static PeMethodEvidence ReadPeEvidence(
        byte[] peBytes,
        string namespaceName,
        string typeName,
        string methodName)
    {
        using var stream = new MemoryStream(peBytes, writable: false);
        using var peReader = new PEReader(stream);
        var metadataReader = peReader.GetMetadataReader();

        foreach (var typeHandle in metadataReader.TypeDefinitions)
        {
            var typeDefinition = metadataReader.GetTypeDefinition(typeHandle);
            if (metadataReader.GetString(typeDefinition.Namespace) != namespaceName ||
                metadataReader.GetString(typeDefinition.Name) != typeName)
            {
                continue;
            }

            foreach (var methodHandle in typeDefinition.GetMethods())
            {
                var methodDefinition = metadataReader.GetMethodDefinition(methodHandle);
                if (metadataReader.GetString(methodDefinition.Name) != methodName)
                {
                    continue;
                }

                var body = peReader.GetMethodBody(methodDefinition.RelativeVirtualAddress);
                return new PeMethodEvidence(
                    peReader.HasMetadata,
                    MetadataTokens.GetToken(methodHandle),
                    methodDefinition.RelativeVirtualAddress,
                    body.MaxStack,
                    body.LocalVariablesInitialized,
                    body.GetILBytes() ?? [],
                    body.ExceptionRegions.Length);
            }
        }

        throw new MissingMethodException($"{namespaceName}.{typeName}", methodName);
    }

    private static async Task<ExecutionEvidence> ExecuteAndInspectAsync(byte[] peBytes)
    {
        var loadContext = new AssemblyLoadContext("RoslynLayeringDemo", isCollectible: true);

        try
        {
            using var stream = new MemoryStream(peBytes, writable: false);
            var assembly = loadContext.LoadFromStream(stream);
            var sampleType = assembly.GetType("DynamicSample.LayeredSample", throwOnError: true)
                ?? throw new TypeLoadException("DynamicSample.LayeredSample");
            var highLevelMethod = GetRequiredMethod(sampleType, "HighLevel");
            var lowLevelMethod = GetRequiredMethod(sampleType, "LowLevelEquivalent");
            var asyncMethod = GetRequiredMethod(sampleType, "DoubleAfterYieldAsync");
            var disposeCountProperty = sampleType.GetProperty(
                "DisposeCount",
                BindingFlags.Public | BindingFlags.Static)
                ?? throw new MissingMemberException(sampleType.FullName, "DisposeCount");

            int[] values = [-2, 1, 3, 11];
            var highLevelResult = InvokeInt32(highLevelMethod, values);
            var lowLevelResult = InvokeInt32(lowLevelMethod, values);
            var disposeCount = (int)(disposeCountProperty.GetValue(null)
                ?? throw new InvalidOperationException("DisposeCount 返回了 null。"));
            var asyncTask = (Task<int>)(asyncMethod.Invoke(null, [21])
                ?? throw new InvalidOperationException("async 方法返回了 null。"));
            var asyncResult = await asyncTask.ConfigureAwait(false);

            var stateMachineType = asyncMethod.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType
                ?? throw new InvalidOperationException("async 方法没有生成 AsyncStateMachineAttribute。");
            var moveNextMethod = stateMachineType.GetMethod(
                "MoveNext",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(stateMachineType.FullName, "MoveNext");
            var stateMachineFields = stateMachineType.GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            return new ExecutionEvidence(
                highLevelResult,
                lowLevelResult,
                disposeCount,
                asyncResult,
                highLevelMethod.MetadataToken,
                highLevelMethod.GetMethodBody()?.GetILAsByteArray() ?? [],
                HasFinally(highLevelMethod),
                HasFinally(lowLevelMethod),
                typeof(IAsyncStateMachine).IsAssignableFrom(stateMachineType),
                stateMachineFields.Any(field =>
                    field.FieldType.IsGenericType &&
                    field.FieldType.GetGenericTypeDefinition() == typeof(AsyncTaskMethodBuilder<>)),
                stateMachineFields.Any(field => field.FieldType == typeof(YieldAwaitable.YieldAwaiter)),
                IlDisassembler.Disassemble(highLevelMethod, maximumInstructionCount: 96),
                IlDisassembler.Disassemble(lowLevelMethod, maximumInstructionCount: 96),
                IlDisassembler.Disassemble(asyncMethod, maximumInstructionCount: 64),
                IlDisassembler.Disassemble(moveNextMethod, maximumInstructionCount: 128));
        }
        finally
        {
            loadContext.Unload();
        }
    }

    private static MethodInfo GetRequiredMethod(Type type, string methodName) =>
        type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)
        ?? throw new MissingMethodException(type.FullName, methodName);

    private static int InvokeInt32(MethodInfo method, int[] values) =>
        (int)(method.Invoke(null, [values])
            ?? throw new InvalidOperationException($"{method.Name} 返回了 null。"));

    private static bool HasFinally(MethodBase method) =>
        method.GetMethodBody()?.ExceptionHandlingClauses.Any(clause =>
            clause.Flags == ExceptionHandlingClauseOptions.Finally) == true;

    private static void WriteIl(DemoContext context, string title, IReadOnlyList<string> lines)
    {
        context.WriteLine($"\n[CIL 指令：{title}]");
        foreach (var line in lines)
        {
            context.WriteLine($"  {line}");
        }
    }

    private sealed record PeMethodEvidence(
        bool HasMetadata,
        int MetadataToken,
        int RelativeVirtualAddress,
        int MaxStack,
        bool LocalVariablesInitialized,
        byte[] IlBytes,
        int ExceptionRegionCount);

    private sealed record ExecutionEvidence(
        int HighLevelResult,
        int LowLevelResult,
        int DisposeCount,
        int AsyncResult,
        int HighLevelMetadataToken,
        byte[] HighLevelIlBytes,
        bool HighLevelHasFinally,
        bool LowLevelHasFinally,
        bool ImplementsAsyncStateMachine,
        bool HasBuilderField,
        bool HasAwaiterField,
        IReadOnlyList<string> HighLevelIl,
        IReadOnlyList<string> LowLevelIl,
        IReadOnlyList<string> AsyncEntryIl,
        IReadOnlyList<string> MoveNextIl);
}
