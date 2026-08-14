using System.Runtime.CompilerServices;
using LearnDotnetCSharp.Infrastructure;

namespace LearnDotnetCSharp.Demos.Async;

public sealed class AsyncCancellationStreamDemo : IDemo
{
    public DemoMetadata Metadata { get; } = new(
        "async.cancellation-stream-valuetask",
        "async",
        "取消、超时、异步流与 ValueTask",
        "以协作式取消截断异步流，用 WaitAsync 建立超时边界，并展示 ValueTask 的同步缓存快路径。",
        [14],
        ["CancellationToken", "Timeout", "IAsyncEnumerable", "ValueTask"]);

    public async ValueTask RunAsync(DemoContext context, CancellationToken cancellationToken)
    {
        var observed = new List<int>();
        var cancellationObserved = false;

        using (var streamCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            try
            {
                await foreach (var value in GenerateNumbersAsync(CancellationToken.None)
                                   .WithCancellation(streamCancellation.Token)
                                   .ConfigureAwait(false))
                {
                    observed.Add(value);
                    if (observed.Count == 3)
                    {
                        streamCancellation.Cancel();
                    }
                }
            }
            catch (OperationCanceledException) when (
                streamCancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                cancellationObserved = true;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        using var slowOperationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var slowOperation = Task.Delay(TimeSpan.FromSeconds(2), slowOperationCancellation.Token);
        var timeoutObserved = false;

        try
        {
            await slowOperation
                .WaitAsync(TimeSpan.FromMilliseconds(40), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            timeoutObserved = true;
            slowOperationCancellation.Cancel();
        }

        try
        {
            await slowOperation.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            slowOperationCancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // The timed-out operation is canceled and observed so no work is left behind.
        }

        var cache = new SquareCache();
        var firstLookup = cache.GetSquareAsync(12, cancellationToken);
        var firstCompletedInline = firstLookup.IsCompletedSuccessfully;
        var firstValue = await firstLookup.ConfigureAwait(false);

        var cachedLookup = cache.GetSquareAsync(12, cancellationToken);
        var cachedCompletedInline = cachedLookup.IsCompletedSuccessfully;
        var cachedValue = await cachedLookup.ConfigureAwait(false);

        if (!cancellationObserved || !observed.SequenceEqual([1, 2, 3]))
        {
            throw new InvalidOperationException("异步流没有在第三个元素后按约定取消。");
        }

        if (!timeoutObserved)
        {
            throw new InvalidOperationException("慢操作没有触发预期的超时边界。");
        }

        if (firstCompletedInline || !cachedCompletedInline || firstValue != 144 || cachedValue != firstValue)
        {
            throw new InvalidOperationException("ValueTask 缓存快路径不满足预期不变量。");
        }

        context.WriteProperty("Async stream values", string.Join(", ", observed));
        context.WriteProperty("Cancellation observed", cancellationObserved);
        context.WriteProperty("Timeout observed", timeoutObserved);
        context.WriteProperty("First lookup inline", firstCompletedInline);
        context.WriteProperty("Cached lookup inline", cachedCompletedInline);
        context.WriteProperty("Cached value", cachedValue);
    }

    private static async IAsyncEnumerable<int> GenerateNumbersAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (var value = 1; value <= 10; value++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(TimeSpan.FromMilliseconds(5), cancellationToken).ConfigureAwait(false);
            yield return value;
        }
    }

    private sealed class SquareCache
    {
        private readonly Dictionary<int, int> values = [];

        public ValueTask<int> GetSquareAsync(int value, CancellationToken cancellationToken)
        {
            if (values.TryGetValue(value, out var cached))
            {
                return ValueTask.FromResult(cached);
            }

            return new ValueTask<int>(ComputeAndCacheAsync(value, cancellationToken));
        }

        private async Task<int> ComputeAndCacheAsync(int value, CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken).ConfigureAwait(false);
            var result = checked(value * value);
            values[value] = result;
            return result;
        }
    }
}
