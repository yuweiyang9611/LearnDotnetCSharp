using System.Text.Json.Serialization;

namespace LearnDotnetCSharp.Capstones.Polyglot;

public sealed record PythonAnalyzeRequest(
    string Id,
    string Label,
    double[] Values,
    int DelayMilliseconds = 0,
    bool CrashOnFirstAttempt = false);

public sealed record PythonAnalyzeResponse(
    string Id,
    bool Ok,
    string? Echo,
    PythonRuntimeInfo Runtime,
    PythonStatistics? Result,
    string? Error);

public sealed record PythonRuntimeInfo(
    string Version,
    string Executable,
    string Prefix,
    string BasePrefix,
    bool InVenv,
    int ProcessId,
    int Generation);

public sealed record PythonStatistics(int Count, double Sum, double Mean, double Median);

public sealed record PythonWorkerPoolSnapshot(
    long Starts,
    long Restarts,
    IReadOnlyList<int> ActiveProcessIds,
    IReadOnlyList<int> ObservedProcessIds,
    string StandardErrorTail);

public sealed class PythonWorkerPoolOptions(
    string interpreterPath,
    string workerScriptPath,
    string workingDirectory)
{
    public string InterpreterPath { get; } = Path.GetFullPath(interpreterPath);

    public string WorkerScriptPath { get; } = Path.GetFullPath(workerScriptPath);

    public string WorkingDirectory { get; } = Path.GetFullPath(workingDirectory);

    public int WorkerCount { get; init; } = 2;

    public int QueueCapacity { get; init; } = 16;

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public int MaxCrashRetries { get; init; } = 1;

    public int StandardErrorTailCharacters { get; init; } = 8 * 1024;

    internal void Validate()
    {
        if (!File.Exists(InterpreterPath))
        {
            throw new FileNotFoundException("The workspace Python interpreter was not found.", InterpreterPath);
        }

        if (!File.Exists(WorkerScriptPath))
        {
            throw new FileNotFoundException("The Python JSON worker was not found.", WorkerScriptPath);
        }

        if (!Directory.Exists(WorkingDirectory))
        {
            throw new DirectoryNotFoundException($"The Python working directory was not found: {WorkingDirectory}");
        }

        if (WorkerCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(WorkerCount), WorkerCount, "WorkerCount must be positive.");
        }

        if (QueueCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(QueueCapacity), QueueCapacity, "QueueCapacity must be positive.");
        }

        if (RequestTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(RequestTimeout), RequestTimeout, "RequestTimeout must be positive.");
        }

        if (StartupTimeout <= TimeSpan.Zero || StartupTimeout.TotalMilliseconds > uint.MaxValue - 1)
        {
            throw new ArgumentOutOfRangeException(nameof(StartupTimeout));
        }

        if (MaxCrashRetries is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxCrashRetries),
                MaxCrashRetries,
                "This learning pool supports zero or one crash retry.");
        }

        if (StandardErrorTailCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(StandardErrorTailCharacters),
                StandardErrorTailCharacters,
                "StandardErrorTailCharacters must be positive.");
        }
    }
}

public class PythonWorkerException : Exception
{
    public PythonWorkerException(string message)
        : base(message)
    {
    }

    public PythonWorkerException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class PythonWorkerCrashedException : PythonWorkerException
{
    internal PythonWorkerCrashedException(
        string message,
        int? exitCode,
        string standardErrorTail,
        Exception? innerException = null)
        : base(message, innerException ?? new InvalidOperationException(message))
    {
        ExitCode = exitCode;
        StandardErrorTail = standardErrorTail;
    }

    public int? ExitCode { get; }

    public string StandardErrorTail { get; }
}

public sealed class PythonProtocolException : PythonWorkerException
{
    internal PythonProtocolException(string message)
        : base(message)
    {
    }

    internal PythonProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

internal sealed record PythonProtocolRequest(
    string Id,
    int ProtocolVersion,
    string Operation,
    string? Label,
    double[]? Values,
    int DelayMilliseconds,
    bool CrashBeforeResponse);

internal sealed record PythonProtocolResponse(
    string? Id,
    bool Ok,
    string? Echo,
    PythonRuntimeInfo Runtime,
    PythonStatistics? Result,
    string? Error);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PythonProtocolRequest))]
[JsonSerializable(typeof(PythonProtocolResponse))]
internal sealed partial class PythonWorkerJsonContext : JsonSerializerContext;
