using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using LearnDotnetCSharp.Capstones.DataPipeline;

namespace LearnDotnetCSharp.Tests;

[TestClass]
public sealed class ResumableDataPipelineTests
{
    private static readonly int[] ExpectedValues = [1, 2, 3, 4];

    [TestMethod]
    public void ContiguousTrackerDoesNotAdvanceAcrossAnOutOfOrderGap()
    {
        var tracker = new ContiguousCheckpointTracker();

        var advancedBySecond = tracker.MarkTerminal(new PipelinePosition(2, 2, 4));

        Assert.IsFalse(advancedBySecond);
        Assert.AreEqual(new PipelineProgress(0, 0), tracker.Progress);

        var advancedByFirst = tracker.MarkTerminal(new PipelinePosition(1, 0, 2));

        Assert.IsTrue(advancedByFirst);
        Assert.AreEqual(new PipelineProgress(2, 4), tracker.Progress);
    }

    [TestMethod]
    public async Task DeterministicProcessorFailureCanResumeFromTheDurablePrefix()
    {
        using var files = new PipelineTestFiles("1\n2\n3\n4\n");
        var effects = new ConcurrentDictionary<int, byte>();
        var failureInjected = 0;
        var firstPipeline = CreatePipeline(
            files,
            (record, _) =>
            {
                if (record.Value == 3 && Interlocked.CompareExchange(ref failureInjected, 1, 0) == 0)
                {
                    return ValueTask.FromException(new InjectedPipelineException());
                }

                effects.TryAdd(record.Value, 0);
                return ValueTask.CompletedTask;
            });

        await Assert.ThrowsExactlyAsync<InjectedPipelineException>(
            async () => await firstPipeline.RunAsync(files.SourcePath));

        var fingerprint = await PipelineSourceFingerprint.ComputeAsync(files.SourcePath);
        var interruptedState = await new JsonPipelineStateStore(files.StatePath)
            .LoadOrCreateAsync(fingerprint);
        Assert.IsLessThan(
            new FileInfo(files.SourcePath).Length,
            interruptedState.Checkpoint.NextByteOffset);

        var resumedPipeline = CreatePipeline(
            files,
            (record, _) =>
            {
                effects.TryAdd(record.Value, 0);
                return ValueTask.CompletedTask;
            });
        var result = await resumedPipeline.RunAsync(files.SourcePath);

        Assert.AreEqual(4, result.Checkpoint.LastContiguousSequence);
        Assert.AreEqual(new FileInfo(files.SourcePath).Length, result.Checkpoint.NextByteOffset);
        CollectionAssert.AreEquivalent(ExpectedValues, effects.Keys.ToArray());
    }

    [TestMethod]
    public async Task DeadLetterIsWrittenOnlyOnceWhenAFailedPrefixIsReplayed()
    {
        using var files = new PipelineTestFiles("1\nbad\n2\n");
        var processorEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var frameAfterDeadLetterParsed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFailure = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstPipeline = new ResumableDataPipeline<int>(
            new JsonPipelineStateStore(files.StatePath),
            bytes =>
            {
                var text = Encoding.UTF8.GetString(bytes.Span);
                if (text == "2")
                {
                    frameAfterDeadLetterParsed.TrySetResult();
                }

                return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
                    ? PipelineParse.Success(value)
                    : PipelineParse.Reject<int>("not an integer");
            },
            async (record, cancellationToken) =>
            {
                if (record.Value == 1)
                {
                    processorEntered.TrySetResult();
                    await releaseFailure.Task.WaitAsync(cancellationToken);
                    throw new InjectedPipelineException();
                }
            },
            TestOptions);

        var firstRun = firstPipeline.RunAsync(files.SourcePath).AsTask();
        await processorEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await frameAfterDeadLetterParsed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        releaseFailure.TrySetResult();
        await Assert.ThrowsExactlyAsync<InjectedPipelineException>(async () => await firstRun);

        var resumedPipeline = CreatePipeline(files, static (_, _) => ValueTask.CompletedTask);
        var result = await resumedPipeline.RunAsync(files.SourcePath);
        var fingerprint = await PipelineSourceFingerprint.ComputeAsync(files.SourcePath);
        var finalState = await new JsonPipelineStateStore(files.StatePath)
            .LoadOrCreateAsync(fingerprint);
        var ndjsonLines = await File.ReadAllLinesAsync(files.DeadLetterPath);

        Assert.AreEqual(1, result.TotalDeadLetters);
        Assert.HasCount(1, finalState.DeadLetters);
        Assert.AreEqual("bad", finalState.DeadLetters[0].RawText);
        Assert.HasCount(1, ndjsonLines);
    }

    [TestMethod]
    public async Task ReusingStateForDifferentSourceFingerprintFailsClosed()
    {
        using var files = new PipelineTestFiles("1\n2\n");
        var firstPipeline = CreatePipeline(files, static (_, _) => ValueTask.CompletedTask);
        await firstPipeline.RunAsync(files.SourcePath);
        await File.WriteAllTextAsync(files.SourcePath, "1\n2\n3\n", new UTF8Encoding(false));

        var processorInvoked = false;
        var changedSourcePipeline = CreatePipeline(
            files,
            (_, _) =>
            {
                processorInvoked = true;
                return ValueTask.CompletedTask;
            });

        await Assert.ThrowsExactlyAsync<PipelineSourceMismatchException>(
            async () => await changedSourcePipeline.RunAsync(files.SourcePath));
        Assert.IsFalse(processorInvoked);
    }

    private static readonly ResumableDataPipelineOptions TestOptions = new()
    {
        ChannelCapacity = 1,
        ConsumerCount = 2,
        ReadBufferBytes = 3,
        MaximumFrameBytes = 64,
    };

    private static ResumableDataPipeline<int> CreatePipeline(
        PipelineTestFiles files,
        Func<PipelineRecord<int>, CancellationToken, ValueTask> processor) =>
        new(
            new JsonPipelineStateStore(files.StatePath),
            static bytes =>
            {
                var text = Encoding.UTF8.GetString(bytes.Span);
                return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
                    ? PipelineParse.Success(value)
                    : PipelineParse.Reject<int>("not an integer");
            },
            processor,
            TestOptions);

    private sealed class PipelineTestFiles : IDisposable
    {
        private readonly string directory;

        public PipelineTestFiles(string source)
        {
            directory = Path.Combine(
                Path.GetTempPath(),
                "LearnDotnetCSharp.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            SourcePath = Path.Combine(directory, "source.txt");
            StatePath = Path.Combine(directory, "pipeline-state.json");
            DeadLetterPath = Path.Combine(directory, "pipeline-state.dead-letter.ndjson");
            File.WriteAllText(SourcePath, source, new UTF8Encoding(false));
        }

        public string SourcePath { get; }

        public string StatePath { get; }

        public string DeadLetterPath { get; }

        public void Dispose() => Directory.Delete(directory, recursive: true);
    }

    private sealed class InjectedPipelineException : Exception;
}
