using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using LearnDotnetCSharp.Capstones.DataPipeline;
using LearnDotnetCSharp.Infrastructure;

namespace LearnDotnetCSharp.Demos.Projects;

public sealed partial class CancellableDataPipelineProjectDemo : IDemo
{
    private const string ActivitySourceName = "LearnDotnetCSharp.Project.DataPipeline";
    private const string MeterName = "LearnDotnetCSharp.Project.DataPipeline";
    private const string InterruptionFault = "data-pipeline.before-checkpoint";

    public DemoMetadata Metadata { get; } = new(
        "project.cancellable-data-pipeline",
        "projects",
        "综合项目：带 checkpoint、死信与恢复的数据管线",
        "以 UTF-8 字节偏移 checkpoint、SQLite 增量事务状态、显式 NDJSON 死信导出和有界连续提交窗口 组合出 at-least-once 管线；确定性中断后从连续前缀恢复。",
        [8, 11, 13, 14, 15, 22, 26],
        ["UTF-8 byte offsets", "checkpoint", "dead-letter", "bounded Channel", "cancellation", "resume", "idempotent sink", "Activity", "Meter"]);

    public async ValueTask RunAsync(DemoContext context, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var stateDirectory = Path.Combine(Path.GetTempPath(), $"learn-dotnet-pipeline-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stateDirectory);
        var sourcePath = Path.Combine(stateDirectory, "jobs.ndjson");
        var statePath = Path.Combine(stateDirectory, "checkpoint.db");
        await File.WriteAllTextAsync(
            sourcePath,
            "job-001|alpha|10\njob-002|beta|7\ninvalid-record\njob-003|alpha|5\njob-004|beta|11\njob-005|gamma|3\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            timeout.Token).ConfigureAwait(false);

        var activityTraceIds = new ConcurrentBag<ActivityTraceId>();
        var stoppedActivities = 0;
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ActivitySourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = _ => Interlocked.Increment(ref stoppedActivities),
        };
        ActivitySource.AddActivityListener(activityListener);

        var acceptedMeasurements = 0L;
        var rejectedMeasurements = 0L;
        var durationMeasurements = 0;
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == MeterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, measurement, _, _) =>
        {
            if (instrument.Name == "pipeline.records.accepted")
            {
                Interlocked.Add(ref acceptedMeasurements, measurement);
            }
            else if (instrument.Name == "pipeline.records.rejected")
            {
                Interlocked.Add(ref rejectedMeasurements, measurement);
            }
        });
        meterListener.SetMeasurementEventCallback<int>((instrument, _, _, _) =>
        {
            if (instrument.Name == "pipeline.duration")
            {
                Interlocked.Increment(ref durationMeasurements);
            }
        });
        meterListener.Start();

        using var activitySource = new ActivitySource(ActivitySourceName);
        using var meter = new Meter(MeterName);
        var acceptedCounter = meter.CreateCounter<long>("pipeline.records.accepted");
        var rejectedCounter = meter.CreateCounter<long>("pipeline.records.rejected");
        var durationHistogram = meter.CreateHistogram<int>("pipeline.duration", unit: "ms");
        using var rootActivity = activitySource.StartActivity("pipeline.run")
            ?? throw new InvalidOperationException("数据管线根 Activity 应被监听器采样。");
        var stateStore = new SqlitePipelineStateStore(statePath);
        var processed = new ConcurrentDictionary<int, PipelineWorkItem>();
        var faultPlan = FaultPlan.FailOn(InterruptionFault, invocation: 1);

        PipelineParseResult<PipelineWorkItem> Parse(ReadOnlyMemory<byte> bytes)
        {
            var result = ParseRecord(bytes);
            if (!result.IsSuccess)
            {
                rejectedCounter.Add(1);
            }

            return result;
        }

