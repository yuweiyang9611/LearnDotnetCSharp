using System.Text;
using System.Text.Json;

namespace LearnDotnetCSharp.Capstones.DataPipeline;

/// <summary>
/// Persists the checkpoint and deduplicated dead letters in one authoritative JSON snapshot.
/// A derived NDJSON file is atomically regenerated for convenient inspection.
/// </summary>
public sealed class JsonPipelineStateStore
{
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private static readonly JsonSerializerOptions LineJsonOptions = new(JsonSerializerDefaults.Web);

    public JsonPipelineStateStore(string statePath, string? deadLetterPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statePath);

        StatePath = Path.GetFullPath(statePath);
        var directory = Path.GetDirectoryName(StatePath)
            ?? throw new ArgumentException("The state path must have a parent directory.", nameof(statePath));
        DeadLetterPath = deadLetterPath is null
            ? Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(StatePath)}.dead-letter.ndjson")
            : Path.GetFullPath(deadLetterPath);
    }

    public string StatePath { get; }

    public string DeadLetterPath { get; }

    public async ValueTask<PipelineStateSnapshot> LoadOrCreateAsync(
        string sourceFingerprint,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFingerprint);
        PipelineStateSnapshot snapshot;
        if (!File.Exists(StatePath))
        {
            snapshot = new PipelineStateSnapshot(
                PipelineCheckpoint.Initial(sourceFingerprint),
                []);
            await SaveUnderLockAsync(snapshot, cancellationToken).ConfigureAwait(false);
            return snapshot;
        }

        snapshot = await ReadSnapshotAsync(cancellationToken).ConfigureAwait(false);
        Validate(snapshot);
        if (!string.Equals(
                snapshot.Checkpoint.SourceFingerprint,
                sourceFingerprint,
                StringComparison.Ordinal))
        {
            throw new PipelineSourceMismatchException(
                snapshot.Checkpoint.SourceFingerprint,
                sourceFingerprint);
        }

        // The JSON snapshot is authoritative. Rebuild a projection that could have been
        // interrupted after the snapshot's atomic replacement.
        await WriteDeadLetterProjectionAsync(snapshot.DeadLetters, cancellationToken).ConfigureAwait(false);
        return snapshot;
    }

    public async ValueTask SaveAsync(
        PipelineStateSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Validate(snapshot);

        await SaveUnderLockAsync(snapshot, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<PipelineStateSnapshot> ReadSnapshotAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                StatePath,
                new FileStreamOptions
                {
                    Mode = FileMode.Open,
                    Access = FileAccess.Read,
                    Share = FileShare.Read,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                });
            return await JsonSerializer.DeserializeAsync<PipelineStateSnapshot>(
                       stream,
                       SnapshotJsonOptions,
                       cancellationToken).ConfigureAwait(false)
                   ?? throw new InvalidDataException("The pipeline state JSON contains null.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The pipeline state JSON is invalid.", exception);
        }
    }

    private async ValueTask SaveUnderLockAsync(
        PipelineStateSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var ordered = snapshot with
        {
            DeadLetters = snapshot.DeadLetters
                .OrderBy(item => item.Position.StartByteOffset)
                .ToArray(),
        };
        var stateBytes = JsonSerializer.SerializeToUtf8Bytes(ordered, SnapshotJsonOptions);
        await WriteAtomicAsync(StatePath, stateBytes, cancellationToken).ConfigureAwait(false);
        await WriteDeadLetterProjectionAsync(ordered.DeadLetters, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask WriteDeadLetterProjectionAsync(
        IEnumerable<PipelineDeadLetter> deadLetters,
        CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        foreach (var item in deadLetters.OrderBy(deadLetter => deadLetter.Position.StartByteOffset))
        {
            text.Append(JsonSerializer.Serialize(item, LineJsonOptions));
            text.Append('\n');
        }

        await WriteAtomicAsync(
            DeadLetterPath,
            Encoding.UTF8.GetBytes(text.ToString()),
            cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask WriteAtomicAsync(
        string destinationPath,
        ReadOnlyMemory<byte> contents,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(destinationPath)
            ?? throw new InvalidOperationException("The destination path must have a parent directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             new FileStreamOptions
                             {
                                 Mode = FileMode.CreateNew,
                                 Access = FileAccess.Write,
                                 Share = FileShare.None,
                                 BufferSize = 4096,
                                 Options = FileOptions.Asynchronous | FileOptions.WriteThrough,
                             }))
            {
                await stream.WriteAsync(contents, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    private static void Validate(PipelineStateSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot.Checkpoint);
        ArgumentNullException.ThrowIfNull(snapshot.DeadLetters);

        if (snapshot.Checkpoint.FormatVersion != PipelineCheckpoint.CurrentFormatVersion)
        {
            throw new InvalidDataException(
                $"Unsupported pipeline state version {snapshot.Checkpoint.FormatVersion}.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var deadLetter in snapshot.DeadLetters)
        {
            if (!string.Equals(
                    deadLetter.SourceFingerprint,
                    snapshot.Checkpoint.SourceFingerprint,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException("A dead letter belongs to a different source fingerprint.");
            }

            if (!ids.Add(deadLetter.EntryId))
            {
                throw new InvalidDataException($"Duplicate dead-letter entry '{deadLetter.EntryId}'.");
            }
        }
    }

    private static void TryDeleteTemporaryFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // A failed best-effort cleanup must not hide the original persistence error.
        }
        catch (UnauthorizedAccessException)
        {
            // A failed best-effort cleanup must not hide the original persistence error.
        }
    }
}
