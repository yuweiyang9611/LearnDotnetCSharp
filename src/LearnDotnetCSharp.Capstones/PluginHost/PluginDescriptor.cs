using LearnDotnetCSharp.PluginContract;

namespace LearnDotnetCSharp.Capstones.PluginHost;

public sealed record PluginDescriptor(
    string Id,
    int ContractVersion,
    Version ImplementationVersion,
    IReadOnlyList<string> Capabilities,
    string AssemblyPath,
    string EntryTypeName);

public sealed record PluginFailure(string TypeName, string Message);

public sealed record PluginAttempt(
    PluginDescriptor Descriptor,
    bool Succeeded,
    PluginFailure? Failure,
    bool LoadContextCollected);

public sealed record PluginInvocationResult(
    PluginResult? Result,
    IReadOnlyList<PluginAttempt> Attempts)
{
    public bool Succeeded => Result is not null;

    public bool UsedFallback => Attempts.Count > 1 && Succeeded;
}

public sealed record PluginHostSnapshot(
    IReadOnlyList<PluginDescriptor> Descriptors,
    IReadOnlySet<string> QuarantinedAssemblyPaths);
