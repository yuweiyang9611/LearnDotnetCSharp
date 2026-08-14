using LearnDotnetCSharp.Infrastructure;

namespace LearnDotnetCSharp.Demos.Async;

public sealed class TaskWhenEachDemo : IDemo
{
    public DemoMetadata Metadata { get; } = new(
        "async.task-when-each",
        "async",
        "Task.WhenEach 完成顺序",
        "对比 Task.WhenEach 的完成顺序流与 Task.WhenAll 的输入顺序结果。",
        [14],
        ["Task.WhenEach", "Task.WhenAll", "Completion order", "TaskCompletionSource"]);

    public async ValueTask RunAsync(DemoContext context, CancellationToken cancellationToken)
    {
        var sources = Enumerable.Range(0, 3)
            .Select(_ => new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously))
            .ToArray();
        Task<int>[] tasks = [sources[0].Task, sources[1].Task, sources[2].Task];

        var releaseTask = ReleaseInKnownOrderAsync(sources, cancellationToken);
        var observeTask = ObserveCompletionOrderAsync(tasks, cancellationToken);
        await Task.WhenAll(releaseTask, observeTask).ConfigureAwait(false);

        var completionOrder = await observeTask.ConfigureAwait(false);
        var inputOrder = await Task.WhenAll(tasks).ConfigureAwait(false);

        if (!completionOrder.SequenceEqual([2, 3, 1]))
        {
            throw new InvalidOperationException("Task.WhenEach 没有保留受控的完成顺序。");
        }

        if (!inputOrder.SequenceEqual([1, 2, 3]))
        {
            throw new InvalidOperationException("Task.WhenAll 没有按输入任务顺序返回结果。");
        }

        context.WriteProperty("WhenEach order", string.Join(" -> ", completionOrder));
        context.WriteProperty("WhenAll result order", string.Join(" -> ", inputOrder));
        context.WriteProperty("Same result set", completionOrder.Order().SequenceEqual(inputOrder));
    }

    private static async Task ReleaseInKnownOrderAsync(
        TaskCompletionSource<int>[] sources,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(5), cancellationToken).ConfigureAwait(false);
            sources[1].SetResult(2);
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken).ConfigureAwait(false);
            sources[2].SetResult(3);
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken).ConfigureAwait(false);
            sources[0].SetResult(1);
        }
        catch (OperationCanceledException)
        {
            foreach (var source in sources)
            {
                source.TrySetCanceled(cancellationToken);
            }

            throw;
        }
    }

    private static async Task<IReadOnlyList<int>> ObserveCompletionOrderAsync(
        IEnumerable<Task<int>> tasks,
        CancellationToken cancellationToken)
    {
        var order = new List<int>();

        await foreach (var completedTask in Task.WhenEach(tasks)
                           .WithCancellation(cancellationToken)
                           .ConfigureAwait(false))
        {
            order.Add(await completedTask.ConfigureAwait(false));
        }

        return order;
    }
}
