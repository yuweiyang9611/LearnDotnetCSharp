using LearnDotnetCSharp.Infrastructure;

namespace LearnDotnetCSharp.Demos.Threading;

public sealed class SynchronizationPrimitivesDemo : IDemo
{
    public DemoMetadata Metadata { get; } = new(
        "threading.synchronization-primitives",
        "threading",
        "同步原语与受控并发",
        "用 SemaphoreSlim 限制异步临界区，并用 ManualResetEventSlim 与 CountdownEvent 协调专用线程。",
        [22],
        ["SemaphoreSlim", "ManualResetEventSlim", "CountdownEvent", "Interlocked"]);

    public async ValueTask RunAsync(DemoContext context, CancellationToken cancellationToken)
    {
        using var semaphore = new SemaphoreSlim(initialCount: 2, maxCount: 2);
        var active = 0;
        var maximum = 0;

        var jobs = Enumerable.Range(0, 6)
            .Select(_ => EnterLimitedRegionAsync(
                semaphore,
                () => Interlocked.Increment(ref active),
                value => UpdateMaximum(ref maximum, value),
                () => Interlocked.Decrement(ref active),
                cancellationToken))
            .ToArray();
        await Task.WhenAll(jobs).ConfigureAwait(false);

        var releasedWorkers = RunSignaledThreads(cancellationToken);
        if (maximum != 2 || Volatile.Read(ref active) != 0 || releasedWorkers != 3)
        {
            throw new InvalidOperationException("同步原语没有维持并发上限或线程协调不变量。");
        }

        context.WriteProperty("Semaphore capacity", 2);
        context.WriteProperty("Observed max concurrency", maximum);
        context.WriteProperty("Active after completion", active);
        context.WriteProperty("Threads released by event", releasedWorkers);
        context.WriteProperty("Countdown reached zero", true);
    }

    private static async Task EnterLimitedRegionAsync(
        SemaphoreSlim semaphore,
        Func<int> enter,
        Action<int> observeMaximum,
        Func<int> leave,
        CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        var current = enter();
        observeMaximum(current);

        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            leave();
            semaphore.Release();
        }
    }

    private static int RunSignaledThreads(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var start = new ManualResetEventSlim(initialState: false);
        using var finished = new CountdownEvent(initialCount: 3);
        var released = 0;
        var threads = new Thread[3];

        for (var index = 0; index < threads.Length; index++)
        {
            threads[index] = new Thread(() =>
            {
                start.Wait();
                Interlocked.Increment(ref released);
                finished.Signal();
            })
            {
                IsBackground = true,
                Name = $"sync-worker-{index}",
            };
            threads[index].Start();
        }

        start.Set();
        try
        {
            if (!finished.Wait(TimeSpan.FromSeconds(2), cancellationToken))
            {
                throw new TimeoutException("专用线程没有及时通过同步屏障。");
            }
        }
        finally
        {
            // Join before disposing the events, including on external cancellation.
            foreach (var thread in threads)
            {
                thread.Join(TimeSpan.FromSeconds(1));
            }
        }

        var unfinishedThread = threads.FirstOrDefault(thread => thread.IsAlive);
        if (unfinishedThread is not null)
        {
            throw new TimeoutException($"线程 {unfinishedThread.Name} 没有及时结束。");
        }

        return Volatile.Read(ref released);
    }

    private static void UpdateMaximum(ref int target, int candidate)
    {
        var current = Volatile.Read(ref target);
        while (candidate > current)
        {
            var observed = Interlocked.CompareExchange(ref target, candidate, current);
            if (observed == current)
            {
                return;
            }

            current = observed;
        }
    }
}
