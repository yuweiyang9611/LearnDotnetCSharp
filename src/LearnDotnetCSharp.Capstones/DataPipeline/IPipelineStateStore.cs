namespace LearnDotnetCSharp.Capstones.DataPipeline;

/// <summary>Durable state for one pipeline owner. Processor effects remain at-least-once.</summary>
public interface IPipelineStateStore
{
    ValueTask<PipelineStoredState> InitializeAsync(string sourceFingerprint, CancellationToken cancellationToken = default);

    ValueTask<PipelineCommitResult> CommitAsync(
        PipelineCheckpoint checkpoint,
        PipelineDeadLetter? deadLetter,
        CancellationToken cancellationToken = default);

    ValueTask ExportDeadLettersAsync(string destinationPath, CancellationToken cancellationToken = default);
}

public sealed record PipelineStoredState(PipelineCheckpoint Checkpoint, int TotalDeadLetters);

public sealed record PipelineCommitResult(PipelineStoredState State, bool DeadLetterAdded);

internal static class PipelineStateValidation
{
    public static void ValidateCommit(PipelineCheckpoint previous, PipelineCheckpoint next, PipelineDeadLetter? deadLetter)
    {
        ArgumentNullException.ThrowIfNull(next);
        if (previous.SourceFingerprint != next.SourceFingerprint)
        {
            throw new PipelineSourceMismatchException(previous.SourceFingerprint, next.SourceFingerprint);
        }

        if (next.LastContiguousSequence < previous.LastContiguousSequence || next.NextByteOffset < previous.NextByteOffset ||
            ((next.LastContiguousSequence == previous.LastContiguousSequence) != (next.NextByteOffset == previous.NextByteOffset)))
        {
            throw new InvalidDataException("A checkpoint must advance both sequence and byte offset, or neither.");
        }

        if (deadLetter is not null && (deadLetter.SourceFingerprint != next.SourceFingerprint ||
            deadLetter.EntryId != PipelineDeadLetter.Create(next.SourceFingerprint, deadLetter.Position, deadLetter.RawText, deadLetter.Reason).EntryId))
        {
            throw new InvalidDataException("The dead letter has an inconsistent source or identity.");
        }
    }
}
