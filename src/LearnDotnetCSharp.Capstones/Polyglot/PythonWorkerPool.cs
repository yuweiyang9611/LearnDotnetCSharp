using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace LearnDotnetCSharp.Capstones.Polyglot;

public sealed class PythonWorkerPool : IAsyncDisposable
{
    private const int ProtocolVersion = 1;

    private readonly PythonWorkerPoolOptions options;
    private readonly Channel<PythonWorkItem> workItems;
    private readonly CancellationTokenSource lifetime = new();
    private readonly ConcurrentDictionary<string, byte> inFlightIds = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<int, byte> observedProcessIds = [];
    private readonly BoundedTextTail standardErrorTail;
    private readonly int[] activeProcessIds;
    private readonly int[] generations;
    private readonly TaskCompletionSource[] readiness;
    private readonly Task[] workerTasks;
    private long starts;
    private long restarts;
    private int disposeState;

    private PythonWorkerPool(PythonWorkerPoolOptions options)
    {
        this.options = options;
        standardErrorTail = new BoundedTextTail(options.StandardErrorTailCharacters);
        activeProcessIds = new int[options.WorkerCount];
        generations = new int[options.WorkerCount];
        readiness = Enumerable.Range(0, options.WorkerCount)
            .Select(static _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
            .ToArray();
        workItems = Channel.CreateBounded<PythonWorkItem>(new BoundedChannelOptions(options.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
        workerTasks = Enumerable.Range(0, options.WorkerCount)
            .Select(index => RunWorkerAsync(index, readiness[index], lifetime.Token))
            .ToArray();
    }

    public PythonWorkerPoolSnapshot Snapshot
    {
        get
        {
            var active = Enumerable.Range(0, activeProcessIds.Length)
                .Select(index => Volatile.Read(ref activeProcessIds[index]))
                .Where(static processId => processId > 0)
                .Order()
                .ToArray();
            var observed = observedProcessIds.Keys.Order().ToArray();
            return new PythonWorkerPoolSnapshot(
                Interlocked.Read(ref starts),
                Interlocked.Read(ref restarts),
                active,
                observed,
                standardErrorTail.ToString());
        }
    }

    public static async Task<PythonWorkerPool> StartAsync(
        PythonWorkerPoolOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        var pool = new PythonWorkerPool(options);
        try
        {
            await Task.WhenAll(pool.readiness.Select(static source => source.Task))
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            return pool;
        }
        catch
        {
            await pool.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task<PythonAnalyzeResponse> AnalyzeAsync(
        PythonAnalyzeRequest request,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateRequest(request);
        if (!inFlightIds.TryAdd(request.Id, 0))
        {
            throw new InvalidOperationException($"A Python request with ID '{request.Id}' is already in flight.");
        }

        var item = PythonWorkItem.ForAnalyze(request, cancellationToken);
        try
        {
            await workItems.Writer.WriteAsync(item, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            inFlightIds.TryRemove(request.Id, out _);
            throw;
        }

        return await item.Response.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task CrashWorkerAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var item = PythonWorkItem.ForCrash(cancellationToken);
        await workItems.Writer.WriteAsync(item, cancellationToken).ConfigureAwait(false);
        await item.Response.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposeState, 1) != 0)
        {
            return;
        }

        workItems.Writer.TryComplete();
        await lifetime.CancelAsync().ConfigureAwait(false);

        try
        {
            await Task.WhenAll(workerTasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }

        var disposedException = new ObjectDisposedException(nameof(PythonWorkerPool));
        while (workItems.Reader.TryRead(out var item))
        {
            CompleteWithException(item, disposedException);
        }

        lifetime.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task RunWorkerAsync(
        int workerIndex,
        TaskCompletionSource ready,
        CancellationToken cancellationToken)
    {
        PythonWorkerSession? session = null;
        try
        {
            session = await StartSessionAsync(workerIndex, cancellationToken).ConfigureAwait(false);
            ready.TrySetResult();

            await foreach (var item in workItems.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                if (item.CancellationToken.IsCancellationRequested)
                {
                    CompleteCanceled(item);
                    continue;
                }

                session = await ProcessItemAsync(workerIndex, session, item, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ready.TrySetCanceled(cancellationToken);
        }
        catch (Exception exception)
        {
            ready.TrySetException(exception);
            standardErrorTail.Append($"[pool worker {workerIndex}] {exception}\n");
        }
        finally
        {
            Volatile.Write(ref activeProcessIds[workerIndex], 0);
            if (session is not null)
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task<PythonWorkerSession> ProcessItemAsync(
        int workerIndex,
        PythonWorkerSession session,
        PythonWorkItem item,
        CancellationToken poolCancellationToken)
    {
        var crashAttempt = 0;
        while (true)
        {
            using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(
                poolCancellationToken,
                item.CancellationToken);
            requestTimeout.CancelAfter(options.RequestTimeout);

            try
            {
                if (item.Kind == PythonWorkItemKind.Crash)
                {
                    await session.CrashAsync(requestTimeout.Token).ConfigureAwait(false);
                    throw new PythonProtocolException("The crash control request unexpectedly returned a response.");
                }

                var request = item.Request
                    ?? throw new InvalidOperationException("An analyze work item must contain a request.");
                var response = await session.AnalyzeAsync(
                    request,
                    crashBeforeResponse: request.CrashOnFirstAttempt && crashAttempt == 0,
                    requestTimeout.Token).ConfigureAwait(false);
                CompleteWithResponse(item, response);
                return session;
            }
            catch (PythonWorkerCrashedException exception)
            {
                session = await RestartSessionAsync(workerIndex, session, poolCancellationToken)
                    .ConfigureAwait(false);

                if (item.Kind == PythonWorkItemKind.Crash)
                {
                    CompleteWithResponse(item, CreateCrashControlResponse(session));
                    return session;
                }

                if (crashAttempt < options.MaxCrashRetries)
                {
                    crashAttempt++;
                    continue;
                }

                CompleteWithException(item, exception);
                return session;
            }
            catch (OperationCanceledException) when (item.CancellationToken.IsCancellationRequested)
            {
                session = await RestartSessionAsync(workerIndex, session, poolCancellationToken)
                    .ConfigureAwait(false);
                CompleteCanceled(item);
                return session;
            }
            catch (OperationCanceledException exception) when (!poolCancellationToken.IsCancellationRequested)
            {
                session = await RestartSessionAsync(workerIndex, session, poolCancellationToken)
                    .ConfigureAwait(false);
                CompleteWithException(
                    item,
                    new TimeoutException($"Python request '{item.Request?.Id ?? "<control>"}' timed out.", exception));
                return session;
            }
            catch (OperationCanceledException) when (poolCancellationToken.IsCancellationRequested)
            {
                CompleteWithException(item, new ObjectDisposedException(nameof(PythonWorkerPool)));
                throw;
            }
            catch (Exception exception) when (exception is PythonProtocolException or IOException or InvalidOperationException)
            {
                session = await RestartSessionAsync(workerIndex, session, poolCancellationToken)
                    .ConfigureAwait(false);
                CompleteWithException(item, exception);
                return session;
            }
        }
    }

    private async Task<PythonWorkerSession> StartSessionAsync(
        int workerIndex,
        CancellationToken cancellationToken)
    {
        var generation = Interlocked.Increment(ref generations[workerIndex]);
        var session = await PythonWorkerSession.StartAsync(
            options,
            workerIndex,
            generation,
            standardErrorTail,
            cancellationToken).ConfigureAwait(false);

        Volatile.Write(ref activeProcessIds[workerIndex], session.ProcessId);
        observedProcessIds.TryAdd(session.ProcessId, 0);
        Interlocked.Increment(ref starts);
        return session;
    }

    private async Task<PythonWorkerSession> RestartSessionAsync(
        int workerIndex,
        PythonWorkerSession session,
        CancellationToken cancellationToken)
    {
        Volatile.Write(ref activeProcessIds[workerIndex], 0);
        await session.DisposeAsync().ConfigureAwait(false);
        Interlocked.Increment(ref restarts);
        return await StartSessionAsync(workerIndex, cancellationToken).ConfigureAwait(false);
    }

    private static PythonAnalyzeResponse CreateCrashControlResponse(PythonWorkerSession session) =>
        new(
            $"crash-control-{session.WorkerIndex}-{session.Generation}",
            true,
            null,
            session.Runtime,
            null,
            null);

    private void CompleteWithResponse(PythonWorkItem item, PythonAnalyzeResponse response)
    {
        if (item.Request is not null)
        {
            inFlightIds.TryRemove(item.Request.Id, out _);
        }

        item.Response.TrySetResult(response);
    }

    private void CompleteWithException(PythonWorkItem item, Exception exception)
    {
        if (item.Request is not null)
        {
            inFlightIds.TryRemove(item.Request.Id, out _);
        }

        item.Response.TrySetException(exception);
    }

    private void CompleteCanceled(PythonWorkItem item)
    {
        if (item.Request is not null)
        {
            inFlightIds.TryRemove(item.Request.Id, out _);
        }

        item.Response.TrySetCanceled(item.CancellationToken);
    }

    private static void ValidateRequest(PythonAnalyzeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Id))
        {
            throw new ArgumentException("The Python request ID must not be empty.", nameof(request));
        }

        if (request.Values is null || request.Values.Length == 0)
        {
            throw new ArgumentException("The Python request must contain at least one value.", nameof(request));
        }

        if (request.DelayMilliseconds is < 0 or > 5_000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.DelayMilliseconds,
                "DelayMilliseconds must be between zero and 5000.");
        }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposeState) != 0, this);

    private enum PythonWorkItemKind
    {
        Analyze,
        Crash,
    }

    private sealed class PythonWorkItem
    {
        private PythonWorkItem(
            PythonWorkItemKind kind,
            PythonAnalyzeRequest? request,
            CancellationToken cancellationToken)
        {
            Kind = kind;
            Request = request;
            CancellationToken = cancellationToken;
        }

        public PythonWorkItemKind Kind { get; }

        public PythonAnalyzeRequest? Request { get; }

        public CancellationToken CancellationToken { get; }

        public TaskCompletionSource<PythonAnalyzeResponse> Response { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static PythonWorkItem ForAnalyze(
            PythonAnalyzeRequest request,
            CancellationToken cancellationToken) =>
            new(PythonWorkItemKind.Analyze, request, cancellationToken);

        public static PythonWorkItem ForCrash(CancellationToken cancellationToken) =>
            new(PythonWorkItemKind.Crash, null, cancellationToken);
    }

    private sealed class BoundedTextTail(int maximumCharacters)
    {
        private readonly Lock gate = new();
        private readonly StringBuilder buffer = new(maximumCharacters);

        public void Append(string value)
        {
            lock (gate)
            {
                buffer.Append(value);
                if (buffer.Length > maximumCharacters)
                {
                    buffer.Remove(0, buffer.Length - maximumCharacters);
                }
            }
        }

        public override string ToString()
        {
            lock (gate)
            {
                return buffer.ToString();
            }
        }
    }

    private sealed class PythonWorkerSession : IAsyncDisposable
    {
        private readonly Process process;
        private readonly BoundedTextTail standardErrorTail;
        private readonly CancellationTokenSource stderrLifetime = new();
        private readonly Task stderrPump;
        private int disposeState;

        private PythonWorkerSession(
            Process process,
            int workerIndex,
            int generation,
            BoundedTextTail standardErrorTail)
        {
            this.process = process;
            this.standardErrorTail = standardErrorTail;
            WorkerIndex = workerIndex;
            Generation = generation;
            stderrPump = PumpStandardErrorAsync(stderrLifetime.Token);
        }

        public int WorkerIndex { get; }

        public int Generation { get; }

        public int ProcessId => Runtime.ProcessId > 0 ? Runtime.ProcessId : process.Id;

        public PythonRuntimeInfo Runtime { get; private set; } =
            new("unknown", "unknown", "unknown", "unknown", false, 0, 0);

        public static async Task<PythonWorkerSession> StartAsync(
            PythonWorkerPoolOptions options,
            int workerIndex,
            int generation,
            BoundedTextTail standardErrorTail,
            CancellationToken cancellationToken)
        {
            var startInfo = CreateStartInfo(options, generation);
            var process = new Process { StartInfo = startInfo };
            try
            {
                if (!process.Start())
                {
                    throw new InvalidOperationException("The Python worker process did not start.");
                }

                var session = new PythonWorkerSession(process, workerIndex, generation, standardErrorTail);
                try
                {
                    var handshakeId = $"handshake-{workerIndex}-{generation}";
                    var handshake = new PythonProtocolRequest(
                        handshakeId,
                        ProtocolVersion,
                        "handshake",
                        null,
                        null,
                        0,
                        false);
                    var response = await session.ExchangeAsync(handshake, cancellationToken).ConfigureAwait(false);
                    if (!response.Ok)
                    {
                        throw new PythonProtocolException(
                            $"Python worker handshake failed: {response.Error ?? "unknown error"}");
                    }

                    if (response.Runtime.ProcessId <= 0 || response.Runtime.Generation != generation)
                    {
                        throw new PythonProtocolException("Python worker handshake returned inconsistent runtime identity.");
                    }

                    session.Runtime = response.Runtime;
                    return session;
                }
                catch
                {
                    await session.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }
            catch
            {
                process.Dispose();
                throw;
            }
        }

        public async Task<PythonAnalyzeResponse> AnalyzeAsync(
            PythonAnalyzeRequest request,
            bool crashBeforeResponse,
            CancellationToken cancellationToken)
        {
            var protocolRequest = new PythonProtocolRequest(
                request.Id,
                ProtocolVersion,
                "analyze",
                request.Label,
                request.Values,
                request.DelayMilliseconds,
                crashBeforeResponse);
            var response = await ExchangeAsync(protocolRequest, cancellationToken).ConfigureAwait(false);
            return new PythonAnalyzeResponse(
                response.Id!,
                response.Ok,
                response.Echo,
                response.Runtime,
                response.Result,
                response.Error);
        }

        public async Task CrashAsync(CancellationToken cancellationToken)
        {
            var request = new PythonProtocolRequest(
                $"crash-{WorkerIndex}-{Generation}",
                ProtocolVersion,
                "crash",
                null,
                null,
                0,
                false);
            _ = await ExchangeAsync(request, cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposeState, 1) != 0)
            {
                return;
            }

            TryCloseStandardInput();
            if (!process.HasExited)
            {
                using var gracefulExit = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
                try
                {
                    await process.WaitForExitAsync(gracefulExit.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    TryKill(process);
                }
            }

            if (!process.HasExited)
            {
                await process.WaitForExitAsync().ConfigureAwait(false);
            }

            await stderrLifetime.CancelAsync().ConfigureAwait(false);
            try
            {
                await stderrPump.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stderrLifetime.IsCancellationRequested)
            {
            }

            stderrLifetime.Dispose();
            process.Dispose();
        }

        private async Task<PythonProtocolResponse> ExchangeAsync(
            PythonProtocolRequest request,
            CancellationToken cancellationToken)
        {
            try
            {
                var requestJson = JsonSerializer.Serialize(
                    request,
                    PythonWorkerJsonContext.Default.PythonProtocolRequest);
                await process.StandardInput.WriteLineAsync(requestJson.AsMemory(), cancellationToken)
                    .ConfigureAwait(false);
                await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);

                var responseJson = await process.StandardOutput.ReadLineAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (responseJson is null)
                {
                    throw CreateCrashedException(request.Id);
                }

                PythonProtocolResponse response;
                try
                {
                    response = JsonSerializer.Deserialize(
                                   responseJson,
                                   PythonWorkerJsonContext.Default.PythonProtocolResponse)
                               ?? throw new JsonException("The Python worker returned JSON null.");
                }
                catch (JsonException exception)
                {
                    throw new PythonProtocolException("The Python worker returned malformed JSON.", exception);
                }

                if (!string.Equals(request.Id, response.Id, StringComparison.Ordinal))
                {
                    throw new PythonProtocolException(
                        $"Python response ID '{response.Id ?? "<null>"}' did not match request ID '{request.Id}'.");
                }

                return response;
            }
            catch (Exception exception) when (
                exception is IOException or InvalidOperationException or ObjectDisposedException)
            {
                throw CreateCrashedException(request.Id, exception);
            }
        }

        private PythonWorkerCrashedException CreateCrashedException(
            string requestId,
            Exception? innerException = null)
        {
            int? exitCode = null;
            try
            {
                if (process.HasExited)
                {
                    exitCode = process.ExitCode;
                }
            }
            catch (InvalidOperationException)
            {
            }

            return new PythonWorkerCrashedException(
                $"Python worker {WorkerIndex}/{Generation} exited while processing '{requestId}'.",
                exitCode,
                standardErrorTail.ToString(),
                innerException);
        }

        private async Task PumpStandardErrorAsync(CancellationToken cancellationToken)
        {
            var buffer = new char[512];
            while (true)
            {
                var read = await process.StandardError.ReadAsync(buffer.AsMemory(), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    return;
                }

                standardErrorTail.Append($"[python {WorkerIndex}/{Generation}] ");
                standardErrorTail.Append(new string(buffer, 0, read));
            }
        }

        private void TryCloseStandardInput()
        {
            try
            {
                process.StandardInput.Close();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private static ProcessStartInfo CreateStartInfo(PythonWorkerPoolOptions options, int generation)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = options.InterpreterPath,
                WorkingDirectory = options.WorkingDirectory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("-I");
            startInfo.ArgumentList.Add("-u");
            startInfo.ArgumentList.Add(options.WorkerScriptPath);
            startInfo.ArgumentList.Add("--generation");
            startInfo.ArgumentList.Add(generation.ToString(System.Globalization.CultureInfo.InvariantCulture));
            startInfo.Environment["PYTHONDONTWRITEBYTECODE"] = "1";
            return startInfo;
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception exception) when (
                exception is InvalidOperationException or Win32Exception or NotSupportedException)
            {
            }
        }
    }
}
