using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Serialization;
using LearnDotnetCSharp.Infrastructure;

namespace LearnDotnetCSharp.Demos.Serialization;

public sealed class ContractVersioningDemo : IDemo
{
    public DemoMetadata Metadata { get; } = new(
        "serialization.contract-versioning",
        "serialization",
        "XML/JSON 契约、多态与版本兼容",
        "用 XmlSerializer 处理显式 XML 契约，以 System.Text.Json 源生成多态契约，并通过扩展数据保留未来版本字段。",
        [17],
        ["XmlSerializer", "JSON polymorphism", "JsonExtensionData", "source generation", "version tolerance", "untrusted input"]);

    public ValueTask RunAsync(DemoContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var xmlCatalog = new XmlCourseCatalog
        {
            Version = 2,
            Courses =
            [
                new XmlCourse { Id = "csharp", Title = "C# 14", Hours = 8 },
                new XmlCourse { Id = "crypto", Title = "Cryptography", Hours = 6 },
            ],
        };
        var xmlSerializer = new XmlSerializer(typeof(XmlCourseCatalog));
        string xml;
        using (var writer = new StringWriter(CultureInfo.InvariantCulture))
        {
            xmlSerializer.Serialize(writer, xmlCatalog);
            xml = writer.ToString();
        }

        XmlCourseCatalog xmlRoundTrip;
        using (var reader = new StringReader(xml))
        using (var xmlReader = XmlReader.Create(
                   reader,
                   new XmlReaderSettings
                   {
                       DtdProcessing = DtdProcessing.Prohibit,
                       XmlResolver = null,
                   }))
        {
            xmlRoundTrip = (XmlCourseCatalog?)xmlSerializer.Deserialize(xmlReader)
                ?? throw new InvalidOperationException("XmlSerializer 返回了 null。");
        }

        const string FutureJson = """
            {
              "schemaVersion": 2,
              "name": "geometry",
              "shapes": [
                { "$kind": "circle", "radius": 2.0 },
                { "$kind": "rectangle", "width": 3.0, "height": 4.0 }
              ],
              "futureFlag": true
            }
            """;
        var jsonContext = new ContractJsonContext(
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
            });
        var envelope = JsonSerializer.Deserialize(FutureJson, jsonContext.VersionedShapeEnvelope)
            ?? throw new JsonException("版本化 JSON 返回了 null。");
        var reserialized = JsonSerializer.Serialize(envelope, jsonContext.VersionedShapeEnvelope);

        context.WriteProperty("XmlSerializer courses", string.Join(", ", xmlRoundTrip.Courses.Select(course => course.Id)));
        context.WriteProperty("polymorphic runtime types", string.Join(", ", envelope.Shapes.Select(shape => shape.GetType().Name)));
        context.WriteProperty(
            "future fields preserved",
            envelope.ExtensionData is null ? string.Empty : string.Join(", ", envelope.ExtensionData.Keys));
        context.WriteProperty("reserialized bytes", reserialized.Length);

        DemoAssert.True(
            xmlRoundTrip.Version == 2 && xmlRoundTrip.Courses.Select(course => course.Id).SequenceEqual(["csharp", "crypto"]),
            "XmlSerializer 应按显式元素/属性契约完成往返");
        DemoAssert.True(
            envelope.Shapes is [ContractCircle { Radius: 2.0 }, ContractRectangle { Width: 3.0, Height: 4.0 }],
            "JSON 类型判别符应恢复正确的派生类型");
        DemoAssert.True(
            envelope.ExtensionData?.ContainsKey("futureFlag") == true && reserialized.Contains("futureFlag", StringComparison.Ordinal),
            "扩展数据应保留当前模型尚不认识的未来字段");

        context.WriteLine("  BinaryFormatter/ISerializable 旧二进制格式不在 .NET 10 中复活；迁移时应选择有边界、可审计且能限制输入的格式。");
        return ValueTask.CompletedTask;
    }
}

[XmlRoot("catalog")]
public sealed class XmlCourseCatalog
{
    [XmlAttribute("version")]
    public int Version { get; set; }

    [XmlElement("course")]
    public List<XmlCourse> Courses { get; set; } = [];
}

public sealed class XmlCourse
{
    [XmlAttribute("id")]
    public string Id { get; set; } = string.Empty;

    [XmlElement("title")]
    public string Title { get; set; } = string.Empty;

    [XmlElement("hours")]
    public int Hours { get; set; }
}

public sealed class VersionedShapeEnvelope
{
    public int SchemaVersion { get; set; }

    public string Name { get; set; } = string.Empty;

    public List<ContractShape> Shapes { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(ContractCircle), "circle")]
[JsonDerivedType(typeof(ContractRectangle), "rectangle")]
public abstract record ContractShape;

public sealed record ContractCircle(double Radius) : ContractShape;

public sealed record ContractRectangle(double Width, double Height) : ContractShape;

[JsonSerializable(typeof(VersionedShapeEnvelope))]
[JsonSerializable(typeof(ContractCircle))]
[JsonSerializable(typeof(ContractRectangle))]
public sealed partial class ContractJsonContext : JsonSerializerContext;
