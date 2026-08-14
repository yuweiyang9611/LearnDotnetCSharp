using System.Linq.Expressions;
using LearnDotnetCSharp.Infrastructure;

namespace LearnDotnetCSharp.Demos.Linq;

public sealed class LinqQuerySemanticsDemo : IDemo
{
    public DemoMetadata Metadata { get; } = new(
        "linq.query-semantics",
        "linq",
        "LINQ 查询语法、延迟执行与表达式树",
        "用 query syntax、let/join/subquery 展示组合查询，并对比 IEnumerable 委托管线、物化快照和 IQueryable 表达式树。",
        [8],
        ["query syntax", "deferred execution", "materialization", "subquery", "IQueryable", "Expression"]);

    public ValueTask RunAsync(DemoContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var categories = new[]
        {
            new QueryCategory(1, "language"),
            new QueryCategory(2, "runtime"),
        };
        var products = new List<QueryProduct>
        {
            new(1, "C# 14", 1, 80m),
            new(2, "LINQ", 1, 60m),
            new(3, "GC", 2, 40m),
        };

        var predicateEvaluations = 0;
        var deferred = products.Where(product =>
        {
            predicateEvaluations++;
            return product.Price >= 50m;
        });
        products.Add(new QueryProduct(4, "HTTP/3", 2, 100m));
        var firstEnumeration = deferred.Select(product => product.Name).ToArray();
        var materializedSnapshot = firstEnumeration.ToArray();
        products.Add(new QueryProduct(5, "XML", 2, 55m));

        context.WriteLine("[延迟执行与物化]");
        context.WriteProperty("predicate evaluations", predicateEvaluations);
        context.WriteProperty("deferred first result", string.Join(", ", firstEnumeration));
        context.WriteProperty("snapshot after source mutation", string.Join(", ", materializedSnapshot));

        var querySyntax =
            from category in categories
            join product in products on category.Id equals product.CategoryId
            let discounted = decimal.Round(product.Price * 0.9m, 2)
            where discounted >= 50m
            orderby category.Name, discounted descending
            select new
            {
                Category = category.Name,
                product.Name,
                Discounted = discounted,
            };
        var queryRows = querySyntax.ToArray();

        var summaries =
            (from category in categories
             let matchingProducts = products.Where(product => product.CategoryId == category.Id)
             select new
             {
                 category.Name,
                 Count = matchingProducts.Count(),
                 Maximum = matchingProducts.Max(product => product.Price),
             }).ToArray();

        context.WriteLine("\n[query syntax、let、join 与子查询]");
        context.WriteProperty(
            "rows",
            string.Join(" | ", queryRows.Select(row => $"{row.Category}/{row.Name}/{row.Discounted:F2}")));
        context.WriteProperty(
            "subquery summaries",
            string.Join(" | ", summaries.Select(summary => $"{summary.Name}:{summary.Count}/{summary.Maximum:F0}")));

        var queryable = products
            .AsQueryable()
            .Where(product => product.Price >= 80m)
            .Select(product => product.Name);
        var expression = (MethodCallExpression)queryable.Expression;
        var queryableResult = queryable.ToArray();

        context.WriteLine("\n[IEnumerable 与 IQueryable]");
        context.WriteProperty("expression root", expression.Method.Name);
        context.WriteProperty("provider", queryable.Provider.GetType().Name);
        context.WriteProperty("translated shape result", string.Join(", ", queryableResult));
        context.WriteLine("  AsQueryable 在这里仍由 LINQ to Objects 执行；数据库 provider 会翻译同一类表达式树。外部 provider 不一定支持所有 .NET 方法。");

        DemoAssert.SequenceEqual(["C# 14", "LINQ", "HTTP/3"], firstEnumeration, "延迟查询应看到首次枚举前加入的元素");
        DemoAssert.True(predicateEvaluations == 4 && materializedSnapshot.Length == 3, "每次枚举才执行委托，物化结果应成为独立快照");
        DemoAssert.True(queryRows.Length == 3 && queryRows[0].Category == "language", "查询语法应完成 join、let、filter 和排序");
        DemoAssert.True(
            summaries.Single(summary => summary.Name == "runtime").Count == 3,
            "相关子查询应按分类统计三个 runtime 产品");
        DemoAssert.SequenceEqual(["C# 14", "HTTP/3"], queryableResult, "表达式树查询应由当前 provider 正确执行");

        return ValueTask.CompletedTask;
    }

    private sealed record QueryCategory(int Id, string Name);

    private sealed record QueryProduct(int Id, string Name, int CategoryId, decimal Price);
}
