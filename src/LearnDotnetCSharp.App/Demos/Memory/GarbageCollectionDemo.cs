using System.Buffers;
using System.Runtime.CompilerServices;
using LearnDotnetCSharp.Infrastructure;

namespace LearnDotnetCSharp.Demos.Memory;

public sealed class GarbageCollectionDemo : IDemo
{
    private const int BufferSize = 4 * 1024;
    private const int IterationCount = 256;

    public DemoMetadata Metadata { get; } = new(
        "memory.gc",
        "memory",
        "GC、分代、弱引用与池化",
        "观察托管分配和 GC 堆信息，并用受控实验解释对象晋升、弱引用及 ArrayPool<T>。",
        [12, 24],
        ["GC generations", "GCMemoryInfo", "allocation counters", "WeakReference<T>", "ArrayPool<T>"]);

    public ValueTask RunAsync(DemoContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        context.WriteLine("[分代与堆快照]");
        var survivor = new object();
        var collectionCountBefore = GC.CollectionCount(0);
        var initialGeneration = GC.GetGeneration(survivor);

        // This forced collection is deliberately confined to a repeatable experiment.
        GC.Collect(0, GCCollectionMode.Forced, blocking: true, compacting: false);

        // Capture immediately: these values are process-wide and describe the most recent GC.
        var generationAfterCollection = GC.GetGeneration(survivor);
        var gen0CollectionDelta = GC.CollectionCount(0) - collectionCountBefore;
        var memoryInfo = GC.GetGCMemoryInfo();

        context.WriteProperty("GC.MaxGeneration", GC.MaxGeneration);
        context.WriteProperty("新对象所在代", initialGeneration);
        context.WriteProperty("存活一次后所在代", generationAfterCollection);
        context.WriteProperty("进程 Gen0 计数差", gen0CollectionDelta);

        context.WriteProperty("最近一次 GC HeapSizeBytes", memoryInfo.HeapSizeBytes);
        context.WriteProperty("最近一次 GC FragmentedBytes", memoryInfo.FragmentedBytes);
        context.WriteProperty("MemoryLoadBytes", memoryInfo.MemoryLoadBytes);
        context.WriteProperty("HighMemoryLoadThreshold", memoryInfo.HighMemoryLoadThresholdBytes);
        context.WriteProperty("TotalAvailableMemory", memoryInfo.TotalAvailableMemoryBytes);
        GC.KeepAlive(survivor);

        context.WriteLine("\n[当前线程分配计数与池化]");
        var newArrayMeasurement = MeasureNewArrays();
        var pooledMeasurement = MeasurePooledArrays();
        context.WriteProperty("new byte[] 分配字节", newArrayMeasurement.AllocatedBytes);
        context.WriteProperty("ArrayPool 分配字节", pooledMeasurement.AllocatedBytes);
        context.WriteProperty("池返回的最小容量", pooledMeasurement.MinimumRentedLength);
        context.WriteProperty("校验和", newArrayMeasurement.Checksum + pooledMeasurement.Checksum);
        context.WriteProperty("进程累计托管分配", GC.GetTotalAllocatedBytes(precise: false));

        context.WriteLine("\n[弱引用生命周期]");
        var weakReference = CreateWeakReference();
        var accessibleBeforeCollection = CanAccessTarget(weakReference);
        context.WriteProperty("收集前可访问", accessibleBeforeCollection);
        ForceFullCollectionForWeakReferenceExperiment();
        var accessibleAfterCollection = CanAccessTarget(weakReference);
        context.WriteProperty("收集后可访问", accessibleAfterCollection);

        DemoAssert.True(initialGeneration == 0, "普通新对象应从第 0 代开始");
        DemoAssert.True(
            generationAfterCollection > initialGeneration && gen0CollectionDelta >= 1,
            "强制第 0 代收集后存活对象应晋升，进程计数应增加");
        DemoAssert.True(
            newArrayMeasurement.AllocatedBytes > pooledMeasurement.AllocatedBytes,
            "预热后的 ArrayPool 路径应比重复 new 数组分配更少");
        DemoAssert.True(
            pooledMeasurement.MinimumRentedLength >= BufferSize &&
            pooledMeasurement.Checksum == newArrayMeasurement.Checksum,
            "池化缓冲区容量和计算结果应保持不变");
        DemoAssert.True(
            accessibleBeforeCollection && !accessibleAfterCollection,
            "仅由弱引用持有的对象应能被完整 GC 回收");

        context.WriteLine();
        context.WriteLine("说明：GC.Collect 只用于这里的可观察实验；生产代码通常应让 GC 自行选择收集时机。池化也应只用于已测得的高频、大缓冲区路径，并始终归还租借对象。");

        return ValueTask.CompletedTask;
    }

    private static AllocationMeasurement MeasureNewArrays()
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        var checksum = 0;

        for (var index = 0; index < IterationCount; index++)
        {
            var buffer = new byte[BufferSize];
            buffer[0] = (byte)index;
            checksum += buffer[0];
            GC.KeepAlive(buffer);
        }

        return new AllocationMeasurement(
            GC.GetAllocatedBytesForCurrentThread() - before,
            checksum,
            BufferSize);
    }

    private static AllocationMeasurement MeasurePooledArrays()
    {
        var pool = ArrayPool<byte>.Shared;
        var warmup = pool.Rent(BufferSize);
        pool.Return(warmup, clearArray: true);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var checksum = 0;
        var minimumRentedLength = int.MaxValue;

        for (var index = 0; index < IterationCount; index++)
        {
            var buffer = pool.Rent(BufferSize);
            try
            {
                minimumRentedLength = Math.Min(minimumRentedLength, buffer.Length);
                buffer[0] = (byte)index;
                checksum += buffer[0];
            }
            finally
            {
                pool.Return(buffer, clearArray: true);
            }
        }

        return new AllocationMeasurement(
            GC.GetAllocatedBytesForCurrentThread() - before,
            checksum,
            minimumRentedLength);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<object> CreateWeakReference()
    {
        var target = new object();
        return new WeakReference<object>(target);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool CanAccessTarget(WeakReference<object> weakReference) =>
        weakReference.TryGetTarget(out _);

    private static void ForceFullCollectionForWeakReferenceExperiment()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    private readonly record struct AllocationMeasurement(
        long AllocatedBytes,
        int Checksum,
        int MinimumRentedLength);
}
