namespace LearnDotnetCSharp.PluginContract;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class LearningPluginAttribute(
    string id,
    int contractVersion,
    params string[] capabilities) : Attribute
{
    public string Id { get; } = id;

    public int ContractVersion { get; } = contractVersion;

    public IReadOnlyList<string> Capabilities { get; } = Array.AsReadOnly([.. capabilities]);
}

public interface ILearningPlugin
{
    string Id { get; }

    PluginResult Execute(string input, string cultureName);
}

public sealed record PluginResult(
    string PluginId,
    string Output,
    string Greeting,
    string AssemblyVersion);
