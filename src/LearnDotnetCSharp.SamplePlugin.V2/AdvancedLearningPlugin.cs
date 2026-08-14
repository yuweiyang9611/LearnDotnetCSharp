using LearnDotnetCSharp.PluginContract;

namespace LearnDotnetCSharp.SamplePlugin.V2;

[LearningPlugin(
    "sample.uppercase",
    contractVersion: 1,
    "text.uppercase",
    "text.decorate")]
public sealed class AdvancedLearningPlugin : ILearningPlugin
{
    public string Id => "sample.uppercase";

    public PluginResult Execute(string input, string cultureName)
    {
        if (input.StartsWith("fail:", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The v2 plug-in rejected the deterministic failure input.");
        }

        var version = typeof(AdvancedLearningPlugin).Assembly.GetName().Version?.ToString()
            ?? "unknown";
        return new PluginResult(
            Id,
            $"V2::{input.ToUpperInvariant()}",
            cultureName.Equals("ja-JP", StringComparison.OrdinalIgnoreCase) ? "こんにちは" : "Hello",
            version);
    }
}
