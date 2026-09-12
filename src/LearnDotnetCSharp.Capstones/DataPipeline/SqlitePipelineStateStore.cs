using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace LearnDotnetCSharp.Capstones.DataPipeline;

/// <summary>Incremental transactional state; use one pipeline owner per database.</summary>
public sealed class SqlitePipelineStateStore : IPipelineStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string connectionString;

    public SqlitePipelineStateStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        StatePath = Path.GetFullPath(databasePath);
        DeadLetterPath = Path.ChangeExtension(StatePath, ".dead-letter.ndjson");
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = StatePath,
            Pooling = false,
        }.ToString();
    }

    public string StatePath { get; }

    public string DeadLetterPath { get; }

    public async ValueTask<PipelineStoredState> InitializeAsync(string sourceFingerprint, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFingerprint);
        Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using (var schema = connection.CreateCommand())
        {
            schema.CommandText = """
                PRAGMA journal_mode=WAL;
                CREATE TABLE IF NOT EXISTS pipeline_state (
                    id INTEGER PRIMARY KEY CHECK(id=1), format_version INTEGER NOT NULL,
                    source TEXT NOT NULL, sequence INTEGER NOT NULL, byte_offset INTEGER NOT NULL,
                    dead_letter_count INTEGER NOT NULL);
                CREATE TABLE IF NOT EXISTS pipeline_dead_letters (
                    entry_id TEXT PRIMARY KEY, byte_offset INTEGER NOT NULL, payload TEXT NOT NULL);
                """;
            await schema.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var transaction = connection.BeginTransaction();
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = "INSERT OR IGNORE INTO pipeline_state VALUES(1, $version, $source, 0, 0, 0);";
            insert.Parameters.AddWithValue("$version", PipelineCheckpoint.CurrentFormatVersion);
            insert.Parameters.AddWithValue("$source", sourceFingerprint);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var state = await ReadAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        if (state.Checkpoint.SourceFingerprint != sourceFingerprint)
        {
            throw new PipelineSourceMismatchException(state.Checkpoint.SourceFingerprint, sourceFingerprint);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return state;
    }

    public async ValueTask<PipelineCommitResult> CommitAsync(
        PipelineCheckpoint checkpoint, PipelineDeadLetter? deadLetter, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction();
        var previous = await ReadAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        PipelineStateValidation.ValidateCommit(previous.Checkpoint, checkpoint, deadLetter);
        var added = false;
        if (deadLetter is not null)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT OR IGNORE INTO pipeline_dead_letters VALUES($id, $offset, $payload);";
            insert.Parameters.AddWithValue("$id", deadLetter.EntryId);
            insert.Parameters.AddWithValue("$offset", deadLetter.Position.StartByteOffset);
            insert.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(deadLetter, JsonOptions));
            added = await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
        }

        var count = checked(previous.TotalDeadLetters + (added ? 1 : 0));
        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = "UPDATE pipeline_state SET sequence=$sequence, byte_offset=$offset, dead_letter_count=$count WHERE id=1;";
            update.Parameters.AddWithValue("$sequence", checkpoint.LastContiguousSequence);
            update.Parameters.AddWithValue("$offset", checkpoint.NextByteOffset);
            update.Parameters.AddWithValue("$count", count);
            await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new PipelineCommitResult(new PipelineStoredState(checkpoint, count), added);
    }

    public async ValueTask ExportDeadLettersAsync(string destinationPath, CancellationToken cancellationToken = default)
    {
        var destination = Path.GetFullPath(destinationPath);
        if (string.Equals(destination, StatePath, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new ArgumentException("The export must not overwrite the database.", nameof(destinationPath));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT payload FROM pipeline_dead_letters ORDER BY byte_offset, entry_id;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            await using (var writer = new StreamWriter(temporary, false, new UTF8Encoding(false)))
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    await writer.WriteAsync((reader.GetString(0) + "\n").AsMemory(), cancellationToken).ConfigureAwait(false);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA busy_timeout=5000; PRAGMA synchronous=FULL;";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<PipelineStoredState> ReadAsync(
        SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT format_version, source, sequence, byte_offset, dead_letter_count FROM pipeline_state WHERE id=1;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("InitializeAsync must complete before committing pipeline state.");
        }

        return new PipelineStoredState(new PipelineCheckpoint(reader.GetInt32(0), reader.GetString(1), reader.GetInt64(3), reader.GetInt64(2)), reader.GetInt32(4));
    }
}
