using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq.Expressions;
using LearnDotnetCSharp.Capstones.PluginHost;
using LearnDotnetCSharp.Infrastructure;
using LearnDotnetCSharp.PluginContract;

namespace LearnDotnetCSharp.Demos.Projects;

public sealed class CollectiblePluginHostProjectDemo : IDemo
{
    private const string ActivitySourceName = "LearnDotnetCSharp.Project.PluginHost";
    private const string MeterName = "LearnDotnetCSharp.Project.PluginHost";

    public DemoMetadata Metadata { get; } = new(
        "project.collectible-plugin-host",
        "projects",
        "综合项目：多版本、可热切换的隔离插件宿主",
        "扫描两个实现版本，按能力路由最高兼容版本；热更新后让普通异常触发隔离和回退，并验证每次执行使用的 AssemblyLoadContext 都可收集。",
        [8, 17, 18, 19, 21, 26],
        ["Attribute", "reflection", "capability routing", "hot reload", "quarantine", "fallback", "AssemblyLoadContext", "Activity", "Meter"]);

    public ValueTask RunAsync(DemoContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var v1Path = Path.Combine(
            AppContext.BaseDirectory,
            "plugins",
            "sample",
            "LearnDotnetCSharp.SamplePlugin.dll");
        var v2Path = Path.Combine(
            AppContext.BaseDirectory,
            "plugins",
            "sample-v2",
            "LearnDotnetCSharp.SamplePlugin.V2.dll");
        DemoAssert.True(File.Exists(v1Path) && File.Exists(v2Path), "两个版本的示例插件都应复制到应用输出目录");

        var stoppedActivities = 0;
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ActivitySourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = _ => Interlocked.Increment(ref stoppedActivities),
        };
        ActivitySource.AddActivityListener(activityListener);

        var routedAttempts = 0L;
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
            if (instrument.Name == "plugin.route.attempts")
            {
                Interlocked.Add(ref routedAttempts, measurement);
            }
        });
        meterListener.Start();

        using var activitySource = new ActivitySource(ActivitySourceName);
        using var meter = new Meter(MeterName);
        var attemptCounter = meter.CreateCounter<long>("plugin.route.attempts");
        var host = new CollectiblePluginHost(supportedContractVersion: 1, failuresBeforeQuarantine: 1);

        host.Refresh([v1Path], cancellationToken);
        var beforeUpdate = Invoke(host, "before update", activitySource, attemptCounter, cancellationToken);

        var refreshed = host.Refresh([v1Path, v2Path], cancellationToken);
        var afterUpdate = Invoke(host, "after update", activitySource, attemptCounter, cancellationToken);
        var fallback = Invoke(host, "fail: deterministic fallback", activitySource, attemptCounter, cancellationToken);
        var whileQuarantined = Invoke(host, "after quarantine", activitySource, attemptCounter, cancellationToken);
        host.Refresh([v1Path, v2Path], cancellationToken);
        var afterRecovery = Invoke(host, "after recovery", activitySource, attemptCounter, cancellationToken);

        Expression<Func<PluginResult, bool>> invariant = result =>
            result.PluginId == "sample.uppercase" && result.Output.Length > 0;
        var compiledInvariant = invariant.Compile();
        var allAttempts = new[] { beforeUpdate, afterUpdate, fallback, whileQuarantined, afterRecovery }
            .SelectMany(result => result.Attempts)
            .ToArray();

        context.WriteProperty("catalog", string.Join(", ", refreshed.Descriptors.Select(
            descriptor => $"{descriptor.Id}@{descriptor.ImplementationVersion}")));
        context.WriteProperty("hot switch", $"{beforeUpdate.Result?.AssemblyVersion} -> {afterUpdate.Result?.AssemblyVersion}");
        context.WriteProperty("failure route", string.Join(" -> ", fallback.Attempts.Select(
            attempt => $"v{attempt.Descriptor.ImplementationVersion.Major}:{(attempt.Succeeded ? "ok" : "failed")}")));
        context.WriteProperty("quarantine / recovery", $"{whileQuarantined.Result?.AssemblyVersion} -> {afterRecovery.Result?.AssemblyVersion}");
        context.WriteProperty("ALC collected", $"{allAttempts.Count(attempt => attempt.LoadContextCollected)}/{allAttempts.Length}");
        context.WriteProperty("telemetry", $"activities={stoppedActivities}, routed attempts={routedAttempts}");

        DemoAssert.True(beforeUpdate.Result?.AssemblyVersion.StartsWith("1.", StringComparison.Ordinal) == true, "更新前应路由 v1");
        DemoAssert.True(afterUpdate.Result is { Output: "V2::AFTER UPDATE" }, "热更新后应路由最高兼容版本 v2");
        DemoAssert.True(fallback.UsedFallback && fallback.Result?.AssemblyVersion.StartsWith("1.", StringComparison.Ordinal) == true, "v2 普通异常应被复制为字符串失败并回退 v1");
        DemoAssert.True(fallback.Attempts[0].Failure?.TypeName == typeof(InvalidOperationException).FullName, "插件异常对象不得逃出可收集边界");
        DemoAssert.True(whileQuarantined.Result?.AssemblyVersion.StartsWith("1.", StringComparison.Ordinal) == true, "被隔离的 v2 不应继续接收请求");
        DemoAssert.True(afterRecovery.Result?.AssemblyVersion.StartsWith("2.", StringComparison.Ordinal) == true, "刷新目录应建立新路由快照并解除旧隔离状态");
        DemoAssert.True(compiledInvariant(afterRecovery.Result!), "表达式树验收规则应适用于热切换后的结果");
        DemoAssert.True(allAttempts.All(attempt => attempt.LoadContextCollected), "宿主不得让 Type、Assembly、实例或异常对象钉住旧 ALC");
        DemoAssert.True(stoppedActivities == 5 && routedAttempts == 6, "五次逻辑调用应产生五个 Activity 和六次实际路由尝试");

        return ValueTask.CompletedTask;
    }

    private static PluginInvocationResult Invoke(
        CollectiblePluginHost host,
        string input,
        ActivitySource activitySource,
        Counter<long> attemptCounter,
        CancellationToken cancellationToken)
    {
        using var activity = activitySource.StartActivity("plugin.route")
            ?? throw new InvalidOperationException("插件路由 Activity 应被监听器采样。");
        var result = host.Execute("text.uppercase", input, "ja-JP", cancellationToken);
        attemptCounter.Add(result.Attempts.Count);
        activity.SetTag("plugin.attempts", result.Attempts.Count);
        activity.SetTag("plugin.fallback", result.UsedFallback);
        return result;
    }
}
