using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using LearnDotnetCSharp.PluginContract;

namespace LearnDotnetCSharp.Capstones.PluginHost;

public sealed class CollectiblePluginHost
{
    private readonly int supportedContractVersion;
    private readonly int failuresBeforeQuarantine;
    private readonly ConcurrentDictionary<string, int> failureCounts = new(StringComparer.Ordinal);
    private PluginDescriptor[] descriptors = [];

    public CollectiblePluginHost(int supportedContractVersion = 1, int failuresBeforeQuarantine = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(supportedContractVersion, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(failuresBeforeQuarantine, 1);
        this.supportedContractVersion = supportedContractVersion;
        this.failuresBeforeQuarantine = failuresBeforeQuarantine;
    }

    public PluginHostSnapshot Refresh(IEnumerable<string> assemblyPaths, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assemblyPaths);

        var next = assemblyPaths
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.Ordinal)
            .SelectMany(path => ReadDescriptors(path, cancellationToken))
            .OrderBy(descriptor => descriptor.Id, StringComparer.Ordinal)
            .ThenByDescending(descriptor => descriptor.ImplementationVersion)
            .ToArray();

        Volatile.Write(ref descriptors, next);
        failureCounts.Clear();
        return Snapshot();
    }

    public PluginHostSnapshot Snapshot()
    {
        var current = Volatile.Read(ref descriptors);
        var quarantined = current
            .Where(descriptor => IsQuarantined(descriptor.AssemblyPath))
            .Select(descriptor => descriptor.AssemblyPath)
            .ToHashSet(StringComparer.Ordinal);
        return new PluginHostSnapshot([.. current], quarantined);
    }

    public PluginInvocationResult Execute(
        string capability,
        string input,
        string cultureName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(capability);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(cultureName);

        var current = Volatile.Read(ref descriptors);
        var quarantined = current
            .Where(descriptor => IsQuarantined(descriptor.AssemblyPath))
            .Select(descriptor => descriptor.AssemblyPath)
            .ToHashSet(StringComparer.Ordinal);
        var candidates = PluginRouter.OrderCandidates(
            current,
            capability,
            supportedContractVersion,
            quarantined);
        var attempts = new List<PluginAttempt>();

        foreach (var descriptor in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var boundary = InvokeAndBeginUnload(descriptor, input, cultureName);
            WaitForUnload(boundary.LoadContextReference, cancellationToken);
            var attempt = new PluginAttempt(
                descriptor,
                boundary.Result is not null,
                boundary.Failure,
                !boundary.LoadContextReference.IsAlive);
            attempts.Add(attempt);

            if (boundary.Result is not null)
            {
                return new PluginInvocationResult(boundary.Result, attempts);
            }

            var failures = failureCounts.AddOrUpdate(
                descriptor.AssemblyPath,
                addValue: 1,
                static (_, count) => checked(count + 1));
            if (failures < failuresBeforeQuarantine)
            {
                continue;
            }
        }

        return new PluginInvocationResult(null, attempts);
    }

    public void ResetQuarantine(string assemblyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        failureCounts.TryRemove(Path.GetFullPath(assemblyPath), out _);
    }

    private bool IsQuarantined(string assemblyPath) =>
        failureCounts.TryGetValue(assemblyPath, out var count) && count >= failuresBeforeQuarantine;

    private static IReadOnlyList<PluginDescriptor> ReadDescriptors(
        string assemblyPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(assemblyPath))
        {
            throw new FileNotFoundException("The plug-in assembly does not exist.", assemblyPath);
        }

        var boundary = ReadDescriptorsAndBeginUnload(assemblyPath);
        WaitForUnload(boundary.LoadContextReference, cancellationToken);
        return boundary.Descriptors;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static DescriptorBoundary ReadDescriptorsAndBeginUnload(string assemblyPath)
    {
        var loadContext = new PluginLoadContext(assemblyPath);
        var weakReference = new WeakReference(loadContext, trackResurrection: true);
        try
        {
            var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
            var implementationVersion = assembly.GetName().Version ?? new Version(0, 0);
            var discovered = assembly.GetTypes()
                .Where(type => !type.IsAbstract && typeof(ILearningPlugin).IsAssignableFrom(type))
                .Select(type => ReadDescriptor(type, implementationVersion, assemblyPath))
                .ToArray();
            if (discovered.Length == 0)
            {
                throw new InvalidOperationException($"No {nameof(ILearningPlugin)} implementation was found in '{assemblyPath}'.");
            }

            return new DescriptorBoundary(discovered, weakReference);
        }
        finally
        {
            loadContext.Unload();
        }
    }

    private static PluginDescriptor ReadDescriptor(Type pluginType, Version implementationVersion, string assemblyPath)
    {
        var attribute = pluginType.CustomAttributes.SingleOrDefault(
            data => data.AttributeType == typeof(LearningPluginAttribute))
            ?? throw new InvalidOperationException($"Plug-in type '{pluginType.FullName}' has no {nameof(LearningPluginAttribute)}.");
        var id = (string?)attribute.ConstructorArguments[0].Value
            ?? throw new InvalidOperationException("The plug-in attribute has no ID.");
        var contractVersion = (int)(attribute.ConstructorArguments[1].Value
            ?? throw new InvalidOperationException("The plug-in attribute has no contract version."));
        var capabilityArguments =
            (IReadOnlyCollection<CustomAttributeTypedArgument>?)attribute.ConstructorArguments[2].Value
            ?? throw new InvalidOperationException("The plug-in attribute has no capabilities.");
        var capabilities = capabilityArguments
            .Select(argument => (string?)argument.Value ?? string.Empty)
            .ToArray();
        if (string.IsNullOrWhiteSpace(id) || capabilities.Length == 0 || capabilities.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException("The plug-in descriptor contains an empty ID or capability.");
        }

        return new PluginDescriptor(
            id,
            contractVersion,
            implementationVersion,
            capabilities,
            assemblyPath,
            pluginType.FullName ?? pluginType.Name);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static InvocationBoundary InvokeAndBeginUnload(
        PluginDescriptor descriptor,
        string input,
        string cultureName)
    {
        var loadContext = new PluginLoadContext(descriptor.AssemblyPath);
        var weakReference = new WeakReference(loadContext, trackResurrection: true);
        try
        {
            try
            {
                var assembly = loadContext.LoadFromAssemblyPath(descriptor.AssemblyPath);
                var pluginType = assembly.GetType(descriptor.EntryTypeName, throwOnError: true)
                    ?? throw new TypeLoadException(descriptor.EntryTypeName);
                var plugin = (ILearningPlugin?)Activator.CreateInstance(pluginType)
                    ?? throw new InvalidOperationException("The plug-in instance could not be created.");
                if (!string.Equals(plugin.Id, descriptor.Id, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("The plug-in instance ID does not match its attribute metadata.");
                }

                return new InvocationBoundary(plugin.Execute(input, cultureName), null, weakReference);
            }
            catch (Exception exception)
            {
                return new InvocationBoundary(
                    null,
                    new PluginFailure(exception.GetType().FullName ?? exception.GetType().Name, exception.Message),
                    weakReference);
            }
        }
        finally
        {
            loadContext.Unload();
        }
    }

    private static void WaitForUnload(WeakReference reference, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 12 && reference.IsAlive; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }

    private sealed class PluginLoadContext(string pluginPath)
        : AssemblyLoadContext($"LearnDotnetCSharp.Capstone.Plugin.{Guid.NewGuid():N}", isCollectible: true)
    {
        private readonly AssemblyDependencyResolver resolver = new(pluginPath);
        private readonly string pluginDirectory = Path.GetDirectoryName(pluginPath)
            ?? throw new ArgumentException("The plug-in path has no directory.", nameof(pluginPath));

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name == typeof(ILearningPlugin).Assembly.GetName().Name)
            {
                return null;
            }

            if (!string.IsNullOrEmpty(assemblyName.CultureName))
            {
                var satellitePath = Path.Combine(
                    pluginDirectory,
                    assemblyName.CultureName,
                    $"{assemblyName.Name}.dll");
                if (File.Exists(satellitePath))
                {
                    return LoadFromAssemblyPath(satellitePath);
                }
            }

            var path = resolver.ResolveAssemblyToPath(assemblyName);
            return path is null ? null : LoadFromAssemblyPath(path);
        }

        protected override nint LoadUnmanagedDll(string unmanagedDllName)
        {
            var path = resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return path is null ? nint.Zero : LoadUnmanagedDllFromPath(path);
        }
    }

    private sealed record DescriptorBoundary(
        IReadOnlyList<PluginDescriptor> Descriptors,
        WeakReference LoadContextReference);

    private sealed record InvocationBoundary(
        PluginResult? Result,
        PluginFailure? Failure,
        WeakReference LoadContextReference);
}
