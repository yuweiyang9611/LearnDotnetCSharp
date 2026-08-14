using System.Globalization;
using LearnDotnetCSharp.Infrastructure;

namespace LearnDotnetCSharp.Demos.Linq;

public sealed class LinqOperatorsDemo : IDemo
{
    public DemoMetadata Metadata { get; } = new(
        "linq.operators",
        "linq",
        "LINQ 运算符全景与 .NET 10 外连接",
        "覆盖筛选、映射、扁平化、连接、排序、分组、集合、分块、元素、量词与聚合，并使用 .NET 10 LeftJoin/RightJoin。",
        [9],
        ["SelectMany", "LeftJoin", "RightJoin", "CountBy", "AggregateBy", "DistinctBy", "Chunk", "Aggregate"]);

    public ValueTask RunAsync(DemoContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        LinqCustomer[] customers =
        [
            new(1, "Ada"),
            new(2, "Grace"),
            new(3, "Linus"),
        ];
        LinqOrder[] orders =
        [
            new(101, 1, "paid", 120m, ["compiler", "book"]),
            new(102, 1, "pending", 80m, ["linq"]),
            new(103, 2, "paid", 200m, ["navy", "runtime"]),
            new(104, 99, "failed", 50m, ["orphan"]),
        ];

        var highValueNames = orders
            .Where(order => order.Total >= 100m)
            .OrderByDescending(order => order.Total)
            .Select(order => order.Id)
            .ToArray();
        var flattenedTags = orders.SelectMany(order => order.Tags).Distinct(StringComparer.Ordinal).Order().ToArray();
        var chunks = orders.Chunk(2).Select(chunk => string.Join("+", chunk.Select(order => order.Id))).ToArray();

        context.WriteLine("[筛选、映射、扁平化与分块]");
        context.WriteProperty("high value", string.Join(", ", highValueNames));
        context.WriteProperty("SelectMany tags", string.Join(", ", flattenedTags));
        context.WriteProperty("Chunk(2)", string.Join(" | ", chunks));

        var leftJoin = orders
            .LeftJoin(
                customers,
                order => order.CustomerId,
                customer => customer.Id,
                (order, customer) => $"{order.Id}:{customer?.Name ?? "<missing>"}")
            .ToArray();
        var rightJoin = orders
            .RightJoin(
                customers,
                order => order.CustomerId,
                customer => customer.Id,
                (order, customer) => $"{customer.Name}:{order?.Id.ToString(CultureInfo.InvariantCulture) ?? "<none>"}")
            .ToArray();

        context.WriteLine("\n[连接]");
        context.WriteProperty("LeftJoin", string.Join(", ", leftJoin));
        context.WriteProperty("RightJoin", string.Join(", ", rightJoin));

        var statusCounts = orders.CountBy(order => order.Status).ToDictionary();
        var totalsByCustomer = orders
            .AggregateBy(order => order.CustomerId, 0m, (total, order) => total + order.Total)
            .ToDictionary();
        var setDifference = orders.Select(order => order.CustomerId).Distinct().Except(customers.Select(customer => customer.Id)).ToArray();
        var aggregate = orders.Aggregate(
            (Count: 0, Total: 0m),
            (state, order) => (state.Count + 1, state.Total + order.Total));
        var zip = customers.Select(customer => customer.Name).Zip(["A", "G", "L"]).ToArray();

        context.WriteLine("\n[分组聚合、集合、元素与量词]");
        context.WriteProperty("CountBy", string.Join(", ", statusCounts.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}={pair.Value}")));
        context.WriteProperty("AggregateBy Ada", totalsByCustomer[1]);
        context.WriteProperty("orphan customer ids", string.Join(", ", setDifference));
        context.WriteProperty("Aggregate count/total", $"{aggregate.Count}/{aggregate.Total}");
        context.WriteProperty("Zip", string.Join(", ", zip.Select(pair => $"{pair.First}-{pair.Second}")));

        DemoAssert.SequenceEqual([103, 101], highValueNames, "筛选、排序和映射应产生确定顺序");
        DemoAssert.True(leftJoin[^1] == "104:<missing>" && rightJoin[^1] == "Linus:<none>", ".NET 10 外连接应保留无匹配一侧");
        DemoAssert.True(statusCounts["paid"] == 2 && totalsByCustomer[1] == 200m, "CountBy 和 AggregateBy 应直接按键聚合");
        DemoAssert.SequenceEqual([99], setDifference, "集合差应找出没有客户记录的外键");
        DemoAssert.True(
            aggregate == (4, 450m) && orders.Any(order => order.Status == "failed") && orders.All(order => order.Total > 0),
            "通用聚合与量词应保持订单不变量");

        return ValueTask.CompletedTask;
    }

    private sealed record LinqCustomer(int Id, string Name);

    private sealed record LinqOrder(int Id, int CustomerId, string Status, decimal Total, string[] Tags);
}
