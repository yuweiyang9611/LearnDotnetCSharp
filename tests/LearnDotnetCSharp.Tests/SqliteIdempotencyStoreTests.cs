using LearnDotnetCSharp.Capstones.LocalService;

namespace LearnDotnetCSharp.Tests;

[TestClass]
public sealed class SqliteIdempotencyStoreTests
{
    [TestMethod]
    public async Task SameKeyAndHashReplaysExactPayloadWithoutRunningFactoryAgain()
    {
        using var database = new TemporaryDatabase();
        await using var store = new SqliteIdempotencyStore(database.Path);
        await store.InitializeAsync();
        byte[] expected = [0, 1, 255, 0, 42];
        var factoryCalls = 0;

        var executed = await store.ExecuteAsync(
            "job-42",
            "hash-a",
            _ =>
            {
                Interlocked.Increment(ref factoryCalls);
                return ValueTask.FromResult(expected);
            });
        var firstPayload = executed.Payload!;
        firstPayload[0] = 99;

        var replayed = await store.ExecuteAsync(
            "job-42",
            "hash-a",
            _ =>
            {
                Interlocked.Increment(ref factoryCalls);
                return ValueTask.FromResult<byte[]>([9, 9, 9]);
            });

        Assert.AreEqual(IdempotencyOutcome.Executed, executed.Outcome);
        Assert.AreEqual(IdempotencyOutcome.Replayed, replayed.Outcome);
        Assert.AreEqual(1, factoryCalls);
        CollectionAssert.AreEqual(expected, replayed.Payload);
    }

    [TestMethod]
    public async Task SameKeyAndDifferentHashReturnsConflictWithoutRunningFactory()
    {
        using var database = new TemporaryDatabase();
        await using var store = new SqliteIdempotencyStore(database.Path);
        await store.InitializeAsync();
        await store.ExecuteAsync("job-42", "hash-a", _ => ValueTask.FromResult<byte[]>([1, 2, 3]));
        var conflictingFactoryCalls = 0;

        var result = await store.ExecuteAsync(
            "job-42",
            "hash-b",
            _ =>
            {
                Interlocked.Increment(ref conflictingFactoryCalls);
                return ValueTask.FromResult<byte[]>([4, 5, 6]);
            });

        Assert.AreEqual(IdempotencyOutcome.Conflict, result.Outcome);
        Assert.IsNull(result.Payload);
        Assert.AreEqual(0, conflictingFactoryCalls);
    }

    [TestMethod]
    public async Task ConcurrentSameKeyCallsUseOneFactoryAcrossStoreInstances()
    {
        using var database = new TemporaryDatabase();
        await using var firstStore = new SqliteIdempotencyStore(database.Path);
        await using var secondStore = new SqliteIdempotencyStore(database.Path);
        await firstStore.InitializeAsync();
        await secondStore.InitializeAsync();
        var factoryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFactory = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryCalls = 0;

        async ValueTask<byte[]> CreatePayloadAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref factoryCalls);
            factoryStarted.TrySetResult();
            await releaseFactory.Task.WaitAsync(cancellationToken);
            return [7, 8, 9];
        }

        var calls = Enumerable.Range(0, 32)
            .Select(index => (index & 1) == 0 ? firstStore : secondStore)
            .Select(store => store.ExecuteAsync("shared-key", "shared-hash", CreatePayloadAsync))
            .ToArray();

        await factoryStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        releaseFactory.TrySetResult();
        var results = await Task.WhenAll(calls);

        Assert.AreEqual(1, factoryCalls);
        Assert.AreEqual(1, results.Count(result => result.Outcome == IdempotencyOutcome.Executed));
        Assert.AreEqual(31, results.Count(result => result.Outcome == IdempotencyOutcome.Replayed));
        byte[] expected = [7, 8, 9];
        Assert.IsTrue(results.All(result => result.Payload!.SequenceEqual(expected)));
    }

    [TestMethod]
    public async Task DisposeAndReopenReplaysPersistedPayload()
    {
        using var database = new TemporaryDatabase();
        await using (var writer = new SqliteIdempotencyStore(database.Path))
        {
            await writer.InitializeAsync();
            var result = await writer.ExecuteAsync(
                "durable-key",
                "durable-hash",
                _ => ValueTask.FromResult<byte[]>([10, 20, 30]));
            Assert.AreEqual(IdempotencyOutcome.Executed, result.Outcome);
        }

        await using var reader = new SqliteIdempotencyStore(database.Path);
        await reader.InitializeAsync();
        var factoryCalls = 0;
        var replayed = await reader.ExecuteAsync(
            "durable-key",
            "durable-hash",
            _ =>
            {
                Interlocked.Increment(ref factoryCalls);
                return ValueTask.FromResult<byte[]>([99]);
            });

        Assert.AreEqual(IdempotencyOutcome.Replayed, replayed.Outcome);
        Assert.AreEqual(0, factoryCalls);
        CollectionAssert.AreEqual(new byte[] { 10, 20, 30 }, replayed.Payload);
    }

    [TestMethod]
    public async Task FactoryExceptionDoesNotLeaveACompletedRow()
    {
        using var database = new TemporaryDatabase();
        await using var store = new SqliteIdempotencyStore(database.Path);
        await store.InitializeAsync();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => store.ExecuteAsync(
            "retry-after-error",
            "same-hash",
            _ => ValueTask.FromException<byte[]>(new InvalidOperationException("injected failure"))));

        var retried = await store.ExecuteAsync(
            "retry-after-error",
            "same-hash",
            _ => ValueTask.FromResult<byte[]>([4, 2]));

        Assert.AreEqual(IdempotencyOutcome.Executed, retried.Outcome);
        CollectionAssert.AreEqual(new byte[] { 4, 2 }, retried.Payload);
    }

    [TestMethod]
    public async Task FactoryCancellationDoesNotLeaveACompletedRow()
    {
        using var database = new TemporaryDatabase();
        await using var store = new SqliteIdempotencyStore(database.Path);
        await store.InitializeAsync();
        using var cancellation = new CancellationTokenSource();

        async ValueTask<byte[]> CancelFactoryAsync(CancellationToken cancellationToken)
        {
            await cancellation.CancelAsync();
            cancellationToken.ThrowIfCancellationRequested();
            return [];
        }

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => store.ExecuteAsync(
            "retry-after-cancellation",
            "same-hash",
            CancelFactoryAsync,
            cancellation.Token));

        var retried = await store.ExecuteAsync(
            "retry-after-cancellation",
            "same-hash",
            _ => ValueTask.FromResult<byte[]>([8, 4]));

        Assert.AreEqual(IdempotencyOutcome.Executed, retried.Outcome);
        CollectionAssert.AreEqual(new byte[] { 8, 4 }, retried.Payload);
    }

    private sealed class TemporaryDatabase : IDisposable
    {
        private readonly string directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "LearnDotnetCSharp.Tests",
            Guid.NewGuid().ToString("N"));

        public TemporaryDatabase()
        {
            Directory.CreateDirectory(directory);
            Path = System.IO.Path.Combine(directory, "idempotency.db");
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
