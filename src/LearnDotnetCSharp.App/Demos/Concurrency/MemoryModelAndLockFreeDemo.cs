using System.Runtime.InteropServices;
using LearnDotnetCSharp.Infrastructure;

namespace LearnDotnetCSharp.Demos.Concurrency;

public sealed class MemoryModelAndLockFreeDemo : IDemo
{
    public DemoMetadata Metadata { get; } = new(
        "concurrency.memory-model-lock-free",
        "concurrency",
        ".NET 内存模型、CAS 与锁自由边界",
        "以 Volatile 发布、CompareExchange 循环、Treiber stack、版本戳 ABA 对照和显式填充展示并发可见性与进度保证。",
        [14, 22, 23],
        ["memory model", "Volatile", "CompareExchange", "lock-free", "ABA", "false sharing"]);

    public async ValueTask RunAsync(DemoContext context, CancellationToken cancellationToken)
    {
        var publication = new PublicationState();
        var producer = Task.Run(
            () =>
            {
                publication.Payload = 42;
                Volatile.Write(ref publication.Ready, 1);
            },
            cancellationToken);
        var consumer = Task.Run(
            () =>
            {
                var spin = new SpinWait();
                while (Volatile.Read(ref publication.Ready) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    spin.SpinOnce();
                }

                return publication.Payload;
            },
            cancellationToken);
        await Task.WhenAll(producer, consumer).ConfigureAwait(false);
        context.WriteProperty("Volatile safe publication", consumer.Result);

        const int workerCount = 4;
        const int incrementsPerWorker = 5_000;
        var counter = new CasCounter();
        var incrementTasks = Enumerable.Range(0, workerCount)
            .Select(_ => Task.Run(
                () =>
                {
                    for (var index = 0; index < incrementsPerWorker; index++)
                    {
                        counter.Increment();
                    }
                },
                cancellationToken))
            .ToArray();
        await Task.WhenAll(incrementTasks).ConfigureAwait(false);
        context.WriteProperty("CAS counter", counter.Value);
        context.WriteProperty("CAS retries", counter.RetryCount);

        var stack = new LockFreeStack<int>();
        const int itemsPerWorker = 250;
        var pushTasks = Enumerable.Range(0, workerCount)
            .Select(worker => Task.Run(
                () =>
                {
                    for (var index = 0; index < itemsPerWorker; index++)
                    {
                        stack.Push((worker * itemsPerWorker) + index);
                    }
                },
                cancellationToken))
            .ToArray();
        await Task.WhenAll(pushTasks).ConfigureAwait(false);

        var popped = new HashSet<int>();
        while (stack.TryPop(out var value))
        {
            popped.Add(value);
        }

        context.WriteProperty("Treiber stack unique items", popped.Count);

        var unversionedState = 1;
        var staleObservation = Volatile.Read(ref unversionedState);
        Interlocked.Exchange(ref unversionedState, 2);
        Interlocked.Exchange(ref unversionedState, 1);
        var abaWentUndetected = Interlocked.CompareExchange(ref unversionedState, 3, staleObservation) == staleObservation;

        var versionedState = Pack(version: 0, value: 1);
        var versionedObservation = Volatile.Read(ref versionedState);
        Interlocked.Exchange(ref versionedState, Pack(version: 1, value: 2));
        Interlocked.Exchange(ref versionedState, Pack(version: 2, value: 1));
        var versionStampDetectedAba = Interlocked.CompareExchange(
            ref versionedState,
            Pack(version: 1, value: 3),
            versionedObservation) != versionedObservation;
        context.WriteProperty("plain CAS missed ABA", abaWentUndetected);
        context.WriteProperty("version stamp detected ABA", versionStampDetectedAba);

        var padded = new PaddedCounterPair { Left = 20, Right = 22 };
        var rightOffset = Marshal.OffsetOf<PaddedCounterPair>(nameof(PaddedCounterPair.Right)).ToInt32();
        var paddedSize = Marshal.SizeOf<PaddedCounterPair>();
        context.WriteProperty("padded counter offsets", $"left=0, right={rightOffset}, size={paddedSize}");
        context.WriteLine("  显式填充只控制字段距离；缓存行大小、调度和真实性能仍必须在目标硬件上测量。");

        DemoAssert.Equal(42, consumer.Result, "Volatile 发布标记应让此前写入对消费者可见");
        DemoAssert.Equal(workerCount * incrementsPerWorker, counter.Value, "CAS 循环不能丢失并发增量");
        DemoAssert.Equal(workerCount * itemsPerWorker, popped.Count, "锁自由栈应保留全部唯一元素");
        DemoAssert.True(
            abaWentUndetected && versionStampDetectedAba,
            "不带版本的 CAS 无法识别 A-B-A，版本戳应使旧观察失效");
        DemoAssert.True(
            padded.Left + padded.Right == 42 && rightOffset == 64 && paddedSize == 128,
            "显式布局应把两个计数器隔开 64 字节并保留完整填充");
    }

    private static long Pack(int version, int value) => ((long)version << 32) | (uint)value;

    private sealed class PublicationState
    {
        public int Payload;
        public int Ready;
    }

    private sealed class CasCounter
    {
        private int retryCount;
        private int value;

        public int Value => Volatile.Read(ref value);

        public int RetryCount => Volatile.Read(ref retryCount);

        public void Increment()
        {
            while (true)
            {
                var observed = Volatile.Read(ref value);
                if (Interlocked.CompareExchange(ref value, observed + 1, observed) == observed)
                {
                    return;
                }

                Interlocked.Increment(ref retryCount);
            }
        }
    }

    private sealed class LockFreeStack<T>
    {
        private Node? head;

        public void Push(T value)
        {
            var node = new Node(value);
            do
            {
                node.Next = Volatile.Read(ref head);
            }
            while (Interlocked.CompareExchange(ref head, node, node.Next) != node.Next);
        }

        public bool TryPop(out T value)
        {
            while (true)
            {
                var observed = Volatile.Read(ref head);
                if (observed is null)
                {
                    value = default!;
                    return false;
                }

                if (Interlocked.CompareExchange(ref head, observed.Next, observed) == observed)
                {
                    value = observed.Value;
                    return true;
                }
            }
        }

        private sealed class Node(T value)
        {
            public Node? Next { get; set; }

            public T Value { get; } = value;
        }
    }

    [StructLayout(LayoutKind.Explicit, Size = 128)]
    private struct PaddedCounterPair
    {
        [FieldOffset(0)]
        public long Left;

        [FieldOffset(64)]
        public long Right;
    }
}
