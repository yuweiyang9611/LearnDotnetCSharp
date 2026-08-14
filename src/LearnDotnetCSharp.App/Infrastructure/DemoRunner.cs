using System.Diagnostics;

namespace LearnDotnetCSharp.Infrastructure;

public sealed class DemoRunner(DemoContext context)
{
    public async ValueTask<DemoResult> RunAsync(IDemo demo, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(demo);
        cancellationToken.ThrowIfCancellationRequested();

        context.WriteLine($"=== {demo.Metadata.Id}: {demo.Metadata.Title} ===");
        context.WriteLine(demo.Metadata.Description);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var availability = demo.Availability;
            if (!availability.CanRun)
            {
                stopwatch.Stop();
                var reason = availability.SkipReason ?? "The current platform does not support this demo.";
                context.WriteLine($"--- skipped: {reason} ---");
                context.WriteLine();
                return new DemoResult(DemoStatus.Skipped, stopwatch.Elapsed, reason);
            }

            await demo.RunAsync(context, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            context.WriteLine($"--- passed in {stopwatch.Elapsed.TotalMilliseconds:F1} ms ---");
            context.WriteLine();
            return new DemoResult(DemoStatus.Passed, stopwatch.Elapsed);
        }
        catch (DemoSkippedException exception)
        {
            stopwatch.Stop();
            context.WriteLine($"--- skipped: {exception.Message} ---");
            context.WriteLine();
            return new DemoResult(DemoStatus.Skipped, stopwatch.Elapsed, exception.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            context.WriteLine($"--- failed in {stopwatch.Elapsed.TotalMilliseconds:F1} ms ---");
            context.WriteLine();
            return new DemoResult(DemoStatus.Failed, stopwatch.Elapsed, exception.Message, exception);
        }
    }
}
