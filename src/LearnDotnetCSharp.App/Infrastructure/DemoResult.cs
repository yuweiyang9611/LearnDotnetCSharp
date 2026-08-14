namespace LearnDotnetCSharp.Infrastructure;

public enum DemoStatus
{
    Passed,
    Skipped,
    Failed,
    Timeout,
}

public sealed record DemoResult(
    DemoStatus Status,
    TimeSpan Duration,
    string? Message = null,
    Exception? Error = null);

public sealed record DemoAvailability(bool CanRun, string? SkipReason)
{
    public static DemoAvailability Supported { get; } = new(true, null);

    public static DemoAvailability Skip(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new DemoAvailability(false, reason);
    }
}

public sealed class DemoSkippedException(string message) : Exception(message);

public static class DemoExitCodes
{
    public const int Passed = 0;
    public const int Failed = 1;
    public const int Cancelled = 2;
    public const int Usage = 64;
    public const int NotFound = 66;
    public const int Skipped = 77;
    public const int Timeout = 124;

    public static int FromStatus(DemoStatus status) => status switch
    {
        DemoStatus.Passed => Passed,
        DemoStatus.Skipped => Skipped,
        DemoStatus.Failed => Failed,
        DemoStatus.Timeout => Timeout,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown demo status."),
    };

    public static DemoStatus ToStatus(int exitCode) => exitCode switch
    {
        Passed => DemoStatus.Passed,
        Skipped => DemoStatus.Skipped,
        Timeout => DemoStatus.Timeout,
        _ => DemoStatus.Failed,
    };
}
