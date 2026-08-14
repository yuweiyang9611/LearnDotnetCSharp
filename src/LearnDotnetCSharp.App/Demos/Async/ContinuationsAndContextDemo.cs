using System.Collections.Concurrent;
using LearnDotnetCSharp.Infrastructure;

namespace LearnDotnetCSharp.Demos.Async;

public sealed class ContinuationsAndContextDemo : IDemo
{
    public DemoMetadata Metadata { get; } = new(
        "async.continuations-context",
        "async",
        "Continuation、SynchronizationContext 与 ExecutionContext",
        "以专用单线程上下文演示 await 捕获、ConfigureAwait(false)、AsyncLocal 流动、SuppressFlow 和同步阻塞症状。",
        [14, 22],
        ["continuation", "SynchronizationContext", "ConfigureAwait", "ExecutionContext", "AsyncLocal", "sync-over-async"]);

    public async ValueTask RunAsync(DemoContext context, CancellationToken cancellationToken)
    {
        using var singleThreadContext = new SingleThreadSynchronizationContext();
        var ambient = new AsyncLocal<string?>();

        var captureBefore = 0;
        var captureAfter = 0;
        var capturedContext = false;
        await singleThreadContext.RunAsync(async () =>
        {
            captureBefore = Environment.CurrentManagedThreadId;
            await Task.Yield();
            captureAfter = Environment.CurrentManagedThreadId;
            capturedContext = ReferenceEquals(SynchronizationContext.Current, singleThreadContext);
        }).WaitAsync(cancellationToken).ConfigureAwait(false);

        var configureAwaitThread = 0;
        var configureAwaitContextIsNull = false;
        string? flowedAmbient = null;
        await singleThreadContext.RunAsync(async () =>
        {
            ambient.Value = "correlation-42";
            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            configureAwaitThread = Environment.CurrentManagedThreadId;
            configureAwaitContextIsNull = SynchronizationContext.Current is null;
            flowedAmbient = ambient.Value;
        }).WaitAsync(cancellationToken).ConfigureAwait(false);

        ambient.Value = "must-not-flow";
        Task<string?> suppressedWorker;
        using (ExecutionContext.SuppressFlow())
        {
            suppressedWorker = Task.Run(() => (string?)ambient.Value, cancellationToken);
        }

        var suppressedAmbient = await suppressedWorker.ConfigureAwait(false);
        ambient.Value = null;

        var blockingTimedOut = false;
        var eventualResult = 0;
        await singleThreadContext.RunAsync(async () =>
        {
            var capturedDelay = CapturedDelayAsync(cancellationToken);
            blockingTimedOut = !capturedDelay.Wait(TimeSpan.FromMilliseconds(75));
            eventualResult = await capturedDelay;
        }).WaitAsync(cancellationToken).ConfigureAwait(false);

        string? exceptionMethod = null;
        try
        {
            await ThrowAfterYieldAsync();
        }
        catch (InvalidOperationException exception)
        {
            exceptionMethod = exception.StackTrace?
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(line => line.Contains(nameof(ThrowAfterYieldAsync), StringComparison.Ordinal))?
                .Trim();
        }

        context.WriteProperty("captured thread", $"{captureBefore} -> {captureAfter}");
        context.WriteProperty("captured context", capturedContext);
        context.WriteProperty("ConfigureAwait thread", configureAwaitThread);
        context.WriteProperty("ConfigureAwait context null", configureAwaitContextIsNull);
        context.WriteProperty("AsyncLocal still flowed", flowedAmbient);
        context.WriteProperty("SuppressFlow result", suppressedAmbient ?? "<null>");
        context.WriteProperty("sync-over-async timed out", blockingTimedOut);
        context.WriteProperty("eventual async result", eventualResult);
        context.WriteProperty("await exception stack", exceptionMethod ?? "<missing>");
        context.WriteLine("  75 ms 超时只用于构造可控症状；生产代码不应依靠超时掩盖同步等待。");

        DemoAssert.True(captureBefore == captureAfter && capturedContext, "Task.Yield 后应回到专用 SynchronizationContext");
        DemoAssert.True(configureAwaitContextIsNull, "ConfigureAwait(false) 后不应恢复专用 SynchronizationContext");
        DemoAssert.Equal("correlation-42", flowedAmbient, "ConfigureAwait(false) 不应抑制 ExecutionContext/AsyncLocal");
        DemoAssert.Equal<string?>(null, suppressedAmbient, "SuppressFlow 应阻止 AsyncLocal 流入新任务");
        DemoAssert.True(blockingTimedOut && eventualResult == 42, "同步等待应先超时，解除阻塞后异步操作应完成");
        DemoAssert.True(exceptionMethod is not null, "await 传播的异常应保留异步方法栈帧");
    }

    private static async Task<int> CapturedDelayAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(10, cancellationToken);
        return 42;
    }

    private static async Task ThrowAfterYieldAsync()
    {
        await Task.Yield();
        throw new InvalidOperationException("expected async failure");
    }

    private sealed class SingleThreadSynchronizationContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = [];
        private readonly Thread _thread;
        private bool _disposed;

        public SingleThreadSynchronizationContext()
        {
            _thread = new Thread(Pump)
            {
                IsBackground = true,
                Name = "LearnDotnetCSharp.SyncContext",
            };
            _thread.Start();
        }

        public override void Post(SendOrPostCallback d, object? state)
        {
            ArgumentNullException.ThrowIfNull(d);
            _queue.Add((d, state));
        }

        public Task RunAsync(Func<Task> work)
        {
            ArgumentNullException.ThrowIfNull(work);
            ObjectDisposedException.ThrowIf(_disposed, this);

            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Post(async _ =>
            {
                try
                {
                    await work();
                    completion.SetResult();
                }
                catch (Exception exception)
                {
                    completion.SetException(exception);
                }
            }, null);
            return completion.Task;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _queue.CompleteAdding();
            _thread.Join();
            _queue.Dispose();
        }

        private void Pump()
        {
            SetSynchronizationContext(this);
            foreach (var (callback, state) in _queue.GetConsumingEnumerable())
            {
                callback(state);
            }
        }
    }
}
