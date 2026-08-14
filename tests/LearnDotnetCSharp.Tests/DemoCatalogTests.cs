using LearnDotnetCSharp.Infrastructure;

namespace LearnDotnetCSharp.Tests;

[TestClass]
public sealed class DemoCatalogTests
{
    [TestMethod]
    public void DiscoverFindsExpectedNumberOfDemos()
    {
        var catalog = DemoCatalogTestFixture.Current;

        Assert.HasCount(DemoCatalogTestFixture.ExpectedDemoCount, catalog.All);
    }

    [TestMethod]
    public void DiscoverProducesCaseInsensitiveUniqueIds()
    {
        var duplicateIds = DemoCatalogTestFixture.Current.All
            .GroupBy(demo => demo.Metadata.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.IsEmpty(
            duplicateIds,
            $"Duplicate demo IDs: {string.Join(", ", duplicateIds)}");
    }

    [TestMethod]
    public void DiscoverContainsKeyCategoriesAndRepresentativeDemos()
    {
        (string Id, string Category)[] requiredDemos =
        [
            ("language.csharp14", "language"),
            ("async.cancellation-stream-valuetask", "async"),
            ("concurrency.channel-pipeline", "concurrency"),
            ("networking.http2-http3", "networking"),
            ("memory.gc", "memory"),
            ("diagnostics.observability", "diagnostics"),
            ("reflection.advanced", "reflection"),
            ("compiler.roslyn-il", "compiler"),
            ("interop.python-process-json", "interop"),
            ("project.cancellable-data-pipeline", "projects"),
            ("project.resilient-analytics-workflow", "projects"),
        ];

        foreach (var (id, category) in requiredDemos)
        {
            Assert.IsTrue(
                DemoCatalogTestFixture.Current.TryGet(id, out var demo),
                $"Required demo '{id}' was not discovered.");
            Assert.IsNotNull(demo);
            Assert.AreEqual(category, demo.Metadata.Category, $"Unexpected category for '{id}'.");
        }
    }

    [TestMethod]
    public void WindowsX64InteropDemosExposeAccurateAvailability()
    {
        string[] nativeDemoIds =
        [
            "interop.c-abi",
            "interop.cpp-opaque-handle",
            "project.polyglot-compute",
        ];
        var shouldBeAvailable = OperatingSystem.IsWindows() &&
            System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture ==
            System.Runtime.InteropServices.Architecture.X64;

        foreach (var id in nativeDemoIds)
        {
            Assert.IsTrue(DemoCatalogTestFixture.Current.TryGet(id, out var demo));
            Assert.IsNotNull(demo);
            Assert.AreEqual(shouldBeAvailable, demo.Availability.CanRun, id);
            if (!shouldBeAvailable)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(demo.Availability.SkipReason), id);
            }
        }
    }
}

internal static class DemoCatalogTestFixture
{
    public const int ExpectedDemoCount = 53;

    private static readonly Lazy<DemoCatalog> Catalog = new(
        static () => DemoCatalog.Discover(typeof(DemoCatalog).Assembly),
        LazyThreadSafetyMode.ExecutionAndPublication);

    public static DemoCatalog Current => Catalog.Value;
}
