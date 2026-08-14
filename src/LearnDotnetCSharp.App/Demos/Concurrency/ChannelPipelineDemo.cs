using System.Collections.Concurrent;
using System.Threading.Channels;
using LearnDotnetCSharp.Infrastructure;

namespace LearnDotnetCSharp.Demos.Concurrency;

public sealed class ChannelPipelineDemo : IDemo
{
    private const int ItemCount = 100;

    public DemoMetadata Metadata { get; } = new(
        "concurrency.channel-pipeline",
        "concurrency",
        "有界 Channel 并发管线",
        "两个生产者与三个消费者通过有界 Channel 施加背压，并用并发集合与 Interlocked 验证恰好一次处理。",
        [14, 22],
        ["Channel", "Backpressure", "ConcurrentDictionary", "Interlocked"]);

    public async ValueTask RunAsync(DemoContext context, CancellationToken cancellationToken)
    {
        var channel = Channel.CreateBounded<int>(new BoundedChannelOptions(8)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });

        var state = new PipelineState();
        Task[] producers =
        [
            ProduceAsync(channel.Writer, first: 1, step: 2, count: ItemCount / 2, cancellationToken),
            ProduceAsync(channel.Writer, first: 2, step: 2, count: ItemCount / 2, cancellationToken),
        ];
        Task[] consumers = Enumerable.Range(0, 3)
            .Select(_ => ConsumeAsync(channel.Reader, state, cancellationToken))
            .ToArray();

        var writerCompletion = CompleteWriterAsync(channel.Writer, producers);
        await Task.WhenAll([writerCompletion, .. consumers]).ConfigureAwait(false);

        var expectedSum = ItemCount * (ItemCount + 1L) / 2;
        var exactlyOnce = state.Count == ItemCount &&
                          state.Seen.Count == ItemCount &&
                          state.Seen.Keys.Min() == 1 &&
                          state.Seen.Keys.Max() == ItemCount;

        if (!exactlyOnce || state.Sum != expectedSum)
        {
            throw new InvalidOperationException("Channel 管线丢失、重复或错误累计了项目。");
        }

        context.WriteProperty("Bounded capacity", 8);
        context.WriteProperty("Produced / consumed", $"{ItemCount} / {state.Count}");
        context.WriteProperty("Exactly once", exactlyOnce);
        context.WriteProperty("Interlocked sum", state.Sum);
        context.WriteProperty("Expected sum", expectedSum);
    }

    private static async Task ProduceAsync(
        ChannelWriter<int> writer,
        int first,
        int step,
        int count,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index < count; index++)
        {
            var value = first + (index * step);
            await writer.WriteAsync(value, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task ConsumeAsync(
        ChannelReader<int> reader,
        PipelineState state,
        CancellationToken cancellationToken)
    {
        await foreach (var value in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            state.Record(value);
        }
    }

    private static async Task CompleteWriterAsync(ChannelWriter<int> writer, IEnumerable<Task> producers)
    {
        try
        {
            await Task.WhenAll(producers).ConfigureAwait(false);
        }
        finally
        {
            writer.TryComplete();
        }
    }

    private sealed class PipelineState
    {
        private int count;
        private long sum;

        public ConcurrentDictionary<int, byte> Seen { get; } = new();

        public int Count => Volatile.Read(ref count);

        public long Sum => Interlocked.Read(ref sum);

        public void Record(int value)
        {
            if (!Seen.TryAdd(value, 0))
            {
                throw new InvalidOperationException($"检测到重复项目 {value}。");
            }

            Interlocked.Increment(ref count);
            Interlocked.Add(ref sum, value);
        }
    }
}
