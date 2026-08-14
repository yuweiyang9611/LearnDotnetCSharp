using LearnDotnetCSharp.Infrastructure;

namespace LearnDotnetCSharp.Demos.Language;

public sealed class CSharp14FeaturesDemo : IDemo
{
    private delegate bool TryConvert<T>(string text, out T result);

    public DemoMetadata Metadata { get; } = new(
        "language.csharp14",
        "language",
        "C# 14 正式语言特性",
        "以可执行断言展示扩展成员、field、null 条件赋值、Span 转换、简化 lambda 参数、partial 成员和原地复合赋值。",
        [3, 4, 24],
        ["extension blocks", "field", "Span", "partial members", "compound assignment"]);

    public ValueTask RunAsync(DemoContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IEnumerable<int> left = [1, 2, 3];
        IEnumerable<int> right = [4, 5];
        var combined = (left + right).ToArray();
        context.WriteProperty("extension property", left.Middle);
        context.WriteProperty("extension operator +", string.Join(", ", combined));
        context.WriteProperty("static extension property", IEnumerable<int>.Identity.Count());

        var label = new NormalizedLabel { Text = "  C# 14  " };
        context.WriteProperty("field-backed property", label.Text);

        OptionalTarget? absent = null;
        var rightSideEvaluations = 0;
        absent?.Value = EvaluateRightSide();

        OptionalTarget? present = new() { Value = 10 };
        present?.Value += 5;
        int[]? slots = [1, 2, 3];
        slots?[1] = 42;
        context.WriteProperty("?. assignment skipped RHS", rightSideEvaluations == 0);
        context.WriteProperty("?. compound assignment", present?.Value);
        context.WriteProperty("?[] assignment", slots?[1]);

        context.WriteProperty("nameof(List<>)", nameof(List<>));

        string[] words = ["span", "without", "copying"];
        ReadOnlySpan<object> covariantArraySpan = words;
        Span<string> writableWords = words;
        ReadOnlySpan<object> widenedSpan = writableWords;
        int[] numbers = [3, 4, 5];
        context.WriteProperty("array -> ROS<object>", covariantArraySpan.Length);
        context.WriteProperty("Span<string> -> ROS<object>", widenedSpan.Length);
        context.WriteProperty("Span extension receiver", numbers.StartsWithItem(3));

        TryConvert<int> parse = (text, out result) => int.TryParse(text, out result);
        var parsedSuccessfully = parse("14", out var parsed);
        context.WriteProperty("implicit lambda + out", $"{parsedSuccessfully}: {parsed}");

        var counter = new GeneratedStyleCounter(5);
        var observedValue = 0;
        counter.Changed += (_, eventArgs) => observedValue = eventArgs.Value;
        counter.Add(4);
        context.WriteProperty("partial ctor/event", $"{counter.Value}, observed {observedValue}");

        var accumulator = new MutableAccumulator(10);
        var originalReference = accumulator;
        accumulator += 7;
        context.WriteProperty("user-defined +=", accumulator.Total);
        context.WriteProperty("+= updated in place", ReferenceEquals(accumulator, originalReference));

        DemoAssert.SequenceEqual([1, 2, 3, 4, 5], combined, "extension operator 应连接两个序列");
        DemoAssert.Equal(2, left.Middle, "extension property 应返回中间元素");
        DemoAssert.Equal("C# 14", label.Text, "field-backed property 应修剪输入");
        DemoAssert.Equal(0, rightSideEvaluations, "null-conditional assignment 不应计算右侧");
        DemoAssert.True(present is { Value: 15 }, "null-conditional compound assignment 应更新非 null 对象");
        DemoAssert.True(slots is [_, 42, _], "null-conditional index assignment 应更新数组元素");
        DemoAssert.Equal("List", nameof(List<>), "unbound generic nameof 应返回无 arity 的名称");
        DemoAssert.True(parsedSuccessfully && parsed == 14, "简化 out lambda 参数应正确解析整数");
        DemoAssert.True(counter.Value == 9 && observedValue == 9, "partial event 应收到更新值");
        DemoAssert.True(
            accumulator.Total == 17 && ReferenceEquals(accumulator, originalReference),
            "用户定义 += 应原地更新当前实例");

        return ValueTask.CompletedTask;

        int EvaluateRightSide()
        {
            rightSideEvaluations++;
            return 99;
        }
    }
}

public static class SequenceLanguageExtensions
{
    extension<T>(IEnumerable<T> source)
    {
        public T? Middle
        {
            get
            {
                var values = source as IReadOnlyList<T> ?? source.ToArray();
                return values.Count == 0 ? default : values[values.Count / 2];
            }
        }
    }

    extension<T>(IEnumerable<T>)
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Design",
            "CA1000:Do not declare static members on generic types",
            Justification = "The static extension property is the C# 14 feature demonstrated here.")]
        public static IEnumerable<T> Identity => [];

        public static IEnumerable<T> operator +(IEnumerable<T> first, IEnumerable<T> second) =>
            first.Concat(second);
    }

    public static bool StartsWithItem<T>(this ReadOnlySpan<T> source, T expected)
        where T : IEquatable<T> =>
        !source.IsEmpty && source[0].Equals(expected);
}

public sealed class NormalizedLabel
{
    public string Text
    {
        get;
        set => field = string.IsNullOrWhiteSpace(value) ? "(empty)" : value.Trim();
    } = "(empty)";
}

public sealed class OptionalTarget
{
    public int Value { get; set; }
}

public sealed class CounterChangedEventArgs(int value) : EventArgs
{
    public int Value { get; } = value;
}

public sealed partial class GeneratedStyleCounter
{
    private EventHandler<CounterChangedEventArgs>? changedHandlers;

    public partial GeneratedStyleCounter(int initialValue);

    public partial event EventHandler<CounterChangedEventArgs>? Changed;

    public int Value { get; private set; }

    public void Add(int amount)
    {
        Value += amount;
        changedHandlers?.Invoke(this, new CounterChangedEventArgs(Value));
    }
}

public sealed partial class GeneratedStyleCounter
{
    public partial GeneratedStyleCounter(int initialValue)
    {
        Value = initialValue;
    }

    public partial event EventHandler<CounterChangedEventArgs>? Changed
    {
        add => changedHandlers += value;
        remove => changedHandlers -= value;
    }
}

public sealed class MutableAccumulator(int initialValue)
{
    public int Total { get; private set; } = initialValue;

    public void operator +=(int addend)
    {
        Total += addend;
    }
}
