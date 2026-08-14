using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LearnDotnetCSharp.Infrastructure;

namespace LearnDotnetCSharp.Demos.Interop;

public sealed class PythonProcessInteropDemo : IDemo
{
    public DemoMetadata Metadata { get; } = new(
        "interop.python-process-json",
        "interop",
        "C# 与工作区 Python venv 的进程互操作",
        "从 .venv 启动隔离 Python 解释器，以逐行 JSON 协议交换 Unicode、数组、运行时信息和统计结果，并处理超时与退出码。",
        [17, 25],
        ["Python venv", "Process", "stdin/stdout", "JSON Lines", "UTF-8", "cancellation"]);

    public async ValueTask RunAsync(DemoContext context, CancellationToken cancellationToken)
    {
        var root = WorkspaceLocator.FindRoot();
        var interpreter = GetInterpreterPath(root);
        var worker = Path.Combine(root, "python", "interop_worker.py");
        DemoAssert.True(File.Exists(interpreter), "工作区 Python 解释器应存在；请先运行 scripts\\setup-python.cmd");
        DemoAssert.True(File.Exists(worker), "Python JSON worker 应位于仓库 python 目录");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));

        var request = new PythonAnalyzeRequest(
            "request-1",
            1,
            "analyze",
            "C# 调用 Python：こんにちは",
            [1.5, 2.5, 10.0]);
        var response = await ExchangeAsync(interpreter, worker, root, request, timeout.Token)
            .ConfigureAwait(false);

        context.WriteProperty("Python", response.Runtime.Version);
        context.WriteProperty("venv interpreter", response.Runtime.Executable);
        context.WriteProperty("isolated venv", response.Runtime.InVenv);
        context.WriteProperty("Unicode echo", response.Echo);
        context.WriteProperty(
            "Python statistics",
            $"count={response.Result?.Count}, sum={response.Result?.Sum}, mean={response.Result?.Mean:F6}, median={response.Result?.Median}");

        DemoAssert.True(response.Ok && response.Error is null, "Python worker 应返回成功响应");
        DemoAssert.True(response.Runtime.InVenv, "Python worker 必须运行在工作区 .venv 中");
        DemoAssert.True(PathsEqual(interpreter, response.Runtime.Executable), "Python 报告的解释器应与工作区 .venv 一致");
        DemoAssert.Equal(request.Label, response.Echo, "逐行 JSON 协议应无损往返 Unicode 文本");
        DemoAssert.True(
            response.Result is { Count: 3, Sum: 14.0, Median: 2.5 } result &&
            Math.Abs(result.Mean - (14.0 / 3.0)) < 1e-12,
            "Python statistics 应返回确定的 count/sum/mean/median");
    }

    internal static async Task<PythonAnalyzeResponse> ExchangeAsync(
        string interpreter,
        string worker,
        string root,
        PythonAnalyzeRequest request,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = interpreter,
            WorkingDirectory = root,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-I");
        startInfo.ArgumentList.Add("-u");
        startInfo.ArgumentList.Add(worker);
        startInfo.Environment["PYTHONUTF8"] = "1";
        startInfo.Environment["PYTHONDONTWRITEBYTECODE"] = "1";

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("无法启动工作区 Python 解释器。");
        }

        try
        {
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            var requestJson = JsonSerializer.Serialize(request, PythonInteropJsonContext.Default.PythonAnalyzeRequest);
            await process.StandardInput.WriteLineAsync(requestJson.AsMemory(), cancellationToken).ConfigureAwait(false);
            await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);

            var responseJson = await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            process.StandardInput.Close();
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Python worker 退出码为 {process.ExitCode}：{stderr.Trim()}");
            }

            if (responseJson is null)
            {
                throw new InvalidOperationException($"Python worker 没有返回 JSON：{stderr.Trim()}");
            }

            return JsonSerializer.Deserialize(
                       responseJson,
                       PythonInteropJsonContext.Default.PythonAnalyzeResponse) ??
                   throw new JsonException("Python worker 返回了 JSON null。");
        }
        catch
        {
            TryTerminate(process);
            throw;
        }
    }

    internal static string GetInterpreterPath(string root) =>
        OperatingSystem.IsWindows()
            ? Path.Combine(root, ".venv", "Scripts", "python.exe")
            : Path.Combine(root, ".venv", "bin", "python");

    private static bool PathsEqual(string expected, string actual) =>
        string.Equals(
            Path.GetFullPath(expected),
            Path.GetFullPath(actual),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // 清理失败不能覆盖原始的协议、超时或进程异常。
        }
    }
}

public sealed record PythonAnalyzeRequest(
    string Id,
    int ProtocolVersion,
    string Operation,
    string Label,
    double[] Values);

public sealed record PythonAnalyzeResponse(
    string? Id,
    bool Ok,
    string? Echo,
    PythonRuntimeInfo Runtime,
    PythonStatistics? Result,
    string? Error);

public sealed record PythonRuntimeInfo(
    string Version,
    string Executable,
    string Prefix,
    string BasePrefix,
    bool InVenv);

public sealed record PythonStatistics(int Count, double Sum, double Mean, double Median);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PythonAnalyzeRequest))]
[JsonSerializable(typeof(PythonAnalyzeResponse))]
public sealed partial class PythonInteropJsonContext : JsonSerializerContext;
