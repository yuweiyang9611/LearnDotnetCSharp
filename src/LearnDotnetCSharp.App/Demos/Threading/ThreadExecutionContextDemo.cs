using LearnDotnetCSharp.Infrastructure;

namespace LearnDotnetCSharp.Demos.Threading;

public sealed class ThreadExecutionContextDemo : IDemo
{
    private static readonly AsyncLocal<string?> AmbientCorrelation = new();

    public DemoMetadata Metadata { get; } = new(
        "threading.execution-context",
        "threading",
        "Thread、ThreadPool 与 ExecutionContext",
        "观察专用线程和线程池回调，并对比 AsyncLocal 上下文的默认流动与显式抑制。",
        [22],
        ["Thread", "ThreadPool", "ExecutionContext", "AsyncLocal"]);

    public async ValueTask RunAsync(DemoContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var previousCorrelation = AmbientCorrelation.Value;
        AmbientCorrelation.Value = "trace-local-42";

        try
        {
            var threadCompletion = new TaskCompletionSource<ThreadSnapshot>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var dedicatedThread = new Thread(() => threadCompletion.TrySetResult(new ThreadSnapshot(
                Environment.CurrentManagedThreadId,
                Thread.CurrentThread.IsThreadPoolThread,
                AmbientCorrelation.Value)))
            {
                IsBackground = true,
                Name = "execution-context-demo",
            };

            dedicatedThread.Start();
            var threadSnapshot = await threadCompletion.Task
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!dedicatedThread.Join(TimeSpan.FromSeconds(1)))
            {
                throw new InvalidOperationException("专用线程未在预期时间内结束。");
            }

            var flowedCompletion = new TaskCompletionSource<string?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var suppressedCompletion = new TaskCompletionSource<string?>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            if (!QueueAmbientRead(flowedCompletion))
            {
                throw new InvalidOperationException("无法排入普通线程池工作项。");
            }

            bool suppressedQueued;
            using (ExecutionContext.SuppressFlow())
            {
                suppressedQueued = QueueAmbientRead(suppressedCompletion);
            }

            if (!suppressedQueued)
            {
                throw new InvalidOperationException("无法排入抑制上下文流动的线程池工作项。");
            }

            var flowed = await flowedCompletion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            var suppressed = await suppressedCompletion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

            if (threadSnapshot.IsThreadPoolThread ||
                flowed != "trace-local-42" ||
                suppressed is not null)
            {
                throw new InvalidOperationException("线程身份或 ExecutionContext 流动不变量不成立。");
            }

            context.WriteProperty("Dedicated thread id", threadSnapshot.ManagedThreadId);
            context.WriteProperty("Dedicated is ThreadPool", threadSnapshot.IsThreadPoolThread);
            context.WriteProperty("Thread inherited ambient", threadSnapshot.AmbientValue ?? "<null>");
            context.WriteProperty("ThreadPool flowed value", flowed ?? "<null>");
            context.WriteProperty("Suppressed-flow value", suppressed ?? "<null>");
        }
        finally
        {
            AmbientCorrelation.Value = previousCorrelation;
        }
    }

    private static bool QueueAmbientRead(TaskCompletionSource<string?> completion) =>
        ThreadPool.QueueUserWorkItem(
            static state => state.TrySetResult(AmbientCorrelation.Value),
            completion,
            preferLocal: false);

    private sealed record ThreadSnapshot(int ManagedThreadId, bool IsThreadPoolThread, string? AmbientValue);
}
