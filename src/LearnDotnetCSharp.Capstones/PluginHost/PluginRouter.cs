namespace LearnDotnetCSharp.Capstones.PluginHost;

public static class PluginRouter
{
    public static IReadOnlyList<PluginDescriptor> OrderCandidates(
        IEnumerable<PluginDescriptor> descriptors,
        string capability,
        int supportedContractVersion,
        IReadOnlySet<string>? quarantinedAssemblyPaths = null)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        ArgumentException.ThrowIfNullOrWhiteSpace(capability);
        ArgumentOutOfRangeException.ThrowIfLessThan(supportedContractVersion, 1);

        return descriptors
            .Where(descriptor => descriptor.ContractVersion == supportedContractVersion)
            .Where(descriptor => descriptor.Capabilities.Contains(capability, StringComparer.OrdinalIgnoreCase))
            .Where(descriptor => quarantinedAssemblyPaths?.Contains(descriptor.AssemblyPath) != true)
            .OrderByDescending(descriptor => descriptor.ImplementationVersion)
            .ThenBy(descriptor => descriptor.Id, StringComparer.Ordinal)
            .ThenBy(descriptor => descriptor.AssemblyPath, StringComparer.Ordinal)
            .ToArray();
    }
}
