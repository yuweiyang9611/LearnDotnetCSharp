using System.Xml.Linq;
using LearnDotnetCSharp.Infrastructure;

namespace LearnDotnetCSharp.Demos.Xml;

public sealed class LinqToXmlDemo : IDemo
{
    public DemoMetadata Metadata { get; } = new(
        "xml.linq",
        "xml",
        "LINQ to XML：命名空间、查询、修改与注解",
        "用函数式构造创建 XML，按命名空间查询并原位修改元素，同时演示不会写入文档的对象注解。",
        [10],
        ["XDocument", "XNamespace", "functional construction", "query", "update", "annotation"]);

    public ValueTask RunAsync(DemoContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        XNamespace learning = "urn:learn-dotnet-csharp:courses";
        XNamespace metadata = "urn:learn-dotnet-csharp:metadata";

        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            new XElement(
                learning + "catalog",
                new XAttribute(XNamespace.Xmlns + "m", metadata),
                new XAttribute(metadata + "version", 1),
                CreateCourse(learning, "csharp", "C# 14", 8, ["language", "compiler"]),
                CreateCourse(learning, "linq", "LINQ", 6, ["query", "xml"]),
                CreateCourse(learning, "io", "I/O", 5, ["stream", "pipeline"])));

        var linqCourse = document
            .Descendants(learning + "course")
            .Single(element => (string?)element.Attribute("id") == "linq");
        linqCourse.AddAnnotation(new SourceLocation("pdf", 10));

        var advancedCourses = document
            .Descendants(learning + "course")
            .Where(element => (int)element.Element(learning + "hours")! >= 6)
            .OrderByDescending(element => (int)element.Element(learning + "hours")!)
            .Select(element => (string)element.Element(learning + "title")!)
            .ToArray();

        linqCourse.SetElementValue(learning + "hours", 7);
        document.Root!.SetAttributeValue(metadata + "version", 2);
        document.Root.Add(
            CreateCourse(learning, "http3", "HTTP/3", 4, ["network", "quic"]));

        var tagCounts = document
            .Descendants(learning + "tag")
            .Select(element => element.Value)
            .CountBy(tag => tag)
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToArray();
        var annotation = linqCourse.Annotation<SourceLocation>();

        context.WriteProperty("advanced before update", string.Join(", ", advancedCourses));
        context.WriteProperty("LINQ annotation", $"{annotation?.Source}/chapter-{annotation?.Chapter}");
        context.WriteProperty("tag counts", string.Join(", ", tagCounts.Select(pair => $"{pair.Key}={pair.Value}")));
        context.WriteLine(document.ToString(SaveOptions.DisableFormatting));

        DemoAssert.SequenceEqual(["C# 14", "LINQ"], advancedCourses, "LINQ to XML 查询应按课时降序筛选课程");
        DemoAssert.Equal(7, (int)linqCourse.Element(learning + "hours")!, "SetElementValue 应更新目标元素");
        DemoAssert.Equal(4, document.Root.Elements(learning + "course").Count(), "修改后的文档应包含四门课程");
        DemoAssert.True(annotation == new SourceLocation("pdf", 10), "XObject 注解应保留对象侧元数据且不污染 XML");

        return ValueTask.CompletedTask;
    }

    private static XElement CreateCourse(
        XNamespace learning,
        string id,
        string title,
        int hours,
        IEnumerable<string> tags)
    {
        return new XElement(
            learning + "course",
            new XAttribute("id", id),
            new XElement(learning + "title", title),
            new XElement(learning + "hours", hours),
            new XElement(
                learning + "tags",
                tags.Select(tag => new XElement(learning + "tag", tag))));
    }

    private sealed record SourceLocation(string Source, int Chapter);
}
