namespace LearnDotnetCSharp.Capstones.DataPipeline;

/// <summary>
/// Tracks terminal frames completed by concurrent consumers without skipping a gap.
/// </summary>
public sealed class ContiguousCheckpointTracker
{
    private readonly object sync = new();
    private readonly SortedDictionary<long, PipelinePosition> pending = [];
    private long lastContiguousSequence;
    private long nextByteOffset;

    public ContiguousCheckpointTracker(long lastContiguousSequence = 0, long nextByteOffset = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(lastContiguousSequence);
        ArgumentOutOfRangeException.ThrowIfNegative(nextByteOffset);

        this.lastContiguousSequence = lastContiguousSequence;
        this.nextByteOffset = nextByteOffset;
    }

    public PipelineProgress Progress
    {
        get
        {
            lock (sync)
            {
                return new PipelineProgress(lastContiguousSequence, nextByteOffset);
            }
        }
    }

    /// <returns><see langword="true"/> only when the durable contiguous prefix advanced.</returns>
    public bool MarkTerminal(PipelinePosition position)
    {
        lock (sync)
        {
            if (position.Sequence <= lastContiguousSequence)
            {
                throw new InvalidOperationException(
                    $"Sequence {position.Sequence} is already behind checkpoint {lastContiguousSequence}.");
            }

            if (pending.TryGetValue(position.Sequence, out var existing))
            {
                if (existing != position)
                {
                    throw new InvalidDataException(
                        $"Sequence {position.Sequence} was observed with conflicting byte offsets.");
                }

                return false;
            }

            pending.Add(position.Sequence, position);
            var advanced = false;
            while (pending.TryGetValue(lastContiguousSequence + 1, out var next))
            {
                if (next.StartByteOffset != nextByteOffset)
                {
                    throw new InvalidDataException(
                        $"Sequence {next.Sequence} starts at byte {next.StartByteOffset}, " +
                        $"but the contiguous prefix ends at byte {nextByteOffset}.");
                }

                pending.Remove(next.Sequence);
                lastContiguousSequence = next.Sequence;
                nextByteOffset = next.NextByteOffset;
                advanced = true;
            }

            return advanced;
        }
    }
}
