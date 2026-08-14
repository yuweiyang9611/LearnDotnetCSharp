using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using LearnDotnetCSharp.Demos.Interop;
using LearnDotnetCSharp.Infrastructure;
using CapstonePython = LearnDotnetCSharp.Capstones.Polyglot;

namespace LearnDotnetCSharp.Demos.Projects;

public sealed class PolyglotComputeProjectDemo : IDemo
{
    public DemoMetadata Metadata { get; } = new(
        "project.polyglot-compute",
        "projects",
        "综合项目：可自愈的 C#、Python、C 与 C++ 计算编排",
        "用有界 Channel 调度批次，让双 Python 常驻工作池、C ABI 和 C++ SafeHandle 处理同一数据；Python 进程确定性崩溃后自动重建并重试幂等请求。",
        [14, 17, 22, 25],
        ["Python venv", "persistent worker pool", "JSON Lines", "crash recovery", "bounded Channel", "C ABI", "C++ opaque handle", "SafeHandle", "unsafe", "exactly-once"]);

    public DemoAvailability Availability =>
        OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64
            ? DemoAvailability.Supported
            : DemoAvailability.Skip("The complete polyglot workflow requires the Windows x64 C and C++ artifacts.");

    public async ValueTask RunAsync(DemoContext context, CancellationToken cancellationToken)
    {
        var root = WorkspaceLocator.FindRoot();
        var interpreter = PythonProcessInteropDemo.GetInterpreterPath(root);
        var worker = Path.Combine(root, "python", "interop_worker.py");
        DemoAssert.True(File.Exists(interpreter), "跨语言项目必须使用工作区 .venv 解释器");
        DemoAssert.True(File.Exists(worker), "Python JSON worker 应存在");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        await using var pythonPool = await CapstonePython.PythonWorkerPool.StartAsync(
            new CapstonePython.PythonWorkerPoolOptions(interpreter, worker, root)
            {
                WorkerCount = 2,
                QueueCapacity = 4,
                RequestTimeout = TimeSpan.FromSeconds(4),
                MaxCrashRetries = 1,
            },
            timeout.Token).ConfigureAwait(false);
        var channel = Channel.CreateBounded<PolyglotBatch>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = true,
            SingleReader = false,
        });
        var results = new ConcurrentDictionary<string, PolyglotJobResult>(StringComparer.Ordinal);
        PolyglotBatch[] batches =
        [
            new("batch-a", [1, 2, 3]),
            new("batch-b", [4, 5]),
            new("batch-c", [7, 8, 9]),
            new("batch-d", [10, 11]),
        ];

        var producer = ProduceAsync(channel.Writer, batches, timeout);
        var consumers = Enumerable.Range(0, 2)
            .Select(_ => ConsumeAsync(
                channel.Reader,
                results,
                pythonPool,
                timeout))
            .ToArray();

        await Task.WhenAll(consumers.Append(producer)).ConfigureAwait(false);
        var poolSnapshot = pythonPool.Snapshot;

        var ordered = results.Values.OrderBy(result => result.Id, StringComparer.Ordinal).ToArray();
        var report = new PolyglotProjectReport(ordered.Length, ordered);
        var json = JsonSerializer.Serialize(report, PolyglotProjectJsonContext.Default.PolyglotProjectReport);
        var roundTrip = JsonSerializer.Deserialize(json, PolyglotProjectJsonContext.Default.PolyglotProjectReport)
            ?? throw new JsonException("跨语言项目报告反序列化为 null。");

        foreach (var result in ordered)
        {
            context.WriteProperty(
                result.Id,
                $"Python pid/gen={result.PythonProcessId}/{result.PythonGeneration}, sum={result.PythonSum}; C sum={result.CSum}; C++ history=[{string.Join(", ", result.CppHistory)}]");
        }
        context.WriteProperty("workers / jobs", $"{poolSnapshot.ActiveProcessIds.Count} / {report.JobCount}");
        context.WriteProperty("Python starts / restarts", $"{poolSnapshot.Starts} / {poolSnapshot.Restarts}");
        context.WriteProperty("observed Python PIDs", string.Join(", ", poolSnapshot.ObservedProcessIds));
        context.WriteProperty("JSON bytes", System.Text.Encoding.UTF8.GetByteCount(json));

        DemoAssert.Equal(batches.Length, report.JobCount, "所有批次都应完成");
        DemoAssert.SequenceEqual(batches.Select(batch => batch.Id), ordered.Select(result => result.Id), "批次 ID 应恰好处理一次");
        DemoAssert.True(ordered.All(result => result.PythonInVenv), "每个 Python 阶段都必须运行在工作区 venv");
        DemoAssert.True(
            poolSnapshot is { Starts: 3, Restarts: 1 } &&
            poolSnapshot.ActiveProcessIds.Count == 2 &&
            poolSnapshot.ObservedProcessIds.Count == 3,
            "两个常驻 worker 中应有一个确定性崩溃、重建并继续处理请求");
        DemoAssert.True(
            ordered.All(result =>
                result.PythonCount == result.CCount &&
                Math.Abs(result.PythonSum - result.CSum) < 1e-12 &&
                Math.Abs(result.PythonMean - result.CMean) < 1e-12),
            "Python 与 C 必须对同一批数据给出相同统计结果");
        DemoAssert.True(
            ordered.All(result => result.CppHistory[^1] == result.CSum && result.HandleClosed),
            "C++ 不透明对象应得到相同总和，并由 SafeHandle 关闭");
        DemoAssert.True(
            roundTrip.JobCount == report.JobCount &&
            roundTrip.Jobs.Select(job => job.Id).SequenceEqual(report.Jobs.Select(job => job.Id)),
            "源生成 JSON 应保留跨语言报告的作业边界");
    }

    private static async Task ProduceAsync(
        ChannelWriter<PolyglotBatch> writer,
        IEnumerable<PolyglotBatch> batches,
        CancellationTokenSource owner)
    {
        Exception? completionException = null;
        try
        {
            foreach (var batch in batches)
            {
                await writer.WriteAsync(batch, owner.Token).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            completionException = exception;
            await owner.CancelAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            writer.TryComplete(completionException);
        }
    }

    private static async Task ConsumeAsync(
        ChannelReader<PolyglotBatch> reader,
        ConcurrentDictionary<string, PolyglotJobResult> results,
        CapstonePython.PythonWorkerPool pythonPool,
        CancellationTokenSource owner)
    {
        try
        {
            await foreach (var batch in reader.ReadAllAsync(owner.Token).ConfigureAwait(false))
            {
                var result = await ProcessBatchAsync(
                    batch,
                    pythonPool,
                    owner.Token).ConfigureAwait(false);
                DemoAssert.True(results.TryAdd(batch.Id, result), "每个跨语言批次只能提交一次结果");
            }
        }
        catch
        {
            await owner.CancelAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<PolyglotJobResult> ProcessBatchAsync(
        PolyglotBatch batch,
        CapstonePython.PythonWorkerPool pythonPool,
        CancellationToken cancellationToken)
    {
        var doubles = batch.Values.Select(value => (double)value).ToArray();
        var pythonResponse = await pythonPool.AnalyzeAsync(
            new CapstonePython.PythonAnalyzeRequest(
                batch.Id,
                $"C# orchestrates {batch.Id}",
                doubles,
                CrashOnFirstAttempt: batch.Id == "batch-a"),
            cancellationToken).ConfigureAwait(false);
        DemoAssert.True(pythonResponse.Ok && pythonResponse.Result is not null, $"{batch.Id} 的 Python 阶段应成功");
        var python = pythonResponse.Result!;

        var (cStatus, cStatistics) = AnalyzeWithC(doubles);
        DemoAssert.Equal(0, cStatus, $"{batch.Id} 的 C ABI 阶段应成功");

        var (history, handleClosed) = AccumulateWithCpp(batch);

        var expectedHistory = new int[batch.Values.Length + 1];
        for (var index = 0; index < batch.Values.Length; index++)
        {
            expectedHistory[index + 1] = expectedHistory[index] + batch.Values[index];
        }
        DemoAssert.SequenceEqual(expectedHistory, history, $"{batch.Id} 的 C++ history 应是逐项前缀和");

        return new PolyglotJobResult(
            batch.Id,
            python.Count,
            python.Sum,
            python.Mean,
            pythonResponse.Runtime.InVenv,
            pythonResponse.Runtime.ProcessId,
            pythonResponse.Runtime.Generation,
            cStatistics.Count,
            cStatistics.Sum,
            cStatistics.Mean,
            history,
            handleClosed);
    }

    private static unsafe (int Status, CStatistics Statistics) AnalyzeWithC(double[] values)
    {
        CStatistics statistics;
        int status;
        fixed (double* valuesPointer = values)
        {
            status = LearnC.learn_c_analyze_f64(valuesPointer, values.Length, out statistics);
        }

        return (status, statistics);
    }

    private static unsafe (int[] History, bool HandleClosed) AccumulateWithCpp(PolyglotBatch batch)
    {
        var createStatus = LearnCpp.learn_cpp_counter_create(0, out var nativeHandle);
        DemoAssert.True(createStatus == 0 && nativeHandle != 0, $"{batch.Id} 应创建 C++ 不透明句柄");
        var counter = new CppCounterHandle(nativeHandle);
        int[] history;
        try
        {
            foreach (var value in batch.Values)
            {
                var addStatus = LearnCpp.learn_cpp_counter_add(counter, value, out _);
                DemoAssert.Equal(0, addStatus, $"{batch.Id} 的 C++ 累加应成功");
            }

            var historyBuffer = new int[batch.Values.Length + 1];
            int historyStatus;
            int written;
            fixed (int* historyPointer = historyBuffer)
            {
                historyStatus = LearnCpp.learn_cpp_counter_copy_history(
                    counter,
                    historyPointer,
                    historyBuffer.Length,
                    out written);
            }

            DemoAssert.Equal(0, historyStatus, $"{batch.Id} 应复制 C++ vector 历史");
            history = historyBuffer.AsSpan(0, written).ToArray();
        }
        finally
        {
            counter.Dispose();
        }

        return (history, counter.IsClosed);
    }
}

internal sealed record PolyglotBatch(string Id, int[] Values);

internal sealed record PolyglotJobResult(
    string Id,
    int PythonCount,
    double PythonSum,
    double PythonMean,
    bool PythonInVenv,
    int PythonProcessId,
    int PythonGeneration,
    int CCount,
    double CSum,
    double CMean,
    int[] CppHistory,
    bool HandleClosed);

internal sealed record PolyglotProjectReport(int JobCount, PolyglotJobResult[] Jobs);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PolyglotProjectReport))]
internal sealed partial class PolyglotProjectJsonContext : JsonSerializerContext;
