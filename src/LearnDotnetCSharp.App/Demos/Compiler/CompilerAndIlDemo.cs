using System.Reflection;
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
        namespace DynamicSample;

        public static class Calculator
        {
            public static int Twice(int value) => value * 2;
        }
        """;

    public DemoMetadata Metadata { get; } = new(
        "compiler.roslyn-il",
        "compiler",
        "Roslyn 编译管线与可读 IL",
        "从 C# 14 语法树和语义模型发射内存程序集，并反汇编普通方法、async 入口及状态机 MoveNext。",
        [18, 19, 27],
        ["Roslyn syntax tree", "semantic model", "emit", "IL opcodes", "async state machine"]);

    public async ValueTask RunAsync(DemoContext context, CancellationToken cancellationToken)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14);
        var syntaxTree = CSharpSyntaxTree.ParseText(
            SampleSource,
            parseOptions,
            cancellationToken: cancellationToken);
        var root = syntaxTree.GetCompilationUnitRoot(cancellationToken);
        var methodSyntax = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();

        var coreLibraryPath = typeof(object).Assembly.Location;
        if (string.IsNullOrWhiteSpace(coreLibraryPath))
        {
            throw new PlatformNotSupportedException("当前部署未暴露核心库路径，无法创建 Roslyn MetadataReference。");
        }

        var compilation = CSharpCompilation.Create(
            "DynamicSample",
            [syntaxTree],
            [MetadataReference.CreateFromFile(coreLibraryPath)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var methodSymbol = semanticModel.GetDeclaredSymbol(methodSyntax, cancellationToken)
            ?? throw new InvalidOperationException("Roslyn 未能为示例方法创建符号。");

        context.WriteLine("[C# -> 语法树 -> 语义模型 -> PE]");
        context.WriteProperty("语言版本", parseOptions.LanguageVersion);
        context.WriteProperty("语法节点", methodSyntax.Kind());
        context.WriteProperty("方法标识符", methodSyntax.Identifier.ValueText);
        context.WriteProperty("符号", methodSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat));
        context.WriteProperty("返回类型", methodSymbol.ReturnType.SpecialType);
        context.WriteProperty("引用范围", "当前极简样例只引用运行中 core library；通用编译器应使用目标框架 reference assemblies");

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

        context.WriteProperty("编译诊断总数", emitResult.Diagnostics.Length);
        context.WriteProperty("PE 字节数", peStream.Length);
        var emittedResult = ExecuteEmittedAssembly(peStream);
        context.WriteProperty("内存程序集 Twice(21)", emittedResult);

        var ordinaryMethod = typeof(CompilerAndIlDemo).GetMethod(
            nameof(SquarePlusOne),
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(CompilerAndIlDemo).FullName, nameof(SquarePlusOne));
        var asyncMethod = typeof(CompilerAndIlDemo).GetMethod(
            nameof(DoubleAfterYieldAsync),
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(CompilerAndIlDemo).FullName, nameof(DoubleAfterYieldAsync));

        var ordinaryResult = SquarePlusOne(6);
        var asyncResult = await DoubleAfterYieldAsync(21).ConfigureAwait(false);
        context.WriteProperty("普通方法执行结果", ordinaryResult);
        context.WriteProperty("async 方法执行结果", asyncResult);

        var ordinaryIl = WriteIl(context, "普通方法 SquarePlusOne", ordinaryMethod, maximumInstructionCount: 32);
        var asyncEntryIl = WriteIl(context, "async 入口 DoubleAfterYieldAsync", asyncMethod, maximumInstructionCount: 48);

        var stateMachineType = asyncMethod.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType
            ?? throw new InvalidOperationException("async 方法没有生成 AsyncStateMachineAttribute。");
        var moveNextMethod = stateMachineType.GetMethod(
            "MoveNext",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(stateMachineType.FullName, "MoveNext");
        var moveNextIl = WriteIl(context, "编译器生成的状态机 MoveNext", moveNextMethod, maximumInstructionCount: 72);

        DemoAssert.Equal(42, emittedResult, "Roslyn 发射程序集中的 Twice(21) 应返回 42");
        DemoAssert.True(ordinaryResult == 37 && asyncResult == 42, "普通方法和 async 方法执行结果应正确");
        DemoAssert.True(
            ordinaryIl.Any(line => line.Contains("mul.ovf", StringComparison.Ordinal)) &&
            asyncEntryIl.Count > 1 &&
            moveNextIl.Count > 1,
            "反汇编结果应包含 checked 乘法和 async 状态机 IL");
    }

    private static int ExecuteEmittedAssembly(Stream peStream)
    {
        peStream.Position = 0;
        var loadContext = new AssemblyLoadContext("RoslynCompilerDemo", isCollectible: true);

        try
        {
            var assembly = loadContext.LoadFromStream(peStream);
            var calculator = assembly.GetType("DynamicSample.Calculator", throwOnError: true)
                ?? throw new TypeLoadException("DynamicSample.Calculator");
            var twice = calculator.GetMethod("Twice", BindingFlags.Public | BindingFlags.Static)
                ?? throw new MissingMethodException(calculator.FullName, "Twice");

            return (int)(twice.Invoke(null, [21])
                ?? throw new InvalidOperationException("动态方法返回了 null。"));
        }
        finally
        {
            loadContext.Unload();
        }
    }

    private static IReadOnlyList<string> WriteIl(
        DemoContext context,
        string title,
        MethodBase method,
        int maximumInstructionCount)
    {
        context.WriteLine($"\n[IL：{title}]");
        var lines = IlDisassembler.Disassemble(method, maximumInstructionCount);
        foreach (var line in lines)
        {
            context.WriteLine($"  {line}");
        }

        return lines;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int SquarePlusOne(int value) => checked((value * value) + 1);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<int> DoubleAfterYieldAsync(int value)
    {
        await Task.Yield();
        return value * 2;
    }
}
