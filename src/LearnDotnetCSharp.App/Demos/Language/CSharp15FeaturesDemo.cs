using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using LearnDotnetCSharp.Infrastructure;

namespace LearnDotnetCSharp.Demos.Language;

public sealed class CSharp15FeaturesDemo : IDemo
{
    public DemoMetadata Metadata { get; } = new(
        "language.csharp15",
        "language",
        "C# 15 正式语言特性",
        "以可执行断言展示集合表达式参数、联合类型、closed 类层次、扩展索引器和带标签的 break/continue。",
        [2, 3, 4, 7],
        ["collection expression arguments", "union", "closed", "extension indexers", "labeled break/continue"]);

    public ValueTask RunAsync(DemoContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // 集合表达式参数：with(...) 必须是第一个元素，参数交给目标类型的构造函数或 Create 方法。
        string[] words = ["union", "closed", "with"];
        List<string> reserved = [with(capacity: words.Length * 2), .. words];
        HashSet<string> keywords = [with(StringComparer.OrdinalIgnoreCase), "Union", "UNION", "union"];
        ImmutableHashSet<string> tags = [with(StringComparer.OrdinalIgnoreCase), "Closed", "CLOSED", "with"];
        IList<int> buffer = [with(capacity: 16), 1, 2, 3];
        UnitReadingCollection celsius = [with("°C"), 18.5, 21.0];
        var evaluationOrder = new List<string>();
        List<int> traced = [with(capacity: Trace("with", 4)), Trace("first", 1), Trace("second", 2)];
        context.WriteProperty("with(capacity)", $"Count {reserved.Count}, Capacity {reserved.Capacity}");
        context.WriteProperty("with(comparer) HashSet", keywords.Count);
        context.WriteProperty("with -> Create builder", tags.Count);
        context.WriteProperty("IList<int> with(16)", buffer.GetType().Name);
        context.WriteProperty("with(...) required", $"{celsius.Unit}: {celsius.Count}");
        context.WriteProperty("evaluation order", string.Join(" -> ", evaluationOrder));

        // 联合类型：编译器生成实现 IUnion 的结构体，Value 保存当前情况；每个情况类型都能隐式转换进来。
        SensorReading warm = 21.5;
        SensorReading fault = new SensorFault("E42");
        SensorReading unset = default;
        context.WriteProperty("union from double", warm.Describe());
        context.WriteProperty("union from record", fault.Describe());
        context.WriteProperty("default union", unset.Describe());
        context.WriteProperty("union Value type", warm.Value?.GetType().Name);
        context.WriteProperty("union is struct/IUnion", $"{typeof(SensorReading).IsValueType}/{typeof(IUnion).IsAssignableFrom(typeof(SensorReading))}");

        // closed 类层次：只有同一程序集里的直接派生类型，switch 覆盖它们即穷尽，无需 default 分支。
        CheckoutPayment[] payments = [new CashPayment(), new CardPayment("4242"), new TransferPayment("DE89370400440532013000")];
        var paymentText = payments.Select(DescribePayment).ToArray();
        context.WriteProperty("closed switch", string.Join(" | ", paymentText));
        context.WriteProperty("null payment", DescribePayment(null));
        context.WriteProperty("closed => abstract", typeof(CheckoutPayment).IsAbstract);

        // 扩展索引器：给不属于自己的 Queue<T> 补上 this[int] 与 Slice，^1、范围和列表模式随之可用。
        Queue<int> arrivals = new([10, 20, 30, 40]);
        var window = arrivals[1..3];
        context.WriteProperty("queue[0] / queue[^1]", $"{arrivals[0]} / {arrivals[^1]}");
        context.WriteProperty("queue[1..3]", string.Join(", ", window));
        context.WriteProperty("queue is [10, .., 40]", arrivals is [10, .., 40]);

        // 带标签的 break/continue：从 switch 或内层循环直接跳出（或继续）带标签的外层循环。
        string[] script = ["load", "# comment", "run", "stop", "never"];
        var executed = new List<string>();
        commands: foreach (var command in script)
        {
            switch (command)
            {
                case ['#', ..]:
                    continue commands;
                case "stop":
                    break commands;
                default:
                    executed.Add(command);
                    break;
            }
        }

        int[][] grid = [[0, 0, 0], [0, 1, 0], [0, 0, 9], [0, 0, 0]];
        var visited = new List<string>();
        (int Row, int Column)? goal = null;
        rows: for (var row = 0; row < grid.Length; row++)
        {
            for (var column = 0; column < grid[row].Length; column++)
            {
                visited.Add($"{row}{column}");
                if (grid[row][column] == 1)
                {
                    continue rows;
                }

                if (grid[row][column] == 9)
                {
                    goal = (row, column);
                    break rows;
                }
            }
        }

        context.WriteProperty("break label in switch", string.Join(", ", executed));
        context.WriteProperty("continue/break rows", $"{string.Join(" ", visited)} -> {goal}");

        DemoAssert.True(reserved is { Count: 3, Capacity: 6 }, "with(capacity:) 应把容量交给 List<T> 构造函数");
        DemoAssert.True(
            keywords.Count == 1 && ReferenceEquals(keywords.Comparer, StringComparer.OrdinalIgnoreCase),
            "with(comparer) 应让 HashSet 按忽略大小写的比较器去重");
        DemoAssert.True(
            tags.Count == 2 && ReferenceEquals(tags.KeyComparer, StringComparer.OrdinalIgnoreCase),
            "with(...) 参数应排在 CollectionBuilder 的 ReadOnlySpan 参数之前");
        DemoAssert.True(buffer is List<int> { Capacity: 16, Count: 3 }, "IList<T> 目标的 with(capacity) 应创建 List<T>");
        DemoAssert.True(celsius is { Unit: "°C", Count: 2 }, "没有无参构造函数的集合类型应通过 with(...) 构造");
        DemoAssert.SequenceEqual(["with", "first", "second"], evaluationOrder, "with(...) 参数应先于元素求值");
        DemoAssert.True(traced is { Capacity: 4, Count: 2 }, "带副作用的 with 参数应只求值一次");

        DemoAssert.True(warm.Value is 21.5, "double 情况应装箱保存在 Value 中");
        DemoAssert.True(fault is SensorFault { Code: "E42" }, "带类型的属性模式应检查联合的内容");
        DemoAssert.True(unset.Value is null && unset.Describe() == "无读数（default）", "default 联合的内容应为 null");
        DemoAssert.True(
            typeof(SensorReading).IsValueType &&
            typeof(IUnion).IsAssignableFrom(typeof(SensorReading)) &&
            typeof(SensorReading).IsDefined(typeof(UnionAttribute), inherit: false),
            "union 声明应生成带 [Union] 并实现 IUnion 的结构体");
        DemoAssert.Equal(2, typeof(SensorReading).GetConstructors().Length, "每个情况类型应各有一个公共构造函数");

        DemoAssert.SequenceEqual(
            ["现金", "卡 *4242", "转账 DE89…"],
            paymentText,
            "closed 层次的 switch 应覆盖全部直接派生类型");
        DemoAssert.Equal("未选择", DescribePayment(null), "可空的 closed 类型仍需 null 分支");
        DemoAssert.True(
            typeof(CheckoutPayment).IsAbstract && typeof(CheckoutPayment).IsDefined(typeof(IsClosedTypeAttribute), inherit: false),
            "closed 类应隐式 abstract 并在元数据中带 IsClosedTypeAttribute");

        DemoAssert.True(arrivals[0] == 10 && arrivals[^1] == 40, "扩展索引器应支持正向和 ^ 索引");
        DemoAssert.SequenceEqual([20, 30], window, "扩展 Slice 应支持范围访问");
        DemoAssert.True(arrivals is [10, .., 40] && arrivals.Count == 4, "列表模式应可用且索引不应出队");

        DemoAssert.SequenceEqual(["load", "run"], executed, "continue/break 标签应作用于 foreach 而不是 switch");
        DemoAssert.SequenceEqual(
            ["00", "01", "02", "10", "11", "20", "21", "22"],
            visited,
            "continue rows 应跳过本行剩余单元格，break rows 应结束全部循环");
        DemoAssert.Equal((2, 2), goal, "break rows 应停在第一个目标单元格");

        return ValueTask.CompletedTask;

        int Trace(string step, int value)
        {
            evaluationOrder.Add(step);
            return value;
        }

        static string DescribePayment(CheckoutPayment? payment) => payment switch
        {
            null => "未选择",
            CashPayment => "现金",
            CardPayment(var last4) => $"卡 *{last4}",
            TransferPayment { Iban: var iban } => $"转账 {iban[..4]}…",
        };
    }
}

public sealed class UnitReadingCollection(string unit) : Collection<double>
{
    public string Unit { get; } = unit;
}

public sealed record class SensorFault(string Code);

public union SensorReading(double, SensorFault)
{
    public string Describe() => this switch
    {
        double celsius => string.Create(CultureInfo.InvariantCulture, $"{celsius:0.0} °C"),
        SensorFault { Code: var code } => $"故障 {code}",
        null => "无读数（default）",
    };
}

public closed record class CheckoutPayment;

public sealed record class CashPayment : CheckoutPayment;

public sealed record class CardPayment(string Last4) : CheckoutPayment;

public sealed record class TransferPayment(string Iban) : CheckoutPayment;

public static class QueueIndexExtensions
{
    extension<T>(Queue<T> queue)
    {
        public T this[int index] => queue.ElementAt(index);

        public T[] Slice(int start, int length) => [.. queue.Skip(start).Take(length)];
    }
}
