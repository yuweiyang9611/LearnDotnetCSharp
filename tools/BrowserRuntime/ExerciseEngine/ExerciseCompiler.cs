using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ExerciseEngine;

public sealed record SourceFile(string Name, string Text);
public sealed record CompilerDiagnostic(string Message, string Severity, string File, int Line, int Column);
public sealed record CompilationResult(bool Success, CompilerDiagnostic[] Diagnostics);
public sealed record CheckResult(string Name, bool Passed);
public sealed record ExecutionResult(CheckResult[] Checks, string Output, bool OutputTruncated, string? Error);

// Vendored from the design-patterns site's ExerciseEngine and extended for this course: one run compiles
// several named files (a lab plus the Infrastructure files it needs), and the entry ExerciseChecks.Run may
// return Task<string[]>, because a lab runs through IDemo.RunAsync.
public sealed class ExerciseCompiler
{
    private const int FileLimit = 100 * 1024;
    private const int TotalLimit = 512 * 1024;
    private const int OutputLimit = 64 * 1024;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private readonly List<MetadataReference> references = [];
    private byte[]? image;

    public void AddReference(byte[] bytes) => references.Add(MetadataReference.CreateFromImage(bytes));

    public CompilationResult Compile(string filesJson, string checks)
    {
        image = null;
        SourceFile[] files;
        try
        {
            files = JsonSerializer.Deserialize<SourceFile[]>(filesJson, Json) ?? [];
        }
        catch (JsonException)
        {
            return new(false, [new("源文件清单不是有效 JSON。", "Error", "", 0, 0)]);
        }
        if (files.Length == 0) return new(false, [new("没有要编译的源文件。", "Error", "", 0, 0)]);
        var total = 0;
        foreach (var file in files)
        {
            var bytes = Encoding.UTF8.GetByteCount(file.Text ?? "");
            if (bytes > FileLimit) return new(false, [new($"{file.Name} 超过 100 KB，已拒绝编译。", "Error", file.Name, 0, 0)]);
            total += bytes;
        }
        if (total > TotalLimit) return new(false, [new("源码总量不能超过 512 KB。", "Error", "", 0, 0)]);

        var options = new CSharpParseOptions(LanguageVersion.CSharp14);
        var trees = files.Select(file => CSharpSyntaxTree.ParseText(file.Text ?? "", options, file.Name))
            .Append(CSharpSyntaxTree.ParseText(checks, options, "Checks.cs"))
            .ToArray();
        var compilation = CSharpCompilation.Create("Submission_" + Guid.NewGuid().ToString("N"), trees, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, concurrentBuild: false,
                nullableContextOptions: NullableContextOptions.Enable));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        var diagnostics = result.Diagnostics
            .Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error)
            .Select(diagnostic =>
            {
                var span = diagnostic.Location.GetLineSpan();
                return new CompilerDiagnostic(diagnostic.GetMessage(), diagnostic.Severity.ToString(), span.Path ?? "",
                    span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1);
            })
            .ToArray();
        if (result.Success) image = stream.ToArray();
        return new(result.Success, diagnostics);
    }

    public async Task<ExecutionResult> ExecuteAsync()
    {
        if (image is null) return new([], "", false, "请先通过编译。");
        var output = new BoundedOutput();
        var previousOut = Console.Out;
        var previousError = Console.Error;
        try
        {
            Console.SetOut(output);
            Console.SetError(output);
            var assembly = Assembly.Load(image);
            var method = assembly.GetType("ExerciseChecks")?.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException("入口 ExerciseChecks.Run 不存在。");
            var invoked = method.Invoke(null, null) ?? throw new InvalidOperationException("入口没有返回结果。");
            var results = invoked switch
            {
                string[] array => array,
                Task<string[]> task => await task.ConfigureAwait(false),
                ValueTask<string[]> valueTask => await valueTask.ConfigureAwait(false),
                _ => throw new InvalidOperationException("入口必须返回 string[] 或 Task<string[]>。"),
            };
            return new(results.Select(line => new CheckResult(line[1..], line.StartsWith('+'))).ToArray(), output.ToString(), output.Truncated, null);
        }
        catch (Exception exception)
        {
            var cause = exception is TargetInvocationException invocation ? invocation.InnerException ?? exception : exception;
            while (cause is AggregateException { InnerExceptions.Count: 1 } aggregate) cause = aggregate.InnerExceptions[0];
            return new([], output.ToString(), output.Truncated, $"{cause.GetType().Name}: {cause.Message}");
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousError);
            image = null;
        }
    }

    private sealed class BoundedOutput : TextWriter
    {
        private readonly StringBuilder text = new();
        private int bytes;
        public bool Truncated { get; private set; }
        public override Encoding Encoding => Encoding.UTF8;
        public override void Write(char value) => Write(value.ToString());
        public override void Write(string? value)
        {
            if (value is null) return;
            foreach (var rune in value.EnumerateRunes())
            {
                if (bytes + rune.Utf8SequenceLength > OutputLimit) { Truncated = true; break; }
                bytes += rune.Utf8SequenceLength;
                text.Append(rune.ToString());
            }
        }
        public override string ToString() => text.ToString();
    }
}
