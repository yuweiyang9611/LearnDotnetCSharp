using System.Dynamic;
using System.Numerics;
using LearnDotnetCSharp.Infrastructure;
using Microsoft.CSharp.RuntimeBinder;

namespace LearnDotnetCSharp.Demos.Language;

public sealed class AdvancedLanguageDemo : IDemo
{
    public DemoMetadata Metadata { get; } = new(
        "language.advanced",
        "language",
        "高级类型系统、模式与 Span",
        "组合静态抽象接口泛型数学、用封闭 record 层次模拟判别联合、模式匹配、ref struct + Span、受控 unsafe 和 dynamic 后期绑定。",
        [3, 4, 6, 7, 24],
        ["generic math", "closed record hierarchy", "pattern matching", "ref struct", "unsafe", "dynamic"]);

    public ValueTask RunAsync(DemoContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        double[] doubles = [1.5, 2.5, 8.0];
        decimal[] decimals = [1.25m, 2.50m, 3.75m];
        context.WriteProperty("generic mean<double>", Mean<double>(doubles));
        context.WriteProperty("generic mean<decimal>", Mean<decimal>(decimals));

        PipelineState[] states =
        [
            new PipelineState.Queued(2),
            new PipelineState.Running(0.65),
            new PipelineState.Completed("demo.dll"),
            new PipelineState.Failed("transient", Retryable: true),
        ];
        context.WriteProperty("record union + patterns", string.Join(" | ", states.Select(DescribeState)));

        var order = new Order("CS14", 129.00m, ["priority", "book", "digital"]);
        context.WriteProperty("property/list patterns", ClassifyOrder(order));

        var reader = new CommaTokenReader("async,span,interop");
        var tokens = new List<string>();
        while (reader.TryRead(out var token))
        {
            tokens.Add(token.ToString());
        }

        context.WriteProperty("ref struct Span scanner", string.Join(" / ", tokens));

        Span<int> stackValues = stackalloc[] { 2, 3, 5, 7 };
        var pointerSum = SumWithPinnedPointer(stackValues);
        context.WriteProperty("stackalloc + fixed pointer", pointerSum);

        dynamic module = new ExpandoObject();
        module.Language = "C#";
        module.Transform = (Func<int, int>)(value => value * value);
        string runtimeLanguage = module.Language;
        int transformed = module.Transform(7);
        context.WriteProperty("dynamic late binding", $"{runtimeLanguage}: {transformed}");

        var binderFailureObserved = false;
        try
        {
            _ = module.MissingMember;
        }
        catch (RuntimeBinderException)
        {
            binderFailureObserved = true;
        }

        context.WriteProperty("dynamic typo caught at runtime", binderFailureObserved);
        context.WriteLine("  Span 扫描本身只切片；ToString/List 用于展示时仍会分配。unsafe 应只用于经审查的互操作或性能边界。");

        DemoAssert.Equal(4.0, Mean<double>(doubles), "泛型数学应计算 double 平均值");
        DemoAssert.Equal(2.50m, Mean<decimal>(decimals), "泛型数学应计算 decimal 平均值");
        DemoAssert.Equal("priority high-value order", ClassifyOrder(order), "属性和列表模式应命中优先订单");
        DemoAssert.SequenceEqual(["async", "span", "interop"], tokens, "Span 分词器应保留三个 token");
        DemoAssert.Equal(17, pointerSum, "固定指针应读取 stackalloc 区域中的四个值");
        DemoAssert.True(
            runtimeLanguage == "C#" && transformed == 49 && binderFailureObserved,
            "dynamic 应展示成功绑定和运行时失败边界");

        return ValueTask.CompletedTask;
    }

    private static unsafe int SumWithPinnedPointer(Span<int> values)
    {
        var sum = 0;
        fixed (int* pointer = values)
        {
            for (var index = 0; index < values.Length; index++)
            {
                sum += pointer[index];
            }
        }

        return sum;
    }

    private static T Mean<T>(ReadOnlySpan<T> values)
        where T : IFloatingPoint<T>
    {
        if (values.IsEmpty)
        {
            throw new ArgumentException("At least one value is required.", nameof(values));
        }

        var total = T.Zero;
        foreach (var value in values)
        {
            total += value;
        }

        return total / T.CreateChecked(values.Length);
    }

    private static string DescribeState(PipelineState state) => state switch
    {
        PipelineState.Queued { Position: <= 0 } => "queued next",
        PipelineState.Queued { Position: var position } => $"queued #{position}",
        PipelineState.Running { Progress: >= 0 and <= 1 } running => $"running {running.Progress:P0}",
        PipelineState.Running => "invalid progress",
        PipelineState.Completed { Artifact: var artifact } => $"completed {artifact}",
        PipelineState.Failed { Retryable: true, Reason: var reason } => $"retry {reason}",
        PipelineState.Failed { Reason: var reason } => $"failed {reason}",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    private static string ClassifyOrder(Order order) => order switch
    {
        { Total: >= 100m, Tags: ["priority", ..] } => "priority high-value order",
        { Tags: [] } => "untagged order",
        { Tags: [var only] } => $"single tag: {only}",
        { Tags: [_, .., _] } => "multi-tag order",
    };
}

public abstract record PipelineState
{
    private PipelineState()
    {
    }

    public sealed record Queued(int Position) : PipelineState;

    public sealed record Running(double Progress) : PipelineState;

    public sealed record Completed(string Artifact) : PipelineState;

    public sealed record Failed(string Reason, bool Retryable) : PipelineState;
}

public sealed record Order(string Id, decimal Total, string[] Tags);

public ref struct CommaTokenReader
{
    private ReadOnlySpan<char> remaining;

    public CommaTokenReader(ReadOnlySpan<char> source)
    {
        remaining = source;
    }

    public bool TryRead(out ReadOnlySpan<char> token)
    {
        if (remaining.IsEmpty)
        {
            token = default;
            return false;
        }

        var separator = remaining.IndexOf(',');
        if (separator < 0)
        {
            token = remaining;
            remaining = default;
            return true;
        }

        token = remaining[..separator];
        remaining = remaining[(separator + 1)..];
        return true;
    }
}
