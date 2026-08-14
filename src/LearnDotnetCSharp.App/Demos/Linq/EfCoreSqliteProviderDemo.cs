using LearnDotnetCSharp.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LearnDotnetCSharp.Demos.Linq;

public sealed class EfCoreSqliteProviderDemo : IDemo
{
    public DemoMetadata Metadata { get; } = new(
        "linq.efcore-sqlite-provider",
        "linq",
        "EF Core 10 与 SQLite：真实 IQueryable 提供程序",
        "把表达式树交给 EF Core 翻译成参数化 SQL，演示 .NET 10 LeftJoin、异步物化、跟踪边界以及显式切换到 LINQ to Objects。",
        [8, 9],
        ["EF Core 10", "SQLite", "IQueryable", "expression tree", "SQL translation", "LeftJoin", "AsEnumerable"]);

    public async ValueTask RunAsync(DemoContext context, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var options = new DbContextOptionsBuilder<LearningDbContext>()
            .UseSqlite(connection)
            .EnableDetailedErrors()
            .Options;
        await using var database = new LearningDbContext(options);
        await database.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
        await SeedAsync(database, cancellationToken).ConfigureAwait(false);

        var excludedCategory = "archived";
        var providerQuery = database.Categories
            .TagWith("LearnDotnetCSharp: translated LeftJoin")
            .AsNoTracking()
            .Where(category => category.Name != excludedCategory)
            .LeftJoin(
                database.Courses.AsNoTracking(),
                category => category.Id,
                course => course.CategoryId,
                (category, course) => new
                {
                    Category = category.Name,
                    Course = course == null ? "<none>" : course.Title,
                    DurationHours = course == null ? 0 : course.DurationHours,
                })
            .OrderBy(row => row.Category)
            .ThenBy(row => row.Course)
            .Select(row => new ProviderRow(row.Category, row.Course, row.DurationHours));

        var expression = providerQuery.Expression.ToString();
        var sql = providerQuery.ToQueryString();
        var rows = await providerQuery.ToArrayAsync(cancellationToken).ConfigureAwait(false);

        var translatedTitles = await database.Courses
            .AsNoTracking()
            .Where(course => course.DurationHours >= 4)
            .OrderBy(course => course.Title)
            .Select(course => course.Title)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        var clientSideTitles = translatedTitles
            .AsEnumerable()
            .Select(NormalizeForDisplay)
            .ToArray();

        context.WriteProperty("provider", database.Database.ProviderName ?? "<unknown>");
        context.WriteProperty("expression root", providerQuery.Expression.NodeType);
        context.WriteProperty("expression contains LeftJoin", expression.Contains("LeftJoin", StringComparison.Ordinal));
        context.WriteProperty("SQL contains LEFT JOIN", sql.Contains("LEFT JOIN", StringComparison.OrdinalIgnoreCase));
        context.WriteProperty("SQL parameterized", sql.Contains(".param set", StringComparison.OrdinalIgnoreCase));
        context.WriteProperty("translated SQL", string.Join(' ', sql.Split(default(char[]), StringSplitOptions.RemoveEmptyEntries)));
        context.WriteProperty(
            "rows",
            string.Join(" | ", rows.Select(row => $"{row.Category}/{row.Course}/{row.DurationHours}h")));
        context.WriteProperty("client boundary", string.Join(", ", clientSideTitles));
        context.WriteLine("  IQueryable 在 ToArrayAsync 前仍是表达式树；ToQueryString 展示 provider 翻译后的 SQL。AsEnumerable 之后才明确进入本地委托流水线。");

        DemoAssert.True(
            sql.Contains("LEFT JOIN", StringComparison.OrdinalIgnoreCase) &&
            sql.Contains("ORDER BY", StringComparison.OrdinalIgnoreCase) &&
            sql.Contains(".param set", StringComparison.OrdinalIgnoreCase),
            "EF Core 10 应把 LeftJoin 和排序翻译到 SQLite SQL");
        DemoAssert.True(
            rows.Any(row => row.Category == "runtime" && row.Course == "<none>"),
            "左连接应保留没有课程的 runtime 分类");
        DemoAssert.SequenceEqual(
            ["ASYNC STREAMS", "NATIVE INTEROP"],
            clientSideTitles,
            "显式物化后应能安全执行 provider 无需理解的本地格式化方法");
    }

    private static async Task SeedAsync(LearningDbContext database, CancellationToken cancellationToken)
    {
        database.Categories.AddRange(
            new CourseCategory { Id = 1, Name = "language" },
            new CourseCategory { Id = 2, Name = "runtime" },
            new CourseCategory { Id = 3, Name = "interop" });
        database.Courses.AddRange(
            new LearningCourse { Id = 1, CategoryId = 1, Title = "C# 14", DurationHours = 3 },
            new LearningCourse { Id = 2, CategoryId = 1, Title = "Async Streams", DurationHours = 5 },
            new LearningCourse { Id = 3, CategoryId = 3, Title = "Native Interop", DurationHours = 4 });
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string NormalizeForDisplay(string title) => title.ToUpperInvariant();

    private sealed record ProviderRow(string Category, string Course, int DurationHours);

    private sealed class LearningDbContext(DbContextOptions<LearningDbContext> options) : DbContext(options)
    {
        public DbSet<CourseCategory> Categories => Set<CourseCategory>();

        public DbSet<LearningCourse> Courses => Set<LearningCourse>();
    }

    private sealed class CourseCategory
    {
        public int Id { get; set; }

        public required string Name { get; set; }
    }

    private sealed class LearningCourse
    {
        public int Id { get; set; }

        public int CategoryId { get; set; }

        public required string Title { get; set; }

        public int DurationHours { get; set; }
    }
}
