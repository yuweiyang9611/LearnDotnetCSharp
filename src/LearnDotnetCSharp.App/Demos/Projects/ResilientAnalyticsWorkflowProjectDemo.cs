using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LearnDotnetCSharp.Capstones.DataPipeline;
using LearnDotnetCSharp.Capstones.LocalService;
using LearnDotnetCSharp.Capstones.PluginHost;
using LearnDotnetCSharp.Capstones.Polyglot;
using LearnDotnetCSharp.Demos.Interop;
using LearnDotnetCSharp.Infrastructure;

namespace LearnDotnetCSharp.Demos.Projects;

public sealed class ResilientAnalyticsWorkflowProjectDemo : IDemo
{
    private const string PipelineFault = "analytics.pipeline.before-checkpoint";

    public DemoMetadata Metadata { get; } = new(
        "project.resilient-analytics-workflow",
        "projects",
        "综合项目：可恢复的跨语言分析工作流",
        "把可恢复文件管线、Python 常驻工作池、可收集多版本插件和 SQLite 幂等存储串成一个跨平台流程，并连续验证两种崩溃与重开数据库后的重放。",
        [8, 11, 13, 14, 15, 17, 18, 21, 22, 25, 26],
        ["checkpoint", "dead-letter", "Python worker pool", "crash retry", "capability routing", "collectible ALC", "SQLite idempotency", "restart replay"]);

