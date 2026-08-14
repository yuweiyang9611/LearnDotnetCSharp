using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using LearnDotnetCSharp.Infrastructure;

namespace LearnDotnetCSharp.Demos.Runtime;

public sealed class RuntimeOverviewDemo : IDemo
{
    public DemoMetadata Metadata { get; } = new(
        "runtime.overview",
        "runtime",
        ".NET 运行时概览",
        "观察 CLR、GC、JIT/动态代码以及当前进程架构，建立后续实验的运行时基线。",
        [1, 5, 13],
        ["CLR", "JIT", "GC", "RuntimeFeature"]);

    public ValueTask RunAsync(DemoContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        context.WriteProperty("Framework", RuntimeInformation.FrameworkDescription);
        context.WriteProperty("Runtime version", Environment.Version);
        context.WriteProperty("OS", RuntimeInformation.OSDescription);
        context.WriteProperty("Process architecture", RuntimeInformation.ProcessArchitecture);
        context.WriteProperty("Server GC", GCSettings.IsServerGC);
        context.WriteProperty("GC latency mode", GCSettings.LatencyMode);
        context.WriteProperty("Dynamic code supported", RuntimeFeature.IsDynamicCodeSupported);
        context.WriteProperty("Dynamic code compiled", RuntimeFeature.IsDynamicCodeCompiled);

        return ValueTask.CompletedTask;
    }
}
