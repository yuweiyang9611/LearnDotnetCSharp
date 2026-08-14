using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using LearnDotnetCSharp.Infrastructure;

namespace LearnDotnetCSharp.Demos.Serialization;

public sealed class XmlJsonStreamingDemo : IDemo
{
    public DemoMetadata Metadata { get; } = new(
        "serialization.xml-json",
        "serialization",
        "流式 XML 与源生成 JSON",
        "用异步 XmlWriter/XmlReader 逐节点处理 XML，并以源生成元数据、未知成员拒绝和重复属性拒绝完成 JSON 往返。",
        [11],
        ["XmlWriter", "XmlReader", "async XML", "System.Text.Json", "source generation", "strict JSON"]);

    public async ValueTask RunAsync(DemoContext context, CancellationToken cancellationToken)
    {
        CourseCatalog catalog = new(
            2,
            [
                new("csharp", "C# 14", 8),
                new("http3", "HTTP/3", 4),
            ]);

        var xmlBytes = await WriteXmlAsync(catalog, cancellationToken).ConfigureAwait(false);
        var xmlTitles = await ReadXmlTitlesAsync(xmlBytes, cancellationToken).ConfigureAwait(false);

        var jsonOptions = new JsonSerializerOptions
        {
            AllowDuplicateProperties = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            WriteIndented = true,
        };
        var jsonContext = new CatalogJsonContext(jsonOptions);
        var json = JsonSerializer.Serialize(catalog, jsonContext.CourseCatalog);
        var jsonRoundTrip = JsonSerializer.Deserialize(json, jsonContext.CourseCatalog);

        context.WriteProperty("XML bytes", xmlBytes.Length);
        context.WriteProperty("XML titles", string.Join(", ", xmlTitles));
        context.WriteLine(json);

        DemoAssert.SequenceEqual(["C# 14", "HTTP/3"], xmlTitles, "异步 XmlReader 应按文档顺序读取课程标题");
        DemoAssert.True(
            jsonRoundTrip is not null &&
            jsonRoundTrip.Version == catalog.Version &&
            jsonRoundTrip.Courses.SequenceEqual(catalog.Courses),
            "源生成 JSON 元数据应完成不可变记录的往返序列化");

        await DemonstrateStrictJsonAsync(jsonContext, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<byte[]> WriteXmlAsync(CourseCatalog catalog, CancellationToken cancellationToken)
    {
        await using var stream = new MemoryStream();
        var settings = new XmlWriterSettings
        {
            Async = true,
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
        };

        await using (var writer = XmlWriter.Create(stream, settings))
        {
            await writer.WriteStartDocumentAsync().ConfigureAwait(false);
            await writer.WriteStartElementAsync(null, "catalog", null).ConfigureAwait(false);
            await writer.WriteAttributeStringAsync(null, "version", null, catalog.Version.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);

            foreach (var course in catalog.Courses)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await writer.WriteStartElementAsync(null, "course", null).ConfigureAwait(false);
                await writer.WriteAttributeStringAsync(null, "id", null, course.Id).ConfigureAwait(false);
                await writer.WriteElementStringAsync(null, "title", null, course.Title).ConfigureAwait(false);
                await writer.WriteElementStringAsync(null, "hours", null, course.Hours.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
                await writer.WriteEndElementAsync().ConfigureAwait(false);
            }

            await writer.WriteEndElementAsync().ConfigureAwait(false);
            await writer.WriteEndDocumentAsync().ConfigureAwait(false);
            await writer.FlushAsync().ConfigureAwait(false);
        }

        return stream.ToArray();
    }

    private static async Task<string[]> ReadXmlTitlesAsync(byte[] xmlBytes, CancellationToken cancellationToken)
    {
        await using var stream = new MemoryStream(xmlBytes, writable: false);
        using var reader = XmlReader.Create(
            stream,
            new XmlReaderSettings { Async = true, IgnoreWhitespace = true });
        var titles = new List<string>();

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "title")
            {
                titles.Add(await reader.ReadElementContentAsStringAsync().ConfigureAwait(false));
            }
        }

        return titles.ToArray();
    }

    private static async Task DemonstrateStrictJsonAsync(
        CatalogJsonContext jsonContext,
        CancellationToken cancellationToken)
    {
        const string JsonWithUnknownMember = """
            {"version":2,"courses":[],"unexpected":true}
            """;

        try
        {
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(JsonWithUnknownMember), writable: false);
            _ = await JsonSerializer
                .DeserializeAsync(stream, jsonContext.CourseCatalog, cancellationToken)
                .ConfigureAwait(false);
            throw new InvalidOperationException("严格 JSON 选项没有拒绝未知成员。");
        }
        catch (JsonException)
        {
            // 预期路径：UnmappedMemberHandling.Disallow 拒绝额外输入。
        }
    }
}

public sealed record CourseCatalog(int Version, CourseEntry[] Courses);

public sealed record CourseEntry(string Id, string Title, int Hours);

[JsonSerializable(typeof(CourseCatalog))]
[JsonSerializable(typeof(CourseEntry))]
public sealed partial class CatalogJsonContext : JsonSerializerContext;