    public async ValueTask RunAsync(DemoContext context, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var root = WorkspaceLocator.FindRoot();
        var interpreter = PythonProcessInteropDemo.GetInterpreterPath(root);
        var workerScript = Path.Combine(root, "python", "interop_worker.py");
        var stateDirectory = Path.Combine(Path.GetTempPath(), $"learn-dotnet-analytics-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stateDirectory);
        var sourcePath = Path.Combine(stateDirectory, "metrics.txt");
        var checkpointPath = Path.Combine(stateDirectory, "pipeline-state.json");
        var databasePath = Path.Combine(stateDirectory, "requests.db");
        await File.WriteAllTextAsync(
            sourcePath,
            "metric-001|alpha|4\ninvalid-metric\nmetric-002|beta|7\nmetric-003|alpha|11\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            timeout.Token).ConfigureAwait(false);

        await using var pythonPool = await PythonWorkerPool.StartAsync(
            new PythonWorkerPoolOptions(interpreter, workerScript, root)
            {
                WorkerCount = 2,
                QueueCapacity = 4,
                RequestTimeout = TimeSpan.FromSeconds(4),
                MaxCrashRetries = 1,
            },
            timeout.Token).ConfigureAwait(false);
        var pluginHost = new CollectiblePluginHost();
        var v1Path = Path.Combine(AppContext.BaseDirectory, "plugins", "sample", "LearnDotnetCSharp.SamplePlugin.dll");
        var v2Path = Path.Combine(AppContext.BaseDirectory, "plugins", "sample-v2", "LearnDotnetCSharp.SamplePlugin.V2.dll");
        pluginHost.Refresh([v1Path, v2Path], timeout.Token);

        var committedMetrics = new ConcurrentDictionary<int, AnalyticsMetric>();
        var faultPlan = FaultPlan.FailOn(PipelineFault, invocation: 1);
        var workflowExecutions = 0;

        async ValueTask<byte[]> ExecuteWorkflowAsync(CancellationToken token)
        {
            Interlocked.Increment(ref workflowExecutions);
            var pipeline = new ResumableDataPipeline<AnalyticsMetric>(
                new JsonPipelineStateStore(checkpointPath),
                ParseMetric,
                (record, _) =>
                {
                    if (record.Value.Id == 2 && faultPlan.ShouldInject(PipelineFault))
                    {
                        return ValueTask.FromException(new AnalyticsWorkflowInterruptedException());
                    }

                    if (!committedMetrics.TryAdd(record.Value.Id, record.Value))
                    {
                        throw new InvalidOperationException($"Metric {record.Value.Id} was committed more than once.");
                    }

                    return ValueTask.CompletedTask;
                },
                new ResumableDataPipelineOptions
                {
                    ChannelCapacity = 2,
                    ConsumerCount = 1,
                    ReadBufferBytes = 9,
                    MaximumFrameBytes = 128,
                });
            var pipelineResult = await pipeline.RunAsync(sourcePath, token).ConfigureAwait(false);
            var ordered = committedMetrics.Values.OrderBy(metric => metric.Id).ToArray();
            var python = await pythonPool.AnalyzeAsync(
                new LearnDotnetCSharp.Capstones.Polyglot.PythonAnalyzeRequest(
                    "analytics-summary",
                    "resilient analytics workflow",
                    ordered.Select(metric => (double)metric.Value).ToArray(),
                    CrashOnFirstAttempt: true),
                token).ConfigureAwait(false);
            if (!python.Ok || python.Result is null)
            {
                throw new InvalidOperationException(python.Error ?? "Python analysis failed.");
            }

            var groupTotals = ordered
                .GroupBy(metric => metric.Group, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => $"{group.Key}={group.Sum(metric => metric.Value)}");
            var plugin = pluginHost.Execute(
                "text.uppercase",
                $"sum={python.Result.Sum}; {string.Join(", ", groupTotals)}",
                "ja-JP",
                token);
            if (!plugin.Succeeded)
            {
                throw new InvalidOperationException("No compatible analytics formatting plug-in succeeded.");
            }

            var report = new AnalyticsWorkflowReport(
                ordered.Length,
                pipelineResult.TotalDeadLetters,
                python.Result.Sum,
                python.Result.Mean,
                python.Runtime.ProcessId,
                python.Runtime.Generation,
                plugin.Result!.Output,
                plugin.Result.AssemblyVersion,
                pipelineResult.Checkpoint.LastContiguousSequence,
                plugin.Attempts.All(attempt => attempt.LoadContextCollected));
            return JsonSerializer.SerializeToUtf8Bytes(
                report,
                AnalyticsWorkflowJsonContext.Default.AnalyticsWorkflowReport);
        }

        try
        {
            var sourceBytes = await File.ReadAllBytesAsync(sourcePath, timeout.Token).ConfigureAwait(false);
            var requestHash = Convert.ToHexString(SHA256.HashData(sourceBytes));
            IdempotencyResult completed;
            await using (var firstStore = new SqliteIdempotencyStore(databasePath))
            {
                await firstStore.InitializeAsync(timeout.Token).ConfigureAwait(false);
                try
                {
                    await firstStore.ExecuteAsync(
                        "analytics-request-42",
                        requestHash,
                        ExecuteWorkflowAsync,
                        timeout.Token).ConfigureAwait(false);
                    throw new InvalidOperationException("The first workflow execution should have been interrupted.");
                }
                catch (AnalyticsWorkflowInterruptedException)
                {
                    // The store persists no completed response; the pipeline owns its independent checkpoint.
                }

                completed = await firstStore.ExecuteAsync(
                    "analytics-request-42",
                    requestHash,
                    ExecuteWorkflowAsync,
                    timeout.Token).ConfigureAwait(false);
            }

            var replayFactoryCalls = 0;
            IdempotencyResult replayed;
            await using (var reopenedStore = new SqliteIdempotencyStore(databasePath))
            {
                await reopenedStore.InitializeAsync(timeout.Token).ConfigureAwait(false);
                replayed = await reopenedStore.ExecuteAsync(
                    "analytics-request-42",
                    requestHash,
                    _ =>
                    {
                        Interlocked.Increment(ref replayFactoryCalls);
                        return ValueTask.FromResult<byte[]>([0]);
                    },
                    timeout.Token).ConfigureAwait(false);
            }

            var report = JsonSerializer.Deserialize(
                completed.Payload,
                AnalyticsWorkflowJsonContext.Default.AnalyticsWorkflowReport)
                ?? throw new JsonException("The analytics workflow report was JSON null.");
            var poolSnapshot = pythonPool.Snapshot;
            var deadLetterLines = await File.ReadAllLinesAsync(
                new JsonPipelineStateStore(checkpointPath).DeadLetterPath,
                timeout.Token).ConfigureAwait(false);

            context.WriteProperty("workflow executions", $"interrupted + resumed = {workflowExecutions}");
            context.WriteProperty("pipeline", $"accepted={report.Accepted}, dead-letter={report.DeadLetters}, checkpoint={report.CheckpointSequence}");
            context.WriteProperty("Python recovery", $"pid/gen={report.PythonProcessId}/{report.PythonGeneration}, starts/restarts={poolSnapshot.Starts}/{poolSnapshot.Restarts}");
            context.WriteProperty("plugin", $"version={report.PluginVersion}, ALC collected={report.PluginLoadContextCollected}");
            context.WriteProperty("durable replay", $"{completed.Outcome} -> reopen -> {replayed.Outcome}, bytes={completed.Payload?.Length}");
            context.WriteProperty("final output", report.FormattedOutput);

            DemoAssert.True(workflowExecutions == 2 && replayFactoryCalls == 0, "第一次中断、第二次恢复，重开数据库后不得再次运行工厂");
            DemoAssert.True(completed.Outcome == IdempotencyOutcome.Executed && replayed.Outcome == IdempotencyOutcome.Replayed, "SQLite 应持久化并重放最终响应");
            DemoAssert.SequenceEqual(completed.Payload!, replayed.Payload!, "重启重放必须保留逐字节相同的响应");
            DemoAssert.True(report is { Accepted: 3, DeadLetters: 1, Sum: 22, CheckpointSequence: 4 }, "恢复后的管线和 Python 汇总应得到确定结果");
            DemoAssert.True(committedMetrics.Count == 3 && deadLetterLines.Length == 1, "幂等 sink 和死信投影都不得重复提交");
            DemoAssert.True(poolSnapshot is { Starts: 3, Restarts: 1 }, "Python 分析请求崩溃后应由常驻池重建并重试一次");
            DemoAssert.True(report.PluginVersion.StartsWith("2.", StringComparison.Ordinal) && report.FormattedOutput.StartsWith("V2::", StringComparison.Ordinal), "能力路由应选择最高兼容插件版本");
            DemoAssert.True(report.PluginLoadContextCollected, "最终插件执行上下文应可收集");
        }
        finally
        {
            if (Directory.Exists(stateDirectory))
            {
                Directory.Delete(stateDirectory, recursive: true);
            }
        }
    }

    private static PipelineParseResult<AnalyticsMetric> ParseMetric(ReadOnlyMemory<byte> bytes)
    {
        var text = Encoding.UTF8.GetString(bytes.Span);
        var fields = text.Split('|');
        if (fields is not [var idText, var group, var valueText] ||
            !idText.StartsWith("metric-", StringComparison.Ordinal) ||
            !int.TryParse(idText.AsSpan("metric-".Length), NumberStyles.None, CultureInfo.InvariantCulture, out var id) ||
            !int.TryParse(valueText, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ||
            string.IsNullOrWhiteSpace(group))
        {
            return PipelineParse.Reject<AnalyticsMetric>("expected metric-NNN|group|value");
        }

        return PipelineParse.Success(new AnalyticsMetric(id, group, value));
    }
}

internal sealed class AnalyticsWorkflowInterruptedException : Exception;

internal sealed record AnalyticsMetric(int Id, string Group, int Value);

internal sealed record AnalyticsWorkflowReport(
    int Accepted,
    int DeadLetters,
    double Sum,
    double Mean,
    int PythonProcessId,
    int PythonGeneration,
    string FormattedOutput,
    string PluginVersion,
    long CheckpointSequence,
    bool PluginLoadContextCollected);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AnalyticsWorkflowReport))]
internal sealed partial class AnalyticsWorkflowJsonContext : JsonSerializerContext;
