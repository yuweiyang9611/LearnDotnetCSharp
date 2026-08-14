using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace LearnDotnetCSharp.Capstones.LocalService;

/// <summary>
/// Describes how an idempotent operation was resolved.
/// </summary>
public enum IdempotencyOutcome
{
    /// <summary>The supplied factory ran and its payload was persisted.</summary>
    Executed,

    /// <summary>A previously persisted payload was returned without running the factory.</summary>
    Replayed,

    /// <summary>The key already belongs to a different request hash.</summary>
    Conflict,
}

/// <summary>
/// Result of resolving one idempotency key.
/// </summary>
public sealed class IdempotencyResult
{
    private readonly byte[]? payload;

    internal IdempotencyResult(IdempotencyOutcome outcome, byte[]? payload)
    {
        Outcome = outcome;
        this.payload = payload?.ToArray();
    }

    public IdempotencyOutcome Outcome { get; }

    /// <summary>
    /// Gets the exact response bytes associated with the key, or <see langword="null"/>
    /// for a conflict. Each access returns a defensive copy owned by the caller.
    /// </summary>
    public byte[]? Payload => payload?.ToArray();
}

/// <summary>
/// Persists completed idempotent operations in a SQLite database.
/// </summary>
/// <remarks>
/// The response factory runs outside a database transaction. Only its successfully produced
/// bytes are inserted in a short transaction, so cancellation and factory exceptions do not
/// leave a completed row. Calls use independent SQLite connections configured with WAL and a
/// busy timeout. A process-local single-flight gate prevents duplicate factory execution for
/// the same database, key, and request hash; the database remains the durable source of truth
/// across disposal and process restarts.
/// </remarks>
public sealed class SqliteIdempotencyStore : IAsyncDisposable
{
    private const int BusyTimeoutMilliseconds = 5_000;
    private static readonly ConcurrentDictionary<string, InFlightOperation> InFlightOperations =
        new(StringComparer.Ordinal);

    private readonly string databasePath;
    private readonly string connectionString;
    private readonly string flightIdentityPrefix;
    private readonly SemaphoreSlim initializationGate = new(1, 1);
    private int initialized;
    private int disposed;

