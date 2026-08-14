using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using LearnDotnetCSharp.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace LearnDotnetCSharp.Demos.Compiler;

public sealed class IncrementalGeneratorAnalyzerDemo : IDemo
{
    private const string SampleSource = """
        using System.Threading.Tasks;

        namespace GeneratedSample;

        public partial class Greeter
        {
        }

        public sealed class BlockingUsage
        {
            public int Read(Task<int> task) => task.Result;
        }
        """;

    public DemoMetadata Metadata { get; } = new(
        "compiler.incremental-generator-analyzer",
        "compiler",
        "增量源生成器与诊断分析器",
        "在内存中运行 IIncrementalGenerator 和 DiagnosticAnalyzer，验证生成源码、编译产物与 Task.Result 诊断。",
        [13, 19, 27],
        ["IIncrementalGenerator", "syntax provider", "source generation", "DiagnosticAnalyzer", "semantic model"]);

    public async ValueTask RunAsync(DemoContext context, CancellationToken cancellationToken)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14);
        var syntaxTree = CSharpSyntaxTree.ParseText(
            SampleSource,
            parseOptions,
            path: "GeneratorInput.cs",
            cancellationToken: cancellationToken);
        var compilation = CSharpCompilation.Create(
            "GeneratedSample",
            [syntaxTree],
            GetRuntimeReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new GreetingGenerator().AsSourceGenerator()],
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var updatedCompilation,
            out var generatorDiagnostics,
            cancellationToken);

        var runResult = driver.GetRunResult();
        var generatedTree = runResult.GeneratedTrees.Single();
        var generatedSource = generatedTree.GetText(cancellationToken).ToString();
        var generatorErrors = generatorDiagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        using var peStream = new MemoryStream();
        var emitResult = updatedCompilation.Emit(peStream, cancellationToken: cancellationToken);
        var emitErrors = emitResult.Diagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        if (!emitResult.Success)
        {
            throw new InvalidOperationException(
                $"生成后的编译失败：{string.Join(" | ", emitErrors.Select(error => error.ToString()))}");
        }

        var analyzerDiagnostics = await updatedCompilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new BlockingTaskResultAnalyzer()))
            .GetAnalyzerDiagnosticsAsync(cancellationToken)
            .ConfigureAwait(false);
        var blockingDiagnostics = analyzerDiagnostics
            .Where(diagnostic => diagnostic.Id == BlockingTaskResultAnalyzer.DiagnosticId)
            .ToArray();

        var greeting = ExecuteGeneratedAssembly(peStream);
        var diagnosticLine = blockingDiagnostics.Single().Location.GetLineSpan().StartLinePosition.Line + 1;

        context.WriteProperty("输入语法树", compilation.SyntaxTrees.Length);
        context.WriteProperty("生成语法树", runResult.GeneratedTrees.Length);
        context.WriteProperty("生成文件", Path.GetFileName(generatedTree.FilePath));
        context.WriteProperty("生成属性值", greeting);
        context.WriteProperty("生成器错误", generatorErrors.Length);
        context.WriteProperty("分析器诊断", $"{BlockingTaskResultAnalyzer.DiagnosticId} at line {diagnosticLine}");
        context.WriteLine("  生成器负责补充编译输入；分析器只报告问题，不应悄悄改变用户代码。");

        DemoAssert.True(generatedSource.Contains("Greeting", StringComparison.Ordinal), "生成源码应包含 Greeting 属性");
        DemoAssert.Equal("hello from generator", greeting, "生成后的程序集应可执行并返回生成值");
        DemoAssert.Equal(0, generatorErrors.Length, "增量生成器不应产生错误诊断");
        DemoAssert.Equal(1, blockingDiagnostics.Length, "分析器应精确报告一次 Task<TResult>.Result");
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

    private static string ExecuteGeneratedAssembly(Stream peStream)
    {
        peStream.Position = 0;
        var loadContext = new AssemblyLoadContext("IncrementalGeneratorDemo", isCollectible: true);
        try
        {
            var assembly = loadContext.LoadFromStream(peStream);
            var greeter = assembly.GetType("GeneratedSample.Greeter", throwOnError: true)
                ?? throw new TypeLoadException("GeneratedSample.Greeter");
            var instance = Activator.CreateInstance(greeter)
                ?? throw new InvalidOperationException("无法创建生成类型实例。");
            var property = greeter.GetProperty("Greeting", BindingFlags.Instance | BindingFlags.Public)
                ?? throw new MissingMemberException(greeter.FullName, "Greeting");
            return (string)(property.GetValue(instance)
                ?? throw new InvalidOperationException("生成属性返回了 null。"));
        }
        finally
        {
            loadContext.Unload();
        }
    }

    private sealed class GreetingGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var targets = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax
                {
                    Identifier.ValueText: "Greeter",
                } declaration && declaration.Modifiers.Any(SyntaxKind.PartialKeyword),
                static (syntaxContext, _) =>
                    ((ClassDeclarationSyntax)syntaxContext.Node).Identifier.ValueText);

            context.RegisterSourceOutput(targets, static (productionContext, className) =>
            {
                var source = $$"""
                    namespace GeneratedSample;

                    public partial class {{className}}
                    {
                        public string Greeting => "hello from generator";
                    }
                    """;
                productionContext.AddSource($"{className}.Greeting.g.cs", SourceText.From(source, Encoding.UTF8));
            });
        }
    }

    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    private sealed class BlockingTaskResultAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "LDCS001";

        private static readonly DiagnosticDescriptor Rule = new(
            DiagnosticId,
            "Avoid synchronously reading Task.Result",
            "Await this task instead of synchronously reading Result",
            "Async",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: "Blocking on Task<TResult>.Result can cause starvation or a context deadlock.");

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
        }

        private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
        {
            var memberAccess = (MemberAccessExpressionSyntax)context.Node;
            if (!string.Equals(memberAccess.Name.Identifier.ValueText, "Result", StringComparison.Ordinal) ||
                context.SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken).Symbol is not IPropertySymbol property ||
                !property.ContainingType.IsGenericType ||
                !string.Equals(property.ContainingType.Name, "Task", StringComparison.Ordinal) ||
                !string.Equals(property.ContainingNamespace.ToDisplayString(), "System.Threading.Tasks", StringComparison.Ordinal))
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(Rule, memberAccess.Name.GetLocation()));
        }
    }
}
