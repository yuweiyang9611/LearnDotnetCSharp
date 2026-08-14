using System.Text.RegularExpressions;
using LearnDotnetCSharp.Infrastructure;
using RegularExpression = System.Text.RegularExpressions.Regex;

namespace LearnDotnetCSharp.Demos.Regex;

public sealed partial class RegexAdvancedDemo : IDemo
{
    private const string CatastrophicPattern = @"^(a+)+$";
    private const int GeneratedRegexTimeoutMilliseconds = 250;

    public DemoMetadata Metadata { get; } = new(
        "regex.advanced",
        "regex",
        "源生成正则、超时与线性匹配",
        "通过 GeneratedRegex、命名分组和替换处理结构化文本，并比较回溯超时与 NonBacktracking 引擎。",
        [26],
        ["GeneratedRegex", "named groups", "replacement", "timeout", "RegexOptions.NonBacktracking"]);

    public ValueTask RunAsync(DemoContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        const string taggedContacts = "Ada <compiler>; Grace <navy>; Linus <kernel>";
        var generatedRegex = ContactTagRegex();
        var matches = generatedRegex.Matches(taggedContacts).Cast<Match>().ToArray();

        context.WriteLine("[GeneratedRegex + 命名分组]");
        foreach (var match in matches)
        {
            context.WriteLine(
                $"  name={match.Groups["name"].Value}, domain={match.Groups["domain"].Value}");
        }

        var normalized = generatedRegex.Replace(
            taggedContacts,
            match => $"{match.Groups["name"].Value.ToLowerInvariant()}@{match.Groups["domain"].Value}.example");
        context.WriteProperty("MatchEvaluator 替换", normalized);

        cancellationToken.ThrowIfCancellationRequested();
        var hostileInput = string.Concat(new string('a', 32_768), "!");
        var backtrackingRegex = new RegularExpression(
            CatastrophicPattern,
            RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(30));

        context.WriteLine("\n[超时保护]");
        bool? backtrackingResult = null;
        var timeoutObserved = false;
        try
        {
            backtrackingResult = backtrackingRegex.IsMatch(hostileInput);
            context.WriteProperty("回溯引擎结果", backtrackingResult);
            context.WriteLine("  本次运行在超时前完成；调用方仍已设置有限超时作为安全边界。");
        }
        catch (RegexMatchTimeoutException exception)
        {
            timeoutObserved = true;
            context.WriteProperty("捕获异常", exception.GetType().Name);
            context.WriteProperty("配置超时", exception.MatchTimeout);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var nonBacktrackingRegex = new RegularExpression(
            CatastrophicPattern,
            RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
            TimeSpan.FromSeconds(1));
        context.WriteLine("\n[NonBacktracking]");
        var nonBacktrackingResult = nonBacktrackingRegex.IsMatch(hostileInput);
        context.WriteProperty("相同输入是否匹配", nonBacktrackingResult);
        context.WriteLine("  NonBacktracking 使用有限自动机保证与输入长度近似线性，但不支持反向引用等依赖回溯的结构。");

        DemoAssert.Equal(3, matches.Length, "源生成正则应提取三个联系人");
        DemoAssert.Equal(
            "ada@compiler.example; grace@navy.example; linus@kernel.example",
            normalized,
            "MatchEvaluator 应生成规范化地址");
        DemoAssert.Equal(
            TimeSpan.FromMilliseconds(GeneratedRegexTimeoutMilliseconds),
            generatedRegex.MatchTimeout,
            "源生成正则也必须具备有限超时");
        DemoAssert.True(
            timeoutObserved || backtrackingResult is false,
            "恶意输入必须超时或被判定为不匹配");
        DemoAssert.True(!nonBacktrackingResult, "非回溯引擎应在线性边界内拒绝相同输入");

        return ValueTask.CompletedTask;
    }

    [GeneratedRegex(
        @"(?<name>[\p{L}][\p{L}\p{M}'-]*)\s*<(?<domain>[a-z][a-z0-9-]*)>",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        GeneratedRegexTimeoutMilliseconds)]
    private static partial RegularExpression ContactTagRegex();
}
