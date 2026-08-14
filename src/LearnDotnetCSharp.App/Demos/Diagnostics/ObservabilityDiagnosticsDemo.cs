using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.Runtime.CompilerServices;
using LearnDotnetCSharp.Infrastructure;

namespace LearnDotnetCSharp.Demos.Diagnostics;

public sealed class ObservabilityDiagnosticsDemo : IDemo
{
    public DemoMetadata Metadata { get; } = new(
        "diagnostics.observability",
        "diagnostics",
        "日志、分布式追踪、指标与 EventSource",
        "在进程内采集 TraceSource、ActivitySource、Meter 和 EventSource，并用 StackTrace/Stopwatch 观察调用栈与耗时。",
        [13],
        ["TraceSource", "ActivitySource", "MeterListener", "EventSource", "StackTrace", "Stopwatch"]);

    public async ValueTask RunAsync(DemoContext context, CancellationToken cancellationToken)
    {
        using var traceWriter = new StringWriter(CultureInfo.InvariantCulture);
        using var traceListener = new TextWriterTraceListener(traceWriter);
        var traceSource = new TraceSource("LearnDotnetCSharp.Trace", SourceLevels.All);
        traceSource.Listeners.Clear();
        traceSource.Listeners.Add(traceListener);
        traceSource.TraceEvent(TraceEventType.Information, 1001, "diagnostic-start");
        traceSource.Flush();
        traceSource.Close();

        using var activitySource = new ActivitySource("LearnDotnetCSharp.Diagnostics");
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == activitySource.Name,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = static (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(activityListener);

        using var activity = activitySource.StartActivity("process-course", ActivityKind.Internal)
            ?? throw new InvalidOperationException("ActivityListener 没有创建 Activity。");
        activity.SetTag("course.id", "diagnostics");
        var traceIdBeforeAwait = Activity.Current?.TraceId;
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        var traceIdAfterAwait = Activity.Current?.TraceId;

        long counterTotal = 0;
        var histogramMeasurements = new List<double>();
        using var meter = new Meter("LearnDotnetCSharp.Metrics", "1.0.0");
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter == meter)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>(
            (_, measurement, _, _) => Interlocked.Add(ref counterTotal, measurement));
        meterListener.SetMeasurementEventCallback<double>(
            (_, measurement, _, _) => histogramMeasurements.Add(measurement));
        meterListener.Start();
        var counter = meter.CreateCounter<long>("courses.processed");
        var histogram = meter.CreateHistogram<double>("course.duration", unit: "ms");
        counter.Add(3, new KeyValuePair<string, object?>("result", "ok"));
        histogram.Record(12.5, new KeyValuePair<string, object?>("stage", "parse"));

        using var eventListener = new LearningEventListener();
        LearningEventSource.Log.CourseProcessed("diagnostics", 3);

        var stopwatch = Stopwatch.StartNew();
        var stackTrace = CaptureStackTrace();
        stopwatch.Stop();
        var stackContainsCapture = stackTrace.GetFrames()
            .Any(frame => frame.GetMethod()?.Name == nameof(CaptureStackTrace));

        context.WriteProperty("TraceSource", traceWriter.ToString().Trim());
        context.WriteProperty("Activity trace id", traceIdBeforeAwait);
        context.WriteProperty("Activity flowed across await", traceIdBeforeAwait == traceIdAfterAwait);
        context.WriteProperty("Meter counter/histogram", $"{counterTotal}/{string.Join(", ", histogramMeasurements)}");
        context.WriteProperty("EventSource events", string.Join(", ", eventListener.EventNames));
        context.WriteProperty("StackTrace captured", stackContainsCapture);
        context.WriteProperty("Stopwatch ticks", stopwatch.ElapsedTicks);

        DemoAssert.True(traceWriter.ToString().Contains("diagnostic-start", StringComparison.Ordinal), "TraceSource 应写入监听器");
        DemoAssert.True(traceIdBeforeAwait is not null && traceIdBeforeAwait == traceIdAfterAwait, "Activity.Current 应随异步执行上下文流动");
        DemoAssert.True(counterTotal == 3 && histogramMeasurements.SequenceEqual([12.5]), "MeterListener 应采集计数器和直方图测量值");
        DemoAssert.True(eventListener.EventNames.Contains("CourseProcessed", StringComparer.Ordinal), "EventListener 应接收强类型 EventSource 事件");
        DemoAssert.True(stackContainsCapture, "StackTrace 应包含禁止内联的捕获方法");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static StackTrace CaptureStackTrace() => new(fNeedFileInfo: true);
}

[EventSource(Name = "LearnDotnetCSharp-Diagnostics")]
internal sealed class LearningEventSource : EventSource
{
    public static LearningEventSource Log { get; } = new();

    [Event(1, Level = EventLevel.Informational)]
    public void CourseProcessed(string courseId, int itemCount) => WriteEvent(1, courseId, itemCount);
}

internal sealed class LearningEventListener : EventListener
{
    private readonly List<string> eventNames = [];

    public IReadOnlyList<string> EventNames => eventNames;

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        if (eventSource.Name == "LearnDotnetCSharp-Diagnostics")
        {
            EnableEvents(eventSource, EventLevel.LogAlways);
        }
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        if (eventData.EventName is { } eventName)
        {
            eventNames.Add(eventName);
        }
    }
}
