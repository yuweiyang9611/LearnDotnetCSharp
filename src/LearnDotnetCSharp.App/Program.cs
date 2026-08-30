using System.Buffers;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using LearnDotnetCSharp.Demos.Compiler;
using LearnDotnetCSharp.Infrastructure;

namespace LearnDotnetCSharp;

internal static class Program
{
    private static readonly TimeSpan SelfTestDemoTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ProcessTerminationTimeout = TimeSpan.FromSeconds(5);
    private const int MaximumCapturedOutputCharacters = 64 * 1024;

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        var catalog = DemoCatalog.Discover(Assembly.GetExecutingAssembly());
        var context = new DemoContext(Console.Out);
        var runner = new DemoRunner(context);

        try
        {
            return await ExecuteAsync(args, catalog, runner, context).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("操作已取消。");
            return DemoExitCodes.Cancelled;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"{exception.GetType().Name}: {exception.Message}");
            return DemoExitCodes.Failed;
        }
    }

    private static async Task<int> ExecuteAsync(
        string[] args,
        DemoCatalog catalog,
        DemoRunner runner,
        DemoContext context)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintHelp(context);
            return 0;
        }

        var command = args[0].ToLowerInvariant();
        if (!HasValidArgumentCount(command, args.Length))
        {
            context.WriteLine("命令或参数无效。\n");
            PrintHelp(context);
            return DemoExitCodes.Usage;
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        switch (command)
        {
            case "list":
                ListDemos(catalog, context, args.ElementAtOrDefault(1));
                return 0;

            case "list-chapter" when int.TryParse(args[1], out var chapter) &&
                                     chapter is >= 1 and <= 27:
                ListChapter(catalog, context, chapter);
                return 0;

            case "describe":
                return Describe(args[1], catalog, context);

            case "run":
                return await RunOneAsync(args[1], catalog, runner, cancellation.Token).ConfigureAwait(false);

            case "run-category":
                return await RunCategoryAsync(args[1], catalog, runner, cancellation.Token).ConfigureAwait(false);

            case "run-all":
                return await RunManyAsync(catalog.All, runner, cancellation.Token).ConfigureAwait(false);

            case "self-test":
                return await SelfTestAsync(catalog, context, cancellation.Token).ConfigureAwait(false);

            case "export-code-layers":
                await LayerArtifactExporter.ExportAsync(
                    args[1],
                    captureJitAssembly: true,
                    cancellation.Token).ConfigureAwait(false);
                context.WriteLine($"四层代码制品已写入：{Path.GetFullPath(args[1])}");
                return 0;

            case "export-code-layers-child":
                await LayerArtifactExporter.ExportAsync(
                    args[1],
                    captureJitAssembly: false,
                    cancellation.Token).ConfigureAwait(false);
                return 0;

            default:
                context.WriteLine("命令或参数无效。\n");
                PrintHelp(context);
                return DemoExitCodes.Usage;
        }
    }

    private static void ListDemos(DemoCatalog catalog, DemoContext context, string? category)
    {
        var demos = category is null
            ? catalog.All
            : catalog.All.Where(demo => string.Equals(
                demo.Metadata.Category,
                category,
                StringComparison.OrdinalIgnoreCase));

        foreach (var group in demos.GroupBy(demo => demo.Metadata.Category))
        {
            context.WriteLine($"[{group.Key}]");
            foreach (var demo in group)
            {
                context.WriteLine($"  {demo.Metadata.Id,-30} {demo.Metadata.Title}");
            }

            context.WriteLine();
        }
    }

    private static void ListChapter(DemoCatalog catalog, DemoContext context, int chapter)
    {
        context.WriteLine($"PDF 第 {chapter} 章关联示例：");
        context.WriteLine();

        foreach (var demo in catalog.All.Where(demo => demo.Metadata.PdfChapters.Contains(chapter)))
        {
            context.WriteLine($"  {demo.Metadata.Id,-30} {demo.Metadata.Title}");
        }
    }

    private static int Describe(string id, DemoCatalog catalog, DemoContext context)
    {
        if (!catalog.TryGet(id, out var demo) || demo is null)
        {
            Console.Error.WriteLine($"未找到示例 '{id}'。可运行 'list' 查看全部示例。");
            return DemoExitCodes.NotFound;
        }

        context.WriteLine($"{demo.Metadata.Id}: {demo.Metadata.Title}");
        context.WriteLine(demo.Metadata.Description);
        context.WriteProperty("Category", demo.Metadata.Category);
        context.WriteProperty("PDF chapters", string.Join(", ", demo.Metadata.PdfChapters));
        context.WriteProperty("Highlights", string.Join(", ", demo.Metadata.Highlights));
        return 0;
    }

    private static async Task<int> RunOneAsync(
        string id,
        DemoCatalog catalog,
        DemoRunner runner,
        CancellationToken cancellationToken)
    {
        if (!catalog.TryGet(id, out var demo) || demo is null)
        {
            Console.Error.WriteLine($"未找到示例 '{id}'。可运行 'list' 查看全部示例。");
            return DemoExitCodes.NotFound;
        }

        var result = await runner.RunAsync(demo, cancellationToken).ConfigureAwait(false);
        ReportFailure(demo.Metadata.Id, result);
        return DemoExitCodes.FromStatus(result.Status);
    }

    private static async Task<int> RunCategoryAsync(
        string category,
        DemoCatalog catalog,
        DemoRunner runner,
        CancellationToken cancellationToken)
    {
        var demos = catalog.All
            .Where(demo => string.Equals(demo.Metadata.Category, category, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (demos.Length == 0)
        {
            Console.Error.WriteLine($"未找到分类 '{category}'。");
            return DemoExitCodes.NotFound;
        }

        return await RunManyAsync(demos, runner, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> SelfTestAsync(
        DemoCatalog catalog,
        DemoContext context,
        CancellationToken cancellationToken)
    {
        var results = new List<IsolatedDemoResult>(catalog.All.Count);

        foreach (var demo in catalog.All)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await RunIsolatedDemoAsync(demo.Metadata.Id, cancellationToken).ConfigureAwait(false);
            results.Add(result);

            var detail = result.Status == DemoStatus.Skipped
                ? ExtractSkipReason(result.StandardOutput)
                : null;
            context.WriteLine(
                $"{GetStatusLabel(result.Status),-7} {result.Id} ({result.Duration.TotalMilliseconds:F1} ms)" +
                (detail is null ? string.Empty : $" - {detail}"));
        }

        var passed = results.Count(result => result.Status == DemoStatus.Passed);
        var skipped = results.Count(result => result.Status == DemoStatus.Skipped);
        var failed = results.Count(result => result.Status == DemoStatus.Failed);
        var timedOut = results.Count(result => result.Status == DemoStatus.Timeout);

        context.WriteLine();
        context.WriteLine(
            $"Self-test summary: passed={passed}, skipped={skipped}, failed={failed}, " +
            $"timeout={timedOut}, total={results.Count}");

        foreach (var result in results.Where(result => result.Status is DemoStatus.Failed or DemoStatus.Timeout))
        {
            context.WriteLine();
            context.WriteLine(
                $"[{GetStatusLabel(result.Status)}] {result.Id}; " +
                $"exit={(result.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "<killed>")}");
            WriteCapturedOutput(context, "stdout", result.StandardOutput);
            WriteCapturedOutput(context, "stderr", result.StandardError);
        }

        return GetSelfTestExitCode(failed, timedOut);
    }

    private static async Task<int> RunManyAsync(
        IEnumerable<IDemo> demos,
        DemoRunner runner,
        CancellationToken cancellationToken)
    {
        var results = new List<DemoResult>();
        foreach (var demo in demos)
        {
            var result = await runner.RunAsync(demo, cancellationToken).ConfigureAwait(false);
            results.Add(result);
            ReportFailure(demo.Metadata.Id, result);
        }

        if (results.Any(result => result.Status == DemoStatus.Failed))
        {
            return DemoExitCodes.Failed;
        }

        if (results.Any(result => result.Status == DemoStatus.Timeout))
        {
            return DemoExitCodes.Timeout;
        }

        return results.All(result => result.Status == DemoStatus.Skipped)
            ? DemoExitCodes.Skipped
            : DemoExitCodes.Passed;
    }

    private static async Task<IsolatedDemoResult> RunIsolatedDemoAsync(
        string id,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        ProcessStartInfo startInfo;
        try
        {
            startInfo = CreateChildStartInfo(id);
        }
        catch (Exception exception)
        {
            return new IsolatedDemoResult(
                id,
                DemoStatus.Failed,
                null,
                Stopwatch.GetElapsedTime(started),
                string.Empty,
                $"Unable to configure child process: {exception.GetType().Name}: {exception.Message}");
        }

        using var process = new Process
        {
            StartInfo = startInfo
        };
        try
        {
            if (!process.Start())
            {
                return new IsolatedDemoResult(
                    id,
                    DemoStatus.Failed,
                    null,
                    Stopwatch.GetElapsedTime(started),
                    string.Empty,
                    "Process.Start returned false.");
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or SystemException)
        {
            return new IsolatedDemoResult(
                id,
                DemoStatus.Failed,
                null,
                Stopwatch.GetElapsedTime(started),
                string.Empty,
                $"Unable to start child process: {exception.GetType().Name}: {exception.Message}");
        }

        var standardOutputTask = ReadBoundedOutputAsync(
            process.StandardOutput,
            MaximumCapturedOutputCharacters);
        var standardErrorTask = ReadBoundedOutputAsync(
            process.StandardError,
            MaximumCapturedOutputCharacters);
        try
        {
            await process.WaitForExitAsync(cancellationToken)
                .WaitAsync(SelfTestDemoTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            var killError = TryKillProcessTree(process);
            await WaitForTerminationAsync(process).ConfigureAwait(false);
            var (standardOutput, standardError) = await CaptureOutputAsync(
                    standardOutputTask,
                    standardErrorTask)
                .ConfigureAwait(false);
            if (killError is not null)
            {
                standardError = string.IsNullOrWhiteSpace(standardError)
                    ? killError
                    : $"{standardError.TrimEnd()}{Environment.NewLine}{killError}";
            }

            return new IsolatedDemoResult(
                id,
                DemoStatus.Timeout,
                process.HasExited ? process.ExitCode : null,
                Stopwatch.GetElapsedTime(started),
                standardOutput,
                standardError);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryKillProcessTree(process);
            await WaitForTerminationAsync(process).ConfigureAwait(false);
            throw;
        }

        var (capturedOutput, capturedError) = await CaptureOutputAsync(
                standardOutputTask,
                standardErrorTask)
            .ConfigureAwait(false);
        var status = DemoExitCodes.ToStatus(process.ExitCode);
        return new IsolatedDemoResult(
            id,
            status,
            process.ExitCode,
            Stopwatch.GetElapsedTime(started),
            capturedOutput,
            capturedError);
    }

    private static ProcessStartInfo CreateChildStartInfo(string id)
    {
        var processPath = Environment.ProcessPath ??
                          throw new InvalidOperationException("The current executable path is unavailable.");
        var startInfo = new ProcessStartInfo
        {
            FileName = processPath,
            WorkingDirectory = Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (string.Equals(
                Path.GetFileNameWithoutExtension(processPath),
                "dotnet",
                StringComparison.OrdinalIgnoreCase))
        {
            var entryAssemblyPath = Assembly.GetEntryAssembly()?.Location;
            if (string.IsNullOrWhiteSpace(entryAssemblyPath))
            {
                throw new InvalidOperationException("The managed entry assembly path is unavailable.");
            }

            startInfo.ArgumentList.Add(entryAssemblyPath);
        }

        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add(id);
        return startInfo;
    }

    private static string? TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            return null;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return $"Failed to kill child process tree: {exception.GetType().Name}: {exception.Message}";
        }
    }

    private static async Task WaitForTerminationAsync(Process process)
    {
        if (process.HasExited)
        {
            return;
        }

        try
        {
            await process.WaitForExitAsync(CancellationToken.None)
                .WaitAsync(ProcessTerminationTimeout)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // CaptureOutputAsync will report incomplete streams if process-tree termination failed.
        }
    }

    private static async Task<(string StandardOutput, string StandardError)> CaptureOutputAsync(
        Task<string> standardOutputTask,
        Task<string> standardErrorTask)
    {
        try
        {
            await Task.WhenAll(standardOutputTask, standardErrorTask)
                .WaitAsync(ProcessTerminationTimeout)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // Preserve whichever redirected stream completed after a failed or slow process termination.
        }
        catch (Exception) when (standardOutputTask.IsFaulted || standardErrorTask.IsFaulted)
        {
            // GetCapturedText turns each failed stream into a diagnostic marker without aborting the suite.
        }

        return (
            GetCapturedText(standardOutputTask),
            GetCapturedText(standardErrorTask));
    }

    private static string GetCapturedText(Task<string> captureTask)
    {
        if (captureTask.IsCompletedSuccessfully)
        {
            return captureTask.Result;
        }

        return captureTask.IsFaulted
            ? $"<capture failed: {captureTask.Exception?.GetBaseException().Message}>"
            : "<capture incomplete>";
    }

    internal static async Task<string> ReadBoundedOutputAsync(
        TextReader reader,
        int maximumCharacters)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCharacters, 1);

        var buffer = ArrayPool<char>.Shared.Rent(4096);
        var output = new StringBuilder(Math.Min(maximumCharacters, buffer.Length));
        var truncated = false;
        try
        {
            while (true)
            {
                var read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), CancellationToken.None)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                var remaining = maximumCharacters - output.Length;
                if (remaining > 0)
                {
                    output.Append(buffer, 0, Math.Min(read, remaining));
                }

                truncated |= read > remaining;
            }
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer, clearArray: true);
        }

        if (truncated)
        {
            output.AppendLine();
            output.Append("<output truncated after ");
            output.Append(maximumCharacters.ToString(CultureInfo.InvariantCulture));
            output.Append(" characters>");
        }

        return output.ToString();
    }

    private static void ReportFailure(string id, DemoResult result)
    {
        if (result.Status is not (DemoStatus.Failed or DemoStatus.Timeout))
        {
            return;
        }

        var error = result.Error;
        Console.Error.WriteLine(
            error is null
                ? $"{id}: {result.Message ?? result.Status.ToString()}"
                : $"{id}: {error}");
    }

    private static string GetStatusLabel(DemoStatus status) => status switch
    {
        DemoStatus.Passed => "PASS",
        DemoStatus.Skipped => "SKIP",
        DemoStatus.Failed => "FAIL",
        DemoStatus.Timeout => "TIMEOUT",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown demo status."),
    };

    private static string? ExtractSkipReason(string standardOutput)
    {
        const string prefix = "--- skipped: ";
        var skipLine = standardOutput
            .ReplaceLineEndings("\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal));

        // ReSharper disable once MergeConditionalExpression
        return skipLine is null
            ? null
            : skipLine[prefix.Length..].TrimEnd(' ', '-');
    }

    private static void WriteCapturedOutput(DemoContext context, string label, string captured)
    {
        if (string.IsNullOrWhiteSpace(captured))
        {
            return;
        }

        context.WriteLine($"  {label}:");
        foreach (var line in captured.ReplaceLineEndings("\n").Split('\n'))
        {
            context.WriteLine($"    {line}");
        }
    }

    internal static int GetSelfTestExitCode(int failed, int timedOut)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(failed);
        ArgumentOutOfRangeException.ThrowIfNegative(timedOut);

        if (failed > 0)
        {
            return DemoExitCodes.Failed;
        }

        return timedOut > 0 ? DemoExitCodes.Timeout : DemoExitCodes.Passed;
    }

    internal static bool HasValidArgumentCount(string command, int argumentCount)
    {
        ArgumentNullException.ThrowIfNull(command);

        return command.ToLowerInvariant() switch
        {
            "list" => argumentCount is 1 or 2,
            "list-chapter" or "describe" or "run" or "run-category" or
                "export-code-layers" or "export-code-layers-child" => argumentCount == 2,
            "run-all" or "self-test" => argumentCount == 1,
            _ => false,
        };
    }

    internal static bool IsHelp(string argument) =>
        string.Equals(argument, "help", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(argument, "--help", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(argument, "-h", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(argument, "/?", StringComparison.OrdinalIgnoreCase);

    private static void PrintHelp(DemoContext context)
    {
        context.WriteLine("LearnDotnetCSharp - .NET 10 / C# 14 高级特性实验室");
        context.WriteLine();
        context.WriteLine("命令：");
        context.WriteLine("  list [category]         列出全部示例或指定分类");
        context.WriteLine("  list-chapter <1..27>    按 PDF 章节列出关联示例");
        context.WriteLine("  describe <id>           查看示例目标和知识点");
        context.WriteLine("  run <id>                运行单个示例");
        context.WriteLine("  run-category <category> 运行一个分类");
        context.WriteLine("  run-all                 运行全部本地、安全示例");
        context.WriteLine("  self-test               隔离运行并验证全部示例");
        context.WriteLine("  export-code-layers <file> 生成网页使用的 C#/CIL/JIT 同源制品");
    }

    private sealed record IsolatedDemoResult(
        string Id,
        DemoStatus Status,
        int? ExitCode,
        TimeSpan Duration,
        string StandardOutput,
        string StandardError);
}
