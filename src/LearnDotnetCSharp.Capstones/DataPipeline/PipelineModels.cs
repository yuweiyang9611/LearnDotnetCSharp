namespace LearnDotnetCSharp.Capstones.DataPipeline;

/// <summary>
/// Identifies one physical frame in a byte-oriented source.
/// </summary>
public readonly record struct PipelinePosition
{
    public PipelinePosition(long sequence, long startByteOffset, long nextByteOffset)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sequence, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(startByteOffset);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(nextByteOffset, startByteOffset);

        Sequence = sequence;
        StartByteOffset = startByteOffset;
        NextByteOffset = nextByteOffset;
    }

    public long Sequence { get; }

    public long StartByteOffset { get; }

    public long NextByteOffset { get; }
}

/// <summary>
/// The durable high-water mark. Only a contiguous prefix of terminal frames may advance it.
/// </summary>
public sealed record PipelineCheckpoint
{
    public const int CurrentFormatVersion = 1;

    public PipelineCheckpoint(
        int formatVersion,
        string sourceFingerprint,
        long nextByteOffset,
        long lastContiguousSequence)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(formatVersion, CurrentFormatVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFingerprint);
        ArgumentOutOfRangeException.ThrowIfNegative(nextByteOffset);
        ArgumentOutOfRangeException.ThrowIfNegative(lastContiguousSequence);

        FormatVersion = formatVersion;
        SourceFingerprint = sourceFingerprint;
        NextByteOffset = nextByteOffset;
        LastContiguousSequence = lastContiguousSequence;
    }

    public int FormatVersion { get; }

    public string SourceFingerprint { get; }

    public long NextByteOffset { get; }

    public long LastContiguousSequence { get; }

    public static PipelineCheckpoint Initial(string sourceFingerprint) =>
        new(CurrentFormatVersion, sourceFingerprint, nextByteOffset: 0, lastContiguousSequence: 0);
}

/// <summary>
/// A rejected physical frame. EntryId is stable across retries of the same source.
/// </summary>
public sealed record PipelineDeadLetter(
    string EntryId,
    string SourceFingerprint,
    PipelinePosition Position,
    string RawText,
    string Reason)
{
    public static PipelineDeadLetter Create(
        string sourceFingerprint,
        PipelinePosition position,
        string rawText,
        string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFingerprint);
        ArgumentNullException.ThrowIfNull(rawText);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        var entryId = $"{sourceFingerprint}:{position.StartByteOffset:X16}";
        return new PipelineDeadLetter(entryId, sourceFingerprint, position, rawText, reason);
    }
}

/// <summary>
/// The authoritative state written atomically as one JSON document.
/// </summary>
public sealed record PipelineStateSnapshot(
    PipelineCheckpoint Checkpoint,
    PipelineDeadLetter[] DeadLetters);

public readonly record struct PipelineProgress(long LastContiguousSequence, long NextByteOffset);

public sealed record PipelineRecord<T>(
    PipelinePosition Position,
    T Value,
    ReadOnlyMemory<byte> RawBytes);

/// <summary>
/// Represents either a parsed value or a reason suitable for the dead-letter stream.
/// </summary>
public sealed class PipelineParseResult<T>
{
    private readonly T? value;
    private readonly string? error;

    internal PipelineParseResult(bool isSuccess, T? value, string? error)
    {
        IsSuccess = isSuccess;
        this.value = value;
        this.error = error;
    }

    public bool IsSuccess { get; }

    public T Value => IsSuccess
        ? value!
        : throw new InvalidOperationException("A rejected parse result has no value.");

    public string Error => !IsSuccess
        ? error!
        : throw new InvalidOperationException("A successful parse result has no error.");

}

public static class PipelineParse
{
    public static PipelineParseResult<T> Success<T>(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new PipelineParseResult<T>(isSuccess: true, value, error: null);
    }

    public static PipelineParseResult<T> Reject<T>(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new PipelineParseResult<T>(isSuccess: false, value: default, error);
    }
}

public sealed record ResumableDataPipelineOptions
{
    public int ChannelCapacity { get; init; } = 2;

    public int ConsumerCount { get; init; } = 3;

    public int ReadBufferBytes { get; init; } = 256;

    public int MaximumFrameBytes { get; init; } = 64 * 1024;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ChannelCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(ConsumerCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(ReadBufferBytes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumFrameBytes, 1);
    }
}

public sealed record PipelineRunResult(
    PipelineCheckpoint Checkpoint,
    int ProcessedThisRun,
    int NewDeadLettersThisRun,
    int TotalDeadLetters);

public sealed class PipelineSourceMismatchException(string expectedFingerprint, string actualFingerprint)
    : IOException(
        $"The checkpoint belongs to source '{expectedFingerprint}', but the current source is '{actualFingerprint}'.")
{
    public string ExpectedFingerprint { get; } = expectedFingerprint;

    public string ActualFingerprint { get; } = actualFingerprint;
}
