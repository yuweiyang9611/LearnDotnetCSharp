using LearnDotnetCSharp.Capstones.Polyglot;

namespace LearnDotnetCSharp.Tests;

[TestClass]
public sealed class PythonWorkerPoolTests
{
    [TestMethod]
    public async Task PoolReusesConfiguredPersistentProcesses()
    {
        var options = CreateOptions(workerCount: 2, queueCapacity: 4);
        await using var pool = await PythonWorkerPool.StartAsync(options).ConfigureAwait(false);

        var tasks = Enumerable.Range(0, 12)
            .Select(index => pool.AnalyzeAsync(new PythonAnalyzeRequest(
                $"reuse-{index}",
                $"batch {index}",
                [index, index + 1, index + 2],
                DelayMilliseconds: 20)))
            .ToArray();
        var responses = await Task.WhenAll(tasks).ConfigureAwait(false);
        var snapshot = pool.Snapshot;

        Assert.IsTrue(responses.All(static response => response.Ok));
        Assert.IsLessThanOrEqualTo(
            upperBound: 2,
            value: responses.Select(static response => response.Runtime.ProcessId).Distinct().Count(),
            "A two-worker pool must not launch a process per request.");
        Assert.AreEqual(2L, snapshot.Starts);
        Assert.AreEqual(0L, snapshot.Restarts);
        Assert.HasCount(2, snapshot.ActiveProcessIds);
        Assert.HasCount(2, snapshot.ObservedProcessIds);
        Assert.IsTrue(responses.All(response => snapshot.ActiveProcessIds.Contains(response.Runtime.ProcessId)));
    }

    [TestMethod]
    public async Task AnalyzeRetriesOnceAfterDeterministicWorkerCrash()
    {
        var options = CreateOptions(workerCount: 1, queueCapacity: 1, standardErrorTailCharacters: 128);
        await using var pool = await PythonWorkerPool.StartAsync(options).ConfigureAwait(false);

        var response = await pool.AnalyzeAsync(new PythonAnalyzeRequest(
            "crash-and-retry",
            "retry-safe analysis",
            [1.5, 2.5, 10.0],
            CrashOnFirstAttempt: true)).ConfigureAwait(false);
        var snapshot = pool.Snapshot;

        Assert.IsTrue(response.Ok);
        Assert.AreEqual("crash-and-retry", response.Id);
        Assert.AreEqual(14.0, response.Result?.Sum);
        Assert.AreEqual(2L, snapshot.Starts);
        Assert.AreEqual(1L, snapshot.Restarts);
        Assert.AreEqual(2, response.Runtime.Generation);
        Assert.IsLessThanOrEqualTo(
            upperBound: options.StandardErrorTailCharacters,
            value: snapshot.StandardErrorTail.Length,
            "The stderr snapshot must never exceed its configured UTF-16 character limit.");
        StringAssert.Contains(snapshot.StandardErrorTail, "forced analyze crash for crash-and-retry");
    }

    [TestMethod]
    public async Task CrashControlWaitsForAReplacementSession()
    {
        var options = CreateOptions(workerCount: 1, queueCapacity: 1);
        await using var pool = await PythonWorkerPool.StartAsync(options).ConfigureAwait(false);

        await pool.CrashWorkerAsync().ConfigureAwait(false);
        var response = await pool.AnalyzeAsync(
            new PythonAnalyzeRequest("after-control-crash", "healthy", [4.0, 5.0])).ConfigureAwait(false);
        var snapshot = pool.Snapshot;

        Assert.IsTrue(response.Ok);
        Assert.AreEqual(9.0, response.Result?.Sum);
        Assert.AreEqual(2, response.Runtime.Generation);
        Assert.AreEqual(2L, snapshot.Starts);
        Assert.AreEqual(1L, snapshot.Restarts);
        StringAssert.Contains(snapshot.StandardErrorTail, "forced crash for crash-0-1");
    }

    [TestMethod]
    public async Task PoolRejectsDuplicateInFlightRequestIds()
    {
        var options = CreateOptions(workerCount: 1, queueCapacity: 2);
        await using var pool = await PythonWorkerPool.StartAsync(options).ConfigureAwait(false);
        var first = pool.AnalyzeAsync(new PythonAnalyzeRequest(
            "duplicate-id",
            "first",
            [1.0],
            DelayMilliseconds: 250));

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => pool.AnalyzeAsync(new PythonAnalyzeRequest("duplicate-id", "second", [2.0])))
            .ConfigureAwait(false);
        var response = await first.ConfigureAwait(false);

        StringAssert.Contains(exception.Message, "already in flight");
        Assert.AreEqual("duplicate-id", response.Id);
    }

    [TestMethod]
    public async Task CancellationReplacesSessionAndDisposeRemovesActiveProcesses()
    {
        var options = CreateOptions(workerCount: 1, queueCapacity: 1);
        var pool = await PythonWorkerPool.StartAsync(options).ConfigureAwait(false);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(80));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => pool.AnalyzeAsync(
                new PythonAnalyzeRequest("cancelled", "slow", [1.0], DelayMilliseconds: 2_000),
                cancellation.Token)).ConfigureAwait(false);

        var recovered = await pool.AnalyzeAsync(
            new PythonAnalyzeRequest("after-cancel", "healthy", [2.0, 3.0])).ConfigureAwait(false);
        var recoveredSnapshot = pool.Snapshot;
        await pool.DisposeAsync().ConfigureAwait(false);
        var disposedSnapshot = pool.Snapshot;

        Assert.IsTrue(recovered.Ok);
        Assert.AreEqual(5.0, recovered.Result?.Sum);
        Assert.AreEqual(1L, recoveredSnapshot.Restarts);
        Assert.HasCount(0, disposedSnapshot.ActiveProcessIds);
    }

    private static PythonWorkerPoolOptions CreateOptions(
        int workerCount,
        int queueCapacity,
        int standardErrorTailCharacters = 4 * 1024)
    {
        var root = FindWorkspaceRoot();
        var interpreter = OperatingSystem.IsWindows()
            ? Path.Combine(root, ".venv", "Scripts", "python.exe")
            : Path.Combine(root, ".venv", "bin", "python");
        return new PythonWorkerPoolOptions(
            interpreter,
            Path.Combine(root, "python", "interop_worker.py"),
            root)
        {
            WorkerCount = workerCount,
            QueueCapacity = queueCapacity,
            RequestTimeout = TimeSpan.FromSeconds(4),
            StandardErrorTailCharacters = standardErrorTailCharacters,
        };
    }

    private static string FindWorkspaceRoot()
    {
        foreach (var startingPath in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(startingPath); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "global.json")) &&
                    File.Exists(Path.Combine(directory.FullName, "python", "interop_worker.py")))
                {
                    return directory.FullName;
                }
            }
        }

        throw new DirectoryNotFoundException("Could not locate the LearnDotnetCSharp workspace root.");
    }
}
