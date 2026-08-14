using LearnDotnetCSharp.Infrastructure;

namespace LearnDotnetCSharp.Demos.Language;

public sealed class DelegatesEventsIteratorsDemo : IDemo
{
    private delegate decimal PriceRule(InvoiceLine line);

    public DemoMetadata Metadata { get; } = new(
        "language.delegates-events-iterators",
        "language",
        "委托、事件、闭包、异常筛选与迭代器",
        "串联 PDF 第 4 章中的委托/Lambda、事件、异常筛选、枚举与 yield、可空值、匿名类型和命名元组。",
        [4],
        ["delegate", "event", "closure", "exception filter", "Flags", "yield", "tuple"]);

    public ValueTask RunAsync(DemoContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        decimal discount = 0.10m;
        PriceRule capturedRule = line => line.Quantity * line.UnitPrice * (1 - discount);
        PriceRule taxRule = static line => line.Quantity * line.UnitPrice * 1.05m;
        var line = new InvoiceLine("C# 14", 2, 50m);
        var discounted = capturedRule(line);
        discount = 0.20m;
        var discountedAfterMutation = capturedRule(line);

        context.WriteLine("[委托与闭包]");
        context.WriteProperty("捕获 10% 折扣", discounted);
        context.WriteProperty("外部变量改为 20%", discountedAfterMutation);
        context.WriteProperty("static lambda 不捕获", taxRule(line));

        var publisher = new BuildPublisher();
        var notifications = new List<string>();
        EventHandler<BuildCompletedEventArgs> handler = (_, args) =>
            notifications.Add($"{args.Artifact}:{args.Succeeded}");
        publisher.Completed += handler;
        publisher.Publish("demo.dll", succeeded: true);
        publisher.Completed -= handler;
        publisher.Publish("ignored.dll", succeeded: false);

        context.WriteLine("\n[事件的订阅与退订]");
        context.WriteProperty("收到通知", string.Join(", ", notifications));

        var filteredCode = "";
        try
        {
            throw new LearningException("TRANSIENT", "Temporary learning failure.");
        }
        catch (LearningException exception) when (exception.Code == "TRANSIENT")
        {
            filteredCode = exception.Code;
        }

        var fibonacci = Fibonacci(8).ToArray();
        var permissions = AccessPermissions.Read | AccessPermissions.Write;
        var anonymous = new { Language = "C#", Version = 14 };
        (string Name, int Count) summary = (anonymous.Language, fibonacci.Length);
        int? maybeCount = summary.Count;
        var nullableDescription = maybeCount is > 0 ? $"{maybeCount} values" : "empty";

        context.WriteLine("\n[异常、枚举、迭代器与轻量数据形状]");
        context.WriteProperty("exception filter", filteredCode);
        context.WriteProperty("Flags enum", permissions);
        context.WriteProperty("yield Fibonacci", string.Join(", ", fibonacci));
        context.WriteProperty("anonymous -> tuple", $"{summary.Name}: {nullableDescription}");

        DemoAssert.True(discounted == 90m && discountedAfterMutation == 80m, "闭包应读取变量的当前值而非创建时快照");
        DemoAssert.Equal(105m, taxRule(line), "static lambda 应执行不捕获的定价规则");
        DemoAssert.SequenceEqual(["demo.dll:True"], notifications, "退订后事件处理器不应再次执行");
        DemoAssert.Equal("TRANSIENT", filteredCode, "异常筛选器应只处理匹配错误码");
        DemoAssert.SequenceEqual([0, 1, 1, 2, 3, 5, 8, 13], fibonacci, "yield 迭代器应按需生成 Fibonacci 序列");
        DemoAssert.True(
            permissions.HasFlag(AccessPermissions.Write) && summary == ("C#", 8),
            "Flags、匿名类型和命名元组应保持预期数据");

        return ValueTask.CompletedTask;
    }

    private static IEnumerable<int> Fibonacci(int count)
    {
        var current = 0;
        var next = 1;

        for (var index = 0; index < count; index++)
        {
            yield return current;
            (current, next) = (next, checked(current + next));
        }
    }

    private sealed record InvoiceLine(string Name, int Quantity, decimal UnitPrice);

    private sealed class BuildPublisher
    {
        public event EventHandler<BuildCompletedEventArgs>? Completed;

        public void Publish(string artifact, bool succeeded) =>
            Completed?.Invoke(this, new BuildCompletedEventArgs(artifact, succeeded));
    }

    private sealed class BuildCompletedEventArgs(string artifact, bool succeeded) : EventArgs
    {
        public string Artifact { get; } = artifact;

        public bool Succeeded { get; } = succeeded;
    }

    private sealed class LearningException(string code, string message) : Exception(message)
    {
        public string Code { get; } = code;
    }

    [Flags]
    private enum AccessPermissions
    {
        None = 0,
        Read = 1 << 0,
        Write = 1 << 1,
        Execute = 1 << 2,
    }
}
