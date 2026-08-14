using System.Globalization;
using System.Resources;
using LearnDotnetCSharp.PluginContract;

namespace LearnDotnetCSharp.SamplePlugin;

[LearningPlugin(
    "sample.uppercase",
    contractVersion: 1,
    "text.uppercase",
    "resources.ja-JP")]
public sealed class SampleLearningPlugin : ILearningPlugin
{
    private static readonly ResourceManager Resources = new(
        "LearnDotnetCSharp.SamplePlugin.Strings",
        typeof(SampleLearningPlugin).Assembly);

    public string Id => "sample.uppercase";

    public PluginResult Execute(string input, string cultureName)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        var greeting = Resources.GetString("Greeting", culture)
            ?? throw new MissingManifestResourceException($"Greeting/{cultureName}");
        var version = typeof(SampleLearningPlugin).Assembly.GetName().Version?.ToString()
            ?? "unknown";

        return new PluginResult(
            Id,
            input.ToUpperInvariant(),
            greeting,
            version);
    }
}
