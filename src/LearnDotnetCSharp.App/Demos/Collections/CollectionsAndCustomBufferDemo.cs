using System.Collections;
using System.Collections.Frozen;
using LearnDotnetCSharp.Infrastructure;

namespace LearnDotnetCSharp.Demos.Collections;

public sealed class CollectionsAndCustomBufferDemo : IDemo
{
    public DemoMetadata Metadata { get; } = new(
        "collections.core-custom-buffer",
        "collections",
        "集合选择、比较器与自定义环形缓冲区",
        "对比 List/Queue/Stack/PriorityQueue/Set/Dictionary/FrozenDictionary，并实现 IReadOnlyList<T> 环形缓冲区理解集合契约。",
        [7],
        ["IEnumerable<T>", "IReadOnlyList<T>", "PriorityQueue", "HashSet", "FrozenDictionary", "IComparer<T>"]);

    public ValueTask RunAsync(DemoContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        List<string> work = ["parse", "compile", "test"];
        var fifo = new Queue<string>(work);
        var lifo = new Stack<string>(work);
        var priority = new PriorityQueue<string, int>();
        priority.Enqueue("documentation", 3);
        priority.Enqueue("correctness", 1);
        priority.Enqueue("performance", 2);

        var fifoFirst = fifo.Dequeue();
        var lifoFirst = lifo.Pop();
        var priorityOrder = Drain(priority).ToArray();

        context.WriteLine("[顺序语义决定集合]");
        context.WriteProperty("Queue first", fifoFirst);
        context.WriteProperty("Stack first", lifoFirst);
        context.WriteProperty("PriorityQueue order", string.Join(" -> ", priorityOrder));

        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "LINQ",
            "linq",
            "HTTP3",
        };
        var chapterNames = new Dictionary<int, string>
        {
            [7] = "Collections",
            [8] = "LINQ queries",
            [9] = "LINQ operators",
        }.ToFrozenDictionary();
        var sorted = new SortedSet<string>(work, StringComparer.OrdinalIgnoreCase);

        context.WriteLine("\n[唯一性、查找与只读优化]");
        context.WriteProperty("case-insensitive set count", tags.Count);
        context.WriteProperty("FrozenDictionary[8]", chapterNames[8]);
        context.WriteProperty("SortedSet", string.Join(", ", sorted));

        var buffer = new RingBuffer<int>(capacity: 3);
        foreach (var value in Enumerable.Range(1, 5))
        {
            buffer.Add(value);
        }

        context.WriteLine("\n[自定义 IReadOnlyList<T>]");
        context.WriteProperty("logical order", string.Join(", ", buffer));
        context.WriteProperty("indexer [0] / [2]", $"{buffer[0]} / {buffer[2]}");

        DemoAssert.True(fifoFirst == "parse" && lifoFirst == "test", "Queue 与 Stack 应体现 FIFO/LIFO");
        DemoAssert.SequenceEqual(["correctness", "performance", "documentation"], priorityOrder, "优先队列应按最小优先级出队");
        DemoAssert.True(tags.Count == 2 && chapterNames[9] == "LINQ operators", "比较器和冻结字典应保持预期语义");
        DemoAssert.SequenceEqual([3, 4, 5], buffer, "满环形缓冲区应覆盖最旧元素并保持逻辑顺序");

        return ValueTask.CompletedTask;
    }

    private static IEnumerable<TElement> Drain<TElement, TPriority>(PriorityQueue<TElement, TPriority> queue)
    {
        while (queue.TryDequeue(out var element, out _))
        {
            yield return element;
        }
    }
}

public sealed class RingBuffer<T> : IReadOnlyList<T>
{
    private readonly T[] items;
    private int start;

    public RingBuffer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        items = new T[capacity];
    }

    public int Count { get; private set; }

    public T this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
            return items[(start + index) % items.Length];
        }
    }

    public void Add(T item)
    {
        if (Count < items.Length)
        {
            items[(start + Count) % items.Length] = item;
            Count++;
            return;
        }

        items[start] = item;
        start = (start + 1) % items.Length;
    }

    public IEnumerator<T> GetEnumerator()
    {
        for (var index = 0; index < Count; index++)
        {
            yield return this[index];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
