using System.Globalization;
using LearnDotnetCSharp.FSharpInterop;
using LearnDotnetCSharp.Infrastructure;
using LearnDotnetCSharp.VisualBasicInterop;

namespace LearnDotnetCSharp.Demos.Interop;

public sealed class ManagedLanguagesInteropDemo : IDemo
{
    public DemoMetadata Metadata { get; } = new(
        "interop.managed-languages",
        "interop",
        "C# 调用 F# 与 Visual Basic",
        "通过真实项目引用调用 F# 模块函数与 Visual Basic 静态类，观察同一 CLR 类型系统上的跨语言复用。",
        [18, 25],
        ["F#", "Visual Basic", "CTS", "project reference"]);

    public ValueTask RunAsync(DemoContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var averages = Analytics.MovingAverage(3, [10.0, 13.0, 16.0, 22.0, 25.0]);
        var classification = Analytics.Classify(-8);
        var wordCounts = TextStatistics.CountWords("CLR csharp CLR fsharp csharp CLR");

        context.WriteProperty(
            "F# moving average",
            string.Join(", ", averages.Select(value => value.ToString("F1", CultureInfo.InvariantCulture))));
        context.WriteProperty("F# pattern match", classification);
        context.WriteProperty(
            "VB word counts",
            string.Join(", ", wordCounts
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key}={pair.Value}")));

        DemoAssert.SequenceEqual([13.0, 17.0, 21.0], averages, "F# 移动平均结果应正确");
        DemoAssert.Equal("negative", classification, "F# 模式匹配应识别负数");
        DemoAssert.True(
            wordCounts.Count == 3 &&
            wordCounts["CLR"] == 3 &&
            wordCounts["csharp"] == 2 &&
            wordCounts["fsharp"] == 1,
            "VB 词频统计应忽略大小写并保留计数");

        return ValueTask.CompletedTask;
    }
}
