using System.Buffers;
using LearnDotnetCSharp.Infrastructure;

namespace LearnDotnetCSharp.Demos.Memory;

public sealed class SpanMemoryOwnershipDemo : IDemo
{
    public DemoMetadata Metadata { get; } = new(
        "memory.span-memory-ownership",
        "memory",
        "Span、Memory 与缓冲区所有权",
        "对比栈限定 Span<T> 与可跨 await 保存的 Memory<T>，并用 IMemoryOwner<T> 明确池化缓冲区的归还责任。",
        [24],
        ["Span<T>", "Memory<T>", "MemoryPool<T>", "IMemoryOwner<T>", "ownership"]);

    public async ValueTask RunAsync(DemoContext context, CancellationToken cancellationToken)
    {
        const int length = 16;
        using var owner = MemoryPool<byte>.Shared.Rent(length);
        var memory = owner.Memory[..length];

        for (var index = 0; index < memory.Length; index++)
        {
            memory.Span[index] = (byte)index;
        }

        var checksum = await SumAfterYieldAsync(memory, cancellationToken).ConfigureAwait(false);
        var readOnlyView = (ReadOnlyMemory<byte>)memory;

        context.WriteProperty("Requested / rented", $"{length} / {owner.Memory.Length}");
        context.WriteProperty("Checksum after await", checksum);
        context.WriteProperty("ReadOnlyMemory length", readOnlyView.Length);
        context.WriteLine("  Span<T> 是 ref struct，不能跨 await 保存；Memory<T> 可以，访问数据时再短暂取得 .Span。");
        context.WriteLine("  IMemoryOwner<T> 代表租借所有权；离开 using 后缓冲区即归还池，不能继续使用旧视图。");

        DemoAssert.True(owner.Memory.Length >= length, "内存池必须返回至少请求长度的缓冲区");
        DemoAssert.Equal(120, checksum, "跨 await 后 Memory<T> 中的数据应保持不变");
        DemoAssert.Equal(length, readOnlyView.Length, "只读视图应保持切片长度");
    }

    private static async Task<int> SumAfterYieldAsync(
        Memory<byte> memory,
        CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        var sum = 0;
        foreach (var value in memory.Span)
        {
            sum += value;
        }

        return sum;
    }
}