        async ValueTask ProcessAsync(PipelineRecord<PipelineWorkItem> record, CancellationToken token)
        {
            var item = record.Value;
            if (item.Id == 3 && faultPlan.ShouldInject(InterruptionFault))
            {
                throw new PipelineInterruptedException("在 sink 成功前确定性中断，用于验证 checkpoint 恢复。");
            }

            using var activity = activitySource.StartActivity(
                "pipeline.process",
                ActivityKind.Internal,
                rootActivity.Context,
                tags: [new("job.id", item.Id), new("job.group", item.Group)])
                ?? throw new InvalidOperationException("工作项 Activity 应被监听器采样。");
            DemoAssert.True(processed.TryAdd(item.Id, item), "幂等 sink 不得重复提交同一业务 ID");
            activityTraceIds.Add(activity.TraceId);
            acceptedCounter.Add(1, new KeyValuePair<string, object?>("group", item.Group));
            durationHistogram.Record(item.Value, new KeyValuePair<string, object?>("group", item.Group));
            await Task.Yield();
            token.ThrowIfCancellationRequested();
        }

        try
        {
            var interrupted = new ResumableDataPipeline<PipelineWorkItem>(
                stateStore,
                Parse,
                ProcessAsync,
                new ResumableDataPipelineOptions
                {
                    ChannelCapacity = 2,
                    ConsumerCount = 1,
                    ReadBufferBytes = 7,
                    MaximumFrameBytes = 128,
                });
            try
            {
                await interrupted.RunAsync(sourcePath, timeout.Token).ConfigureAwait(false);
                throw new InvalidOperationException("首轮管线应在确定性故障点中断。");
            }
            catch (PipelineInterruptedException)
            {
                // checkpoint 已经覆盖故障前的连续终态帧；使用一个全新 runner 模拟进程恢复。
            }

            var fingerprint = await PipelineSourceFingerprint.ComputeAsync(sourcePath, timeout.Token)
                .ConfigureAwait(false);
            var interruptedState = await stateStore.InitializeAsync(fingerprint, timeout.Token)
                .ConfigureAwait(false);
            var resumedFromSequence = interruptedState.Checkpoint.LastContiguousSequence;
            var resumed = new ResumableDataPipeline<PipelineWorkItem>(
                new SqlitePipelineStateStore(statePath),
                Parse,
                ProcessAsync,
                new ResumableDataPipelineOptions
                {
                    ChannelCapacity = 2,
                    ConsumerCount = 3,
                    ReadBufferBytes = 11,
                    MaximumFrameBytes = 128,
                });
            var resumedResult = await resumed.RunAsync(sourcePath, timeout.Token).ConfigureAwait(false);

            var ordered = processed.Values.OrderBy(item => item.Id).ToArray();
            var totals = ordered
                .GroupBy(item => item.Group, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Sum(item => item.Value), StringComparer.Ordinal);
            var report = new DataPipelineReport(
                ordered.Length,
                resumedResult.TotalDeadLetters,
                ordered.Sum(item => item.Value),
                totals,
                ordered.Select(item => item.Id).ToArray(),
                resumedFromSequence,
                resumedResult.ProcessedThisRun);
            var json = JsonSerializer.Serialize(report, DataPipelineProjectJsonContext.Default.DataPipelineReport);
            var roundTrip = JsonSerializer.Deserialize(json, DataPipelineProjectJsonContext.Default.DataPipelineReport)
                ?? throw new JsonException("数据管线报告反序列化为 null。");
            await stateStore.ExportDeadLettersAsync(stateStore.DeadLetterPath, timeout.Token).ConfigureAwait(false);
            var deadLetterLines = await File.ReadAllLinesAsync(stateStore.DeadLetterPath, timeout.Token)
                .ConfigureAwait(false);
            rootActivity.Stop();

            context.WriteProperty("accepted / dead-letter", $"{report.Accepted} / {report.Rejected}");
            context.WriteProperty("recovery point", $"sequence={report.ResumedFromSequence}, resumed records={report.ResumedRecords}");
            context.WriteProperty("idempotent sink IDs", string.Join(", ", report.ProcessedIds));
            context.WriteProperty("group totals", string.Join(", ", report.GroupTotals.Select(pair => $"{pair.Key}={pair.Value}")));
            context.WriteProperty("durable state", $"offset={resumedResult.Checkpoint.NextByteOffset}, DLQ lines={deadLetterLines.Length}");
            context.WriteProperty("telemetry", $"accepted={acceptedMeasurements}, rejected={rejectedMeasurements}, durations={durationMeasurements}, activities={stoppedActivities}");
            context.WriteProperty("JSON report", json);

            DemoAssert.True(report is { Accepted: 5, Rejected: 1, Total: 36, ResumedFromSequence: 3, ResumedRecords: 3 }, "恢复后应得到五条合法记录、一条死信和三个续跑工作项");
            DemoAssert.SequenceEqual([1, 2, 3, 4, 5], report.ProcessedIds, "幂等 sink 应只保存每个业务 ID 一次");
            DemoAssert.True(report.GroupTotals is { Count: 3 } && report.GroupTotals["alpha"] == 15 && report.GroupTotals["beta"] == 18 && report.GroupTotals["gamma"] == 3, "分组汇总应保持确定性");
            DemoAssert.True(deadLetterLines.Length == 1 && deadLetterLines[0].Contains("invalid-record", StringComparison.Ordinal), "非法帧跨恢复只能进入一次死信投影");
            DemoAssert.Equal(new FileInfo(sourcePath).Length, resumedResult.Checkpoint.NextByteOffset, "最终 checkpoint 应到达 UTF-8 源末尾");
            DemoAssert.True(roundTrip.ProcessedIds.SequenceEqual(report.ProcessedIds), "源生成 JSON 应无损往返恢复报告");
            DemoAssert.True(acceptedMeasurements == 5 && rejectedMeasurements == 1 && durationMeasurements == 5, "Meter 应与最终业务结果一致");
            DemoAssert.True(activityTraceIds.Count == 5 && activityTraceIds.All(id => id == rootActivity.TraceId), "恢复前后的 Activity 都应继承同一根 trace");
            DemoAssert.Equal(6, stoppedActivities, "根 Activity 和五个成功工作项 Activity 都应停止");
        }
        finally
        {
            if (Directory.Exists(stateDirectory))
            {
                Directory.Delete(stateDirectory, recursive: true);
            }
        }
    }

    private static PipelineParseResult<PipelineWorkItem> ParseRecord(ReadOnlyMemory<byte> bytes)
    {
        var text = Encoding.UTF8.GetString(bytes.Span);
        var match = RecordPattern().Match(text);
        if (!match.Success)
        {
            return PipelineParse.Reject<PipelineWorkItem>("record does not match job-NNN|group|value");
        }

        return PipelineParse.Success(new PipelineWorkItem(
            int.Parse(match.Groups["id"].ValueSpan, CultureInfo.InvariantCulture),
            match.Groups["group"].Value,
            int.Parse(match.Groups["value"].ValueSpan, CultureInfo.InvariantCulture)));
    }

    [GeneratedRegex(
        "^job-(?<id>[0-9]{3})\\|(?<group>[a-z]+)\\|(?<value>[0-9]+)$",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 100)]
    private static partial System.Text.RegularExpressions.Regex RecordPattern();
}

internal sealed class PipelineInterruptedException(string message) : Exception(message);

internal sealed record PipelineWorkItem(int Id, string Group, int Value);

internal sealed record DataPipelineReport(
    int Accepted,
    int Rejected,
    int Total,
    Dictionary<string, int> GroupTotals,
    int[] ProcessedIds,
    long ResumedFromSequence,
    int ResumedRecords);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DataPipelineReport))]
internal sealed partial class DataPipelineProjectJsonContext : JsonSerializerContext;
