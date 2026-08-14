using System.Globalization;
using System.Text;
using LearnDotnetCSharp.Infrastructure;

namespace LearnDotnetCSharp.Demos.Framework;

public sealed class TextAndGlobalizationDemo : IDemo
{
    public DemoMetadata Metadata { get; } = new(
        "framework.text-globalization",
        "framework",
        "文本、Unicode、格式化与全球化",
        "展示 string/Rune/文本元素的不同视图、UTF-8 编解码、区域性解析、标准格式和显式 StringComparer。",
        [5, 6],
        ["Rune", "StringInfo", "UTF-8", "CultureInfo", "NumberStyles", "StringComparer"]);

    public ValueTask RunAsync(DemoContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        const string text = "A😀e\u0301";
        var utf16CodeUnits = text.Length;
        var runes = text.EnumerateRunes().ToArray();
        var textElements = EnumerateTextElements(text).ToArray();
        var utf8 = Encoding.UTF8.GetBytes(text);
        var utf8RoundTrip = Encoding.UTF8.GetString(utf8);

        context.WriteLine("[同一文本的三种计数]");
        context.WriteProperty("UTF-16 code units", utf16CodeUnits);
        context.WriteProperty("Unicode scalar values", runes.Length);
        context.WriteProperty("grapheme-like elements", textElements.Length);
        context.WriteProperty("UTF-8 bytes", utf8.Length);
        context.WriteProperty("UTF-8 hex", Convert.ToHexStringLower(utf8));

        var german = CultureInfo.GetCultureInfo("de-DE");
        var japanese = CultureInfo.GetCultureInfo("ja-JP");
        var parsed = decimal.TryParse("1.234,50", NumberStyles.Number, german, out var amount);
        var invariant = FormattableString.Invariant($"amount={amount:F2}");
        var japaneseCurrency = amount.ToString("C", japanese);

        context.WriteLine("\n[显式区域性]");
        context.WriteProperty("de-DE parse", $"{parsed}: {invariant}");
        context.WriteProperty("ja-JP currency", japaneseCurrency);

        string[] words = ["ångström", "Apple", "apple", "Äther"];
        var ordinal = words.Order(StringComparer.Ordinal).ToArray();
        var linguistic = words.Order(StringComparer.Create(german, ignoreCase: true)).ToArray();
        context.WriteProperty("Ordinal first", ordinal[0]);
        context.WriteProperty("de-DE ignore-case first", linguistic[0]);

        DemoAssert.True(
            utf16CodeUnits == 5 && runes.Length == 4 && textElements.Length == 3,
            "UTF-16 单元、Unicode 标量和文本元素的计数应体现代理项与组合字符差异");
        DemoAssert.Equal(text, utf8RoundTrip, "UTF-8 编解码应无损往返");
        DemoAssert.True(parsed && amount == 1234.50m, "区域性解析应正确理解德式小数和千位分隔符");
        DemoAssert.Equal("amount=1234.50", invariant, "持久化文本应使用固定区域性");
        DemoAssert.True(
            !ordinal.SequenceEqual(linguistic),
            "序数排序与语言学排序应被显式区分");

        return ValueTask.CompletedTask;
    }

    private static IEnumerable<string> EnumerateTextElements(string value)
    {
        var enumerator = StringInfo.GetTextElementEnumerator(value);
        while (enumerator.MoveNext())
        {
            yield return enumerator.GetTextElement();
        }
    }
}
