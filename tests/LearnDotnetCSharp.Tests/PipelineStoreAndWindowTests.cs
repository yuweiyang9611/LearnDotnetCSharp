using System.Text;
using LearnDotnetCSharp.Capstones.DataPipeline;
using Microsoft.Data.Sqlite;

namespace LearnDotnetCSharp.Tests;

[TestClass]
public sealed class PipelineStoreAndWindowTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task StoresResumeDeduplicateAndRejectInvalidCommits(bool sqlite)
    {
        using var files = new Files();
        var store = files.Store(sqlite);
        var initial = await store.InitializeAsync("source");
        var letter = PipelineDeadLetter.Create("source", new PipelinePosition(2, 2, 4), "bad", "invalid");
        var first = await store.CommitAsync(initial.Checkpoint, letter);
        var duplicate = await store.CommitAsync(initial.Checkpoint, letter);
        Assert.IsTrue(first.DeadLetterAdded);
        Assert.IsFalse(duplicate.DeadLetterAdded);
        var complete = new PipelineCheckpoint(1, "source", 4, 2);
        await store.CommitAsync(complete, null);
        var reopened = files.Store(sqlite);
        var restored = await reopened.InitializeAsync("source");
        Assert.AreEqual(complete, restored.Checkpoint);
        Assert.AreEqual(1, restored.TotalDeadLetters);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(async () => await reopened.CommitAsync(initial.Checkpoint, null));
        await Assert.ThrowsExactlyAsync<PipelineSourceMismatchException>(async () => await reopened.InitializeAsync("different"));
        await reopened.ExportDeadLettersAsync(files.Export);
        Assert.HasCount(1, await File.ReadAllLinesAsync(files.Export));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task StoresRecoverPipelineAfterProcessorFailure(bool sqlite)
    {
        using var files = new Files();
        await File.WriteAllTextAsync(files.Source, "1\nbad\n3\n4\n");
        var effects = new HashSet<string>();
        var options = new ResumableDataPipelineOptions { ConsumerCount = 1, MaxUncommittedRecords = 1 };
        var failed = new ResumableDataPipeline<string>(files.Store(sqlite), Parse,
            (record, _) => record.Value == "3" ? ValueTask.FromException(new IOException("injected")) : Record(record), options);
        await Assert.ThrowsExactlyAsync<IOException>(async () => await failed.RunAsync(files.Source));
        var resumed = new ResumableDataPipeline<string>(files.Store(sqlite), Parse, (record, _) => Record(record), options);
        var result = await resumed.RunAsync(files.Source);
        Assert.AreEqual(4L, result.Checkpoint.LastContiguousSequence);
        Assert.AreEqual(1, result.TotalDeadLetters);
        Assert.HasCount(3, effects);

        ValueTask Record(PipelineRecord<string> record)
        {
            Assert.IsTrue(effects.Add(record.Value));
            return ValueTask.CompletedTask;
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task WindowBoundsOutOfOrderFramesAndCancellation(bool deadLetters)
    {
        using var files = new Files();
        await File.WriteAllTextAsync(files.Source, "first\n" + string.Concat(Enumerable.Repeat(deadLetters ? "bad\n" : "next\n", 200)));
        var reachedWindow = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exceededWindow = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var parsed = 0;
        var pipeline = new ResumableDataPipeline<string>(files.Store(true), bytes =>
        {
            var count = Interlocked.Increment(ref parsed);
            if (count == 4) reachedWindow.TrySetResult();
            if (count > 4) exceededWindow.TrySetResult();
            return Parse(bytes);
        }, async (record, token) =>
        {
            if (record.Value == "first") await release.Task.WaitAsync(token);
        }, new ResumableDataPipelineOptions { ConsumerCount = 2, ChannelCapacity = 1, MaxUncommittedRecords = 4 });
        var running = pipeline.RunAsync(files.Source, cancellation.Token).AsTask();
        try
        {
            await reachedWindow.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsExactlyAsync<TimeoutException>(() => exceededWindow.Task.WaitAsync(TimeSpan.FromMilliseconds(150)));
            Assert.AreEqual(4, Volatile.Read(ref parsed));
            await cancellation.CancelAsync();
            await Assert.ThrowsAsync<OperationCanceledException>(() => running.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            release.TrySetResult();
            await cancellation.CancelAsync();
        }

        var resumed = new ResumableDataPipeline<string>(files.Store(true), Parse, static (_, _) => ValueTask.CompletedTask,
            new ResumableDataPipelineOptions { MaxUncommittedRecords = 1 });
        Assert.AreEqual(201L, (await resumed.RunAsync(files.Source)).Checkpoint.LastContiguousSequence);
    }

    [TestMethod]
    public async Task SqliteRollsBackDeadLetterWhenCheckpointWriteFails()
    {
        using var files = new Files();
        var store = files.Store(true);
        var initial = await store.InitializeAsync("source");
        await using (var connection = new SqliteConnection($"Data Source={files.Database};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TRIGGER fail_commit BEFORE UPDATE ON pipeline_state BEGIN SELECT RAISE(ABORT, 'injected'); END;";
            await command.ExecuteNonQueryAsync();
        }

        await Assert.ThrowsExactlyAsync<SqliteException>(async () => await store.CommitAsync(new PipelineCheckpoint(1, "source", 4, 1),
            PipelineDeadLetter.Create("source", new PipelinePosition(1, 0, 4), "bad", "invalid")));
        Assert.AreEqual(initial, await files.Store(true).InitializeAsync("source"));
        await store.ExportDeadLettersAsync(files.Export);
        Assert.AreEqual("", await File.ReadAllTextAsync(files.Export));
    }

    [TestMethod]
    public async Task SqliteCommitsDoNotRewriteHistoricalDeadLetters()
    {
        using var files = new Files();
        var store = files.Store(true);
        await store.InitializeAsync("source");
        await using (var connection = new SqliteConnection($"Data Source={files.Database};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TRIGGER no_update BEFORE UPDATE ON pipeline_dead_letters BEGIN SELECT RAISE(ABORT, 'rewrite'); END;
                CREATE TRIGGER no_delete BEFORE DELETE ON pipeline_dead_letters BEGIN SELECT RAISE(ABORT, 'rewrite'); END;
                """;
            await command.ExecuteNonQueryAsync();
        }

        for (var i = 1; i <= 1000; i++)
        {
            await store.CommitAsync(new PipelineCheckpoint(1, "source", i * 4, i),
                PipelineDeadLetter.Create("source", new PipelinePosition(i, (i - 1) * 4, i * 4), "bad", "invalid"));
        }

        Assert.AreEqual(1000, (await files.Store(true).InitializeAsync("source")).TotalDeadLetters);
        Assert.IsFalse(File.Exists(files.Export));
        await store.ExportDeadLettersAsync(files.Export);
        Assert.HasCount(1000, await File.ReadAllLinesAsync(files.Export));
    }

    [TestMethod]
    public async Task PersistenceFailureCancelsProducerWaitingForWindow()
    {
        using var files = new Files();
        await File.WriteAllTextAsync(files.Source, "first\nnext\nnext\n");
        var pipeline = new ResumableDataPipeline<string>(new FailingStore(), Parse, static (_, _) => ValueTask.CompletedTask,
            new ResumableDataPipelineOptions { MaxUncommittedRecords = 1 });
        await Assert.ThrowsExactlyAsync<IOException>(() => pipeline.RunAsync(files.Source).AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
    }

    private static PipelineParseResult<string> Parse(ReadOnlyMemory<byte> bytes)
    {
        var text = Encoding.UTF8.GetString(bytes.Span);
        return text == "bad" ? PipelineParse.Reject<string>("invalid") : PipelineParse.Success(text);
    }

    private sealed class FailingStore : IPipelineStateStore
    {
        public ValueTask<PipelineStoredState> InitializeAsync(string sourceFingerprint, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new PipelineStoredState(PipelineCheckpoint.Initial(sourceFingerprint), 0));
        public ValueTask<PipelineCommitResult> CommitAsync(PipelineCheckpoint checkpoint, PipelineDeadLetter? deadLetter, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<PipelineCommitResult>(new IOException("injected persistence failure"));
        public ValueTask ExportDeadLettersAsync(string destinationPath, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class Files : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "LearnDotnetCSharp.Tests", Guid.NewGuid().ToString("N"));
        public Files() => Directory.CreateDirectory(root);
        public string Database => Path.Combine(root, "state.db");
        public string Source => Path.Combine(root, "source.txt");
        public string Export => Path.Combine(root, "export.ndjson");
        public IPipelineStateStore Store(bool sqlite) => sqlite ? new SqlitePipelineStateStore(Database) : new JsonPipelineStateStore(Path.Combine(root, "state.json"));
        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}
