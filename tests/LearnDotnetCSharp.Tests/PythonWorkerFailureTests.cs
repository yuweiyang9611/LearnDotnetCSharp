using System.Diagnostics;
using LearnDotnetCSharp.Capstones.Polyglot;

namespace LearnDotnetCSharp.Tests;

[TestClass]
public sealed class PythonWorkerFailureTests
{
    [TestMethod]
    [DataRow("silent")]
    [DataRow("exit")]
    public async Task StartupFailureTerminatesOtherWorkers(string mode)
    {
        using var fixture = new WorkerFixture(mode);
        var error = await Assert.ThrowsExactlyAsync<PythonWorkerException>(() =>
            PythonWorkerPool.StartAsync(fixture.Options).WaitAsync(TimeSpan.FromSeconds(8)));
        Assert.IsNotNull(error.InnerException);
        fixture.AssertExited();
    }

    [TestMethod]
    public async Task RestartFailureCompletesActiveQueuedAndFutureRequests()
    {
        using var fixture = new WorkerFixture("restart");
        await using var pool = await PythonWorkerPool.StartAsync(fixture.Options);
        var active = pool.AnalyzeAsync(new PythonAnalyzeRequest("active", "test", [1.0]));
        var queued = pool.AnalyzeAsync(new PythonAnalyzeRequest("queued", "test", [2.0]));
        await Assert.ThrowsExactlyAsync<PythonWorkerException>(() => active.WaitAsync(TimeSpan.FromSeconds(8)));
        await Assert.ThrowsExactlyAsync<PythonWorkerException>(() => queued.WaitAsync(TimeSpan.FromSeconds(8)));
        await Assert.ThrowsExactlyAsync<PythonWorkerException>(() => pool.AnalyzeAsync(new PythonAnalyzeRequest("active", "again", [1.0])));
        await pool.DisposeAsync();
        Assert.HasCount(0, pool.Snapshot.ActiveProcessIds);
        fixture.AssertExited();
    }

    [TestMethod]
    public async Task ConcurrentDisposeCompletesAllRequestsAndWaitsForCleanup()
    {
        using var fixture = new WorkerFixture("hold");
        var pool = await PythonWorkerPool.StartAsync(fixture.Options);
        var active = pool.AnalyzeAsync(new PythonAnalyzeRequest("active", "test", [1.0]));
        var queued = pool.AnalyzeAsync(new PythonAnalyzeRequest("queued", "test", [2.0]));
        var first = pool.DisposeAsync().AsTask();
        var second = pool.DisposeAsync().AsTask();
        Assert.AreSame(first, second);
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(8));
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => active);
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => queued);
        fixture.AssertExited();
    }

    [TestMethod]
    public async Task CancelledStartupCleansUpSilentWorker()
    {
        using var fixture = new WorkerFixture("silent");
        using var cancellation = new CancellationTokenSource();
        var starting = PythonWorkerPool.StartAsync(fixture.Options, cancellation.Token);
        await cancellation.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => starting.WaitAsync(TimeSpan.FromSeconds(8)));
        fixture.AssertExited();
    }

    private sealed class WorkerFixture : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "LearnDotnetCSharp.Tests", Guid.NewGuid().ToString("N"));
        public WorkerFixture(string mode)
        {
            Directory.CreateDirectory(directory);
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "global.json"))) root = root.Parent;
            Assert.IsNotNull(root);
            var interpreter = Path.Combine(root.FullName, ".venv", OperatingSystem.IsWindows() ? "Scripts/python.exe" : "bin/python");
            var script = Path.Combine(directory, "worker.py");
            File.WriteAllText(script, "mode = '" + mode + "'\n" + """
                import json, os, sys, time
                generation = int(sys.argv[-1])
                with open('pid-' + str(os.getpid()), 'w') as output:
                    output.write(str(os.getpid()))
                if mode == 'silent':
                    time.sleep(60)
                if mode == 'exit' or (mode == 'restart' and generation > 1):
                    sys.exit(2)
                for line in sys.stdin:
                    request = json.loads(line)
                    if request['operation'] != 'handshake':
                        if mode == 'restart':
                            sys.exit(3)
                        time.sleep(60)
                    print(json.dumps({'id': request['id'], 'ok': True, 'runtime': {
                        'version': sys.version, 'executable': sys.executable, 'prefix': sys.prefix,
                        'basePrefix': sys.base_prefix, 'inVenv': True,
                        'processId': os.getpid(), 'generation': generation}}), flush=True)
                """);
            Options = new PythonWorkerPoolOptions(interpreter, script, directory)
            {
                WorkerCount = mode == "exit" ? 2 : 1,
                QueueCapacity = 1,
                StartupTimeout = mode == "silent" ? TimeSpan.FromMilliseconds(800) : TimeSpan.FromSeconds(5),
                RequestTimeout = TimeSpan.FromSeconds(3),
            };
        }

        public PythonWorkerPoolOptions Options { get; }

        public void AssertExited()
        {
            foreach (var path in Directory.EnumerateFiles(directory, "pid-*"))
            {
                var id = int.Parse(File.ReadAllText(path), System.Globalization.CultureInfo.InvariantCulture);
                try
                {
                    using var process = Process.GetProcessById(id);
                    Assert.IsTrue(process.HasExited, $"Worker {id} was not reaped.");
                }
                catch (ArgumentException)
                {
                    // The OS already removed the process.
                }
            }
        }

        public void Dispose() => Directory.Delete(directory, recursive: true);
    }
}