    public SqliteIdempotencyStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        this.databasePath = Path.GetFullPath(databasePath);
        var databaseIdentity = OperatingSystem.IsWindows()
            ? this.databasePath.ToUpperInvariant()
            : this.databasePath;
        flightIdentityPrefix = $"{databaseIdentity.Length}:{databaseIdentity}";
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = this.databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString();
    }

    /// <summary>
    /// Creates the database schema and enables write-ahead logging.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (Volatile.Read(ref initialized) != 0)
        {
            return;
        }

        await initializationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (Volatile.Read(ref initialized) != 0)
            {
                return;
            }

            var directory = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using (var journalMode = connection.CreateCommand())
            {
                journalMode.CommandText = "PRAGMA journal_mode = WAL;";
                var mode = (string?)await journalMode.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                if (!string.Equals(mode, "wal", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"SQLite did not enable WAL mode; reported '{mode ?? "<null>"}'.");
                }
            }

            await using (var schema = connection.CreateCommand())
            {
                schema.CommandText =
                    """
                    CREATE TABLE IF NOT EXISTS idempotency_entries
                    (
                        idempotency_key TEXT NOT NULL PRIMARY KEY,
                        request_hash TEXT NOT NULL,
                        response_payload BLOB NOT NULL,
                        completed_utc TEXT NOT NULL
                    ) WITHOUT ROWID;
                    """;
                await schema.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            Volatile.Write(ref initialized, 1);
        }
        finally
        {
            initializationGate.Release();
        }
    }

    /// <summary>
    /// Executes <paramref name="payloadFactory"/> once for a new key, replays the stored bytes
    /// for the same hash, or reports a conflict for a different hash.
    /// </summary>
    public async Task<IdempotencyResult> ExecuteAsync(
        string idempotencyKey,
        string requestHash,
        Func<CancellationToken, ValueTask<byte[]>> payloadFactory,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestHash);
        ArgumentNullException.ThrowIfNull(payloadFactory);
        cancellationToken.ThrowIfCancellationRequested();

        var flightKey = $"{flightIdentityPrefix}{idempotencyKey.Length}:{idempotencyKey}";
        var candidate = new InFlightOperation(requestHash);
        var operation = InFlightOperations.GetOrAdd(flightKey, candidate);
        if (!string.Equals(operation.RequestHash, requestHash, StringComparison.Ordinal))
        {
            return new IdempotencyResult(IdempotencyOutcome.Conflict, payload: null);
        }

        if (!ReferenceEquals(operation, candidate))
        {
            var shared = await operation.Completion.Task
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            return shared.Outcome == IdempotencyOutcome.Conflict
                ? new IdempotencyResult(IdempotencyOutcome.Conflict, payload: null)
                : new IdempotencyResult(IdempotencyOutcome.Replayed, shared.Payload);
        }

        try
        {
            var result = await ExecuteOwnerAsync(
                    idempotencyKey,
                    requestHash,
                    payloadFactory,
                    cancellationToken)
                .ConfigureAwait(false);
            operation.Completion.TrySetResult(new FlightCompletion(result.Outcome, result.Payload));
            return result;
        }
        catch (OperationCanceledException exception)
        {
            operation.Completion.TrySetCanceled(exception.CancellationToken);
            throw;
        }
        catch (Exception exception)
        {
            operation.Completion.TrySetException(exception);
            throw;
        }
        finally
        {
            InFlightOperations.TryRemove(flightKey, out _);
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            initializationGate.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private async Task<IdempotencyResult> ExecuteOwnerAsync(
        string idempotencyKey,
        string requestHash,
        Func<CancellationToken, ValueTask<byte[]>> payloadFactory,
        CancellationToken cancellationToken)
    {
        var existing = await ReadAsync(idempotencyKey, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal)
                ? new IdempotencyResult(IdempotencyOutcome.Replayed, existing.Payload)
                : new IdempotencyResult(IdempotencyOutcome.Conflict, payload: null);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var producedPayload = await payloadFactory(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The idempotency payload factory returned null.");
        var payload = producedPayload.ToArray();
        cancellationToken.ThrowIfCancellationRequested();

        return await CommitAsync(
                idempotencyKey,
                requestHash,
                payload,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<StoredEntry?> ReadAsync(
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT request_hash, response_payload
            FROM idempotency_entries
            WHERE idempotency_key = $key;
            """;
        command.Parameters.AddWithValue("$key", idempotencyKey);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new StoredEntry(reader.GetString(0), (byte[])reader[1])
            : null;
    }

    private async Task<IdempotencyResult> CommitAsync(
        string idempotencyKey,
        string requestHash,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await using var transaction = connection.BeginTransaction(deferred: false);

        StoredEntry? existing = null;

        await using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText =
                """
                SELECT request_hash, response_payload
                FROM idempotency_entries
                WHERE idempotency_key = $key;
                """;
            read.Parameters.AddWithValue("$key", idempotencyKey);

            await using var reader = await read.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                existing = new StoredEntry(reader.GetString(0), (byte[])reader[1]);
            }
        }

        if (existing is not null)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal)
                ? new IdempotencyResult(IdempotencyOutcome.Replayed, existing.Payload)
                : new IdempotencyResult(IdempotencyOutcome.Conflict, payload: null);
        }

        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText =
                """
                INSERT INTO idempotency_entries
                    (idempotency_key, request_hash, response_payload, completed_utc)
                VALUES
                    ($key, $hash, $payload, $completedUtc);
                """;
            insert.Parameters.AddWithValue("$key", idempotencyKey);
            insert.Parameters.AddWithValue("$hash", requestHash);
            insert.Parameters.AddWithValue("$payload", payload);
            insert.Parameters.AddWithValue(
                "$completedUtc",
                DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new IdempotencyResult(IdempotencyOutcome.Executed, payload);
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA busy_timeout = {BusyTimeoutMilliseconds};";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private void EnsureInitialized()
    {
        if (Volatile.Read(ref initialized) == 0)
        {
            throw new InvalidOperationException("InitializeAsync must complete before executing idempotent operations.");
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(
        Volatile.Read(ref disposed) != 0,
        this);

    private sealed class InFlightOperation(string requestHash)
    {
        public string RequestHash { get; } = requestHash;

        public TaskCompletionSource<FlightCompletion> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed record FlightCompletion(IdempotencyOutcome Outcome, byte[]? Payload);

    private sealed record StoredEntry(string RequestHash, byte[] Payload);
}
