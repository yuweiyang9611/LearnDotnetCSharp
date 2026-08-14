using LearnDotnetCSharp.Capstones.PluginHost;
using LearnDotnetCSharp.Infrastructure;

namespace LearnDotnetCSharp.Tests;

[TestClass]
[DoNotParallelize]
public sealed class PluginHostTests
{
    [TestMethod]
    public void RouterSelectsHighestCompatibleImplementationVersion()
    {
        PluginDescriptor[] descriptors =
        [
            Descriptor("v1.dll", contractVersion: 1, new Version(1, 0), "text.uppercase"),
            Descriptor("v3-incompatible.dll", contractVersion: 2, new Version(3, 0), "text.uppercase"),
            Descriptor("v2.dll", contractVersion: 1, new Version(2, 0), "text.uppercase", "text.decorate"),
        ];

        var candidates = PluginRouter.OrderCandidates(descriptors, "TEXT.UPPERCASE", supportedContractVersion: 1);

        Assert.HasCount(2, candidates);
        Assert.AreEqual(new Version(2, 0), candidates[0].ImplementationVersion);
        Assert.AreEqual(new Version(1, 0), candidates[1].ImplementationVersion);
    }

    [TestMethod]
    public void RefreshHotSwitchesToV2AndFailureFallsBackToV1()
    {
        var v1 = GetBuiltPluginPath("LearnDotnetCSharp.SamplePlugin", "LearnDotnetCSharp.SamplePlugin.dll");
        var v2 = GetBuiltPluginPath("LearnDotnetCSharp.SamplePlugin.V2", "LearnDotnetCSharp.SamplePlugin.V2.dll");
        var host = new CollectiblePluginHost(failuresBeforeQuarantine: 1);

        host.Refresh([v1]);
        var beforeUpdate = host.Execute("text.uppercase", "before update", "ja-JP");
        host.Refresh([v1, v2]);
        var afterUpdate = host.Execute("text.uppercase", "after update", "ja-JP");
        var fallback = host.Execute("text.uppercase", "fail: fallback", "ja-JP");

        Assert.IsTrue(beforeUpdate.Succeeded);
        StringAssert.StartsWith(beforeUpdate.Result!.Output, "BEFORE UPDATE");
        Assert.IsTrue(afterUpdate.Succeeded);
        StringAssert.StartsWith(afterUpdate.Result!.Output, "V2::AFTER UPDATE");
        Assert.IsTrue(fallback.Succeeded);
        Assert.IsTrue(fallback.UsedFallback);
        Assert.HasCount(2, fallback.Attempts);
        Assert.IsNotNull(fallback.Attempts[0].Failure);
        Assert.AreEqual(new Version(1, 0, 0, 0), fallback.Attempts[1].Descriptor.ImplementationVersion);
        Assert.Contains(v2, host.Snapshot().QuarantinedAssemblyPaths);
    }

    private static PluginDescriptor Descriptor(
        string path,
        int contractVersion,
        Version version,
        params string[] capabilities) =>
        new("sample.uppercase", contractVersion, version, capabilities, path, "Example.Plugin");

    private static string GetBuiltPluginPath(string projectName, string assemblyName)
    {
        var root = WorkspaceLocator.FindRoot();
        var frameworkDirectory = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar));
        var configuration = frameworkDirectory.Parent?.Name
            ?? throw new InvalidOperationException("Could not determine the test build configuration.");
        var path = Path.Combine(root, "src", projectName, "bin", configuration, "net10.0", assemblyName);
        Assert.IsTrue(File.Exists(path), $"Expected the built plug-in at '{path}'.");
        return path;
    }
}
