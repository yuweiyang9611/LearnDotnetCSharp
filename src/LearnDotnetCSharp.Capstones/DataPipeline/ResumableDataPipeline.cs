using System.Buffers;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;

namespace LearnDotnetCSharp.Capstones.DataPipeline;

/// <summary>
/// Runs a byte-oriented, newline-framed source through bounded concurrent consumers.
/// Processor effects are at-least-once; processors should therefore be idempotent.
/// </summary>
public sealed class ResumableDataPipeline<T>
{
    private static readonly Encoding Utf8 = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: false);
    private readonly JsonPipelineStateStore stateStore;
    private readonly Func<ReadOnlyMemory<byte>, PipelineParseResult<T>> parser;
    private readonly Func<PipelineRecord<T>, CancellationToken, ValueTask> processor;
    private readonly ResumableDataPipelineOptions options;

    public ResumableDataPipeline(
        JsonPipelineStateStore stateStore,
        Func<ReadOnlyMemory<byte>, PipelineParseResult<T>> parser,
        Func<PipelineRecord<T>, CancellationToken, ValueTask> processor,
        ResumableDataPipelineOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stateStore);
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(processor);

        this.stateStore = stateStore;
        this.parser = parser;
        this.processor = processor;
        this.options = options ?? new ResumableDataPipelineOptions();
        this.options.Validate();
    }

    public async ValueTask<PipelineRunResult> RunAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        var fullSourcePath = Path.GetFullPath(sourcePath);
        var fingerprint = await PipelineSourceFingerprint.ComputeAsync(fullSourcePath, cancellationToken)
            .ConfigureAwait(false);
        var initialState = await stateStore.LoadOrCreateAsync(fingerprint, cancellationToken)
            .ConfigureAwait(false);
        var sourceLength = new FileInfo(fullSourcePath).Length;
        if (initialState.Checkpoint.NextByteOffset > sourceLength)
        {
            throw new InvalidDataException(
                $"Checkpoint byte offset {initialState.Checkpoint.NextByteOffset} exceeds source length {sourceLength}.");
        }

        using var coordinator = new PipelineStateCoordinator(stateStore, initialState);
        var channel = Channel.CreateBounded<PipelineRecord<T>>(new BoundedChannelOptions(options.ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = options.ConsumerCount == 1,
            SingleWriter = true,
            AllowSynchronousContinuations = false,
        });
        using var owner = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var failures = new FailureCollector();
        var processedThisRun = 0;
        var newDeadLettersThisRun = 0;

        var consumers = Enumerable.Range(0, options.ConsumerCount)
            .Select(_ => ConsumeAsync(
                channel.Reader,
                coordinator,
                () => Interlocked.Increment(ref processedThisRun),
                owner,
                failures))
            .ToArray();
        var producer = ProduceAsync(
            fullSourcePath,
            initialState.Checkpoint,
            channel.Writer,
            coordinator,
            () => Interlocked.Increment(ref newDeadLettersThisRun),
            owner,
            failures);
        Task[] stages = [.. consumers, producer];

        try
        {
            await Task.WhenAll(stages).ConfigureAwait(false);
        }
        catch (Exception observed)
        {
            await owner.CancelAsync().ConfigureAwait(false);
            try
            {
                await Task.WhenAll(stages).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Every stage is observed. The first non-cancellation failure is rethrown below.
            }

            if (failures.Exception is { } failure)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }

            cancellationToken.ThrowIfCancellationRequested();
            ExceptionDispatchInfo.Capture(observed).Throw();
            throw new InvalidOperationException("Unreachable failure propagation path.");
        }

        var finalState = coordinator.Snapshot;
        return new PipelineRunResult(
            finalState.Checkpoint,
            processedThisRun,
            newDeadLettersThisRun,
            finalState.DeadLetters.Length);
    }

    private async Task ProduceAsync(
        string sourcePath,
        PipelineCheckpoint checkpoint,
        ChannelWriter<PipelineRecord<T>> writer,
        PipelineStateCoordinator coordinator,
        Action recordNewDeadLetter,
        CancellationTokenSource owner,
        FailureCollector failures)
    {
        Exception? completionException = null;
        var readBuffer = ArrayPool<byte>.Shared.Rent(options.ReadBufferBytes);
        var frameBuffer = new ArrayBufferWriter<byte>(Math.Min(options.ReadBufferBytes, options.MaximumFrameBytes));
        var oversized = false;
        var frameStartOffset = checkpoint.NextByteOffset;
        var nextByteOffset = checkpoint.NextByteOffset;
        var nextSequence = checkpoint.LastContiguousSequence + 1;

        try
        {
            await using var stream = new FileStream(
                sourcePath,
                new FileStreamOptions
                {
                    Mode = FileMode.Open,
                    Access = FileAccess.Read,
                    Share = FileShare.Read,
                    BufferSize = options.ReadBufferBytes,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                });
            stream.Seek(checkpoint.NextByteOffset, SeekOrigin.Begin);

            while (true)
            {
                var read = await stream.ReadAsync(
                    readBuffer.AsMemory(0, options.ReadBufferBytes),
                    owner.Token).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                for (var index = 0; index < read; index++)
                {
                    var current = readBuffer[index];
                    nextByteOffset++;
                    if (current == (byte)'\n')
                    {
                        await DispatchFrameAsync(
                            frameBuffer.WrittenMemory,
                            oversized,
                            new PipelinePosition(nextSequence, frameStartOffset, nextByteOffset),
                            writer,
                            coordinator,
                            recordNewDeadLetter,
                            owner.Token).ConfigureAwait(false);
                        frameBuffer.Clear();
                        oversized = false;
                        frameStartOffset = nextByteOffset;
                        nextSequence++;
                    }
                    else if (frameBuffer.WrittenCount < options.MaximumFrameBytes)
                    {
                        frameBuffer.GetSpan(1)[0] = current;
                        frameBuffer.Advance(1);
                    }
                    else
                    {
                        oversized = true;
                    }
                }
            }

            if (frameBuffer.WrittenCount > 0 || oversized)
            {
                await DispatchFrameAsync(
                    frameBuffer.WrittenMemory,
                    oversized,
                    new PipelinePosition(nextSequence, frameStartOffset, nextByteOffset),
                    writer,
                    coordinator,
                    recordNewDeadLetter,
                    owner.Token).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            completionException = exception;
            failures.TryRecord(exception, owner.IsCancellationRequested);
            await owner.CancelAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(readBuffer);
            writer.TryComplete(completionException ?? failures.Exception);
        }
    }

    private async ValueTask DispatchFrameAsync(
        ReadOnlyMemory<byte> bufferedFrame,
        bool oversized,
        PipelinePosition position,
        ChannelWriter<PipelineRecord<T>> writer,
        PipelineStateCoordinator coordinator,
        Action recordNewDeadLetter,
        CancellationToken cancellationToken)
    {
        var frame = bufferedFrame.ToArray();
        var logicalLength = frame.Length > 0 && frame[^1] == (byte)'\r'
            ? frame.Length - 1
            : frame.Length;
        var logicalFrame = frame.AsMemory(0, logicalLength);
        var parsed = oversized
            ? PipelineParse.Reject<T>($"Frame exceeds {options.MaximumFrameBytes} bytes.")
            : parser(logicalFrame);
        if (!parsed.IsSuccess)
        {
            var rawText = Utf8.GetString(logicalFrame.Span);
            if (oversized)
            {
                rawText += "<truncated>";
            }

            var wasAdded = await coordinator.MarkDeadLetterAsync(
                PipelineDeadLetter.Create(
                    coordinator.SourceFingerprint,
                    position,
                    rawText,
                    parsed.Error),
                cancellationToken).ConfigureAwait(false);
            if (wasAdded)
            {
                recordNewDeadLetter();
            }

            return;
        }

        await writer.WriteAsync(
            new PipelineRecord<T>(position, parsed.Value, logicalFrame.ToArray()),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ConsumeAsync(
        ChannelReader<PipelineRecord<T>> reader,
        PipelineStateCoordinator coordinator,
        Action recordProcessed,
        CancellationTokenSource owner,
        FailureCollector failures)
    {
        try
        {
            await foreach (var item in reader.ReadAllAsync(owner.Token).ConfigureAwait(false))
            {
                await processor(item, owner.Token).ConfigureAwait(false);
                await coordinator.MarkProcessedAsync(item.Position, owner.Token).ConfigureAwait(false);
                recordProcessed();
            }
        }
        catch (Exception exception)
        {
            failures.TryRecord(exception, owner.IsCancellationRequested);
            await owner.CancelAsync().ConfigureAwait(false);
            throw;
        }
    }

    private sealed class FailureCollector
    {
        private Exception? exception;

        public Exception? Exception => Volatile.Read(ref exception);

        public void TryRecord(Exception candidate, bool cancellationAlreadyRequested)
        {
            if (candidate is OperationCanceledException && cancellationAlreadyRequested)
            {
                return;
            }

            Interlocked.CompareExchange(ref exception, candidate, comparand: null);
        }
    }

    private sealed class PipelineStateCoordinator : IDisposable
    {
        private readonly JsonPipelineStateStore store;
        private readonly SemaphoreSlim gate = new(1, 1);
        private readonly ContiguousCheckpointTracker tracker;
        private readonly HashSet<string> deadLetterIds;
        private readonly List<PipelineDeadLetter> deadLetters;
        private PipelineCheckpoint checkpoint;

        public PipelineStateCoordinator(JsonPipelineStateStore store, PipelineStateSnapshot initialState)
        {
            this.store = store;
            checkpoint = initialState.Checkpoint;
            deadLetters = [.. initialState.DeadLetters];
            deadLetterIds = new HashSet<string>(
                initialState.DeadLetters.Select(item => item.EntryId),
                StringComparer.Ordinal);
            tracker = new ContiguousCheckpointTracker(
                checkpoint.LastContiguousSequence,
                checkpoint.NextByteOffset);
        }

        public string SourceFingerprint => checkpoint.SourceFingerprint;

        public PipelineStateSnapshot Snapshot
        {
            get
            {
                gate.Wait();
                try
                {
                    return CreateSnapshot();
                }
                finally
                {
                    gate.Release();
                }
            }
        }

        public ValueTask MarkProcessedAsync(PipelinePosition position, CancellationToken cancellationToken) =>
            MarkTerminalAsync(position, deadLetter: null, cancellationToken).AsVoid();

        public ValueTask<bool> MarkDeadLetterAsync(
            PipelineDeadLetter deadLetter,
            CancellationToken cancellationToken) =>
            MarkTerminalAsync(deadLetter.Position, deadLetter, cancellationToken);

        private async ValueTask<bool> MarkTerminalAsync(
            PipelinePosition position,
            PipelineDeadLetter? deadLetter,
            CancellationToken cancellationToken)
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var deadLetterAdded = deadLetter is not null && deadLetterIds.Add(deadLetter.EntryId);
                if (deadLetterAdded)
                {
                    deadLetters.Add(deadLetter!);
                }

                var checkpointAdvanced = tracker.MarkTerminal(position);
                if (checkpointAdvanced)
                {
                    var progress = tracker.Progress;
                    checkpoint = new PipelineCheckpoint(
                        PipelineCheckpoint.CurrentFormatVersion,
                        checkpoint.SourceFingerprint,
                        progress.NextByteOffset,
                        progress.LastContiguousSequence);
                }

                if (deadLetterAdded || checkpointAdvanced)
                {
                    await store.SaveAsync(CreateSnapshot(), cancellationToken).ConfigureAwait(false);
                }

                return deadLetterAdded;
            }
            finally
            {
                gate.Release();
            }
        }

        private PipelineStateSnapshot CreateSnapshot() =>
            new(checkpoint, [.. deadLetters]);

        public void Dispose() => gate.Dispose();
    }
}

public static class PipelineSourceFingerprint
{
    public static async ValueTask<string> ComputeAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        await using var stream = new FileStream(
            Path.GetFullPath(sourcePath),
            new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            });
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return $"sha256:{Convert.ToHexString(hash)}";
    }
}

internal static class ValueTaskExtensions
{
    public static async ValueTask AsVoid(this ValueTask<bool> task) =>
        _ = await task.ConfigureAwait(false);
}
