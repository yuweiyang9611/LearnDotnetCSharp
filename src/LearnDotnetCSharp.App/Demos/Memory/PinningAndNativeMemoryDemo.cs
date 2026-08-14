using System.Buffers.Binary;
using System.Runtime.InteropServices;
using LearnDotnetCSharp.Infrastructure;
using Microsoft.Win32.SafeHandles;

namespace LearnDotnetCSharp.Demos.Memory;

public sealed class PinningAndNativeMemoryDemo : IDemo
{
    public DemoMetadata Metadata { get; } = new(
        "memory.pinning-native-memory",
        "memory",
        "固定对象、原生内存与 SafeHandle",
        "比较 pinned heap 与 GCHandle 固定，使用 MemoryMarshal/BinaryPrimitives 解释内存视图，并以对齐分配和 SafeHandle 管理原生所有权。",
        [12, 24, 25],
        ["pinned object heap", "GCHandle", "MemoryMarshal", "endianness", "NativeMemory", "alignment", "SafeHandle"]);

    public unsafe ValueTask RunAsync(DemoContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var pinnedArray = GC.AllocateUninitializedArray<byte>(256, pinned: true);
        nuint pinnedBefore;
        fixed (byte* pointer = pinnedArray)
        {
            pinnedBefore = (nuint)pointer;
        }

        ForceCompactingCollection();
        nuint pinnedAfter;
        fixed (byte* pointer = pinnedArray)
        {
            pinnedAfter = (nuint)pointer;
        }

        var regularArray = new byte[256];
        var pinnedHandle = GCHandle.Alloc(regularArray, GCHandleType.Pinned);
        nuint handleBefore;
        nuint handleAfter;
        try
        {
            handleBefore = (nuint)pinnedHandle.AddrOfPinnedObject();
            ForceCompactingCollection();
            handleAfter = (nuint)pinnedHandle.AddrOfPinnedObject();
        }
        finally
        {
            pinnedHandle.Free();
        }

        Span<byte> bytes = stackalloc byte[sizeof(int) * 2];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, 0x01020304);
        BinaryPrimitives.WriteInt32BigEndian(bytes[sizeof(int)..], 0x01020304);
        var nativeView = MemoryMarshal.Cast<byte, int>(bytes);
        var nativeFirst = nativeView[0];
        var nativeSecond = nativeView[1];

        const nuint alignment = 64;
        const nuint byteLength = 256;
        var alignedPointer = NativeMemory.AlignedAlloc(byteLength, alignment);
        if (alignedPointer is null)
        {
            throw new InvalidOperationException("NativeMemory.AlignedAlloc 未能分配缓冲区。");
        }

        var alignedAddress = (nuint)alignedPointer;
        var alignedChecksum = 0;
        try
        {
            var alignedSpan = new Span<byte>(alignedPointer, checked((int)byteLength));
            alignedSpan.Fill(7);
            foreach (var value in alignedSpan)
            {
                alignedChecksum += value;
            }
        }
        finally
        {
            NativeMemory.AlignedFree(alignedPointer);
        }

        var releaseState = new ReleaseState();
        NativeBuffer? owner = new(byteLength: 16, releaseState);
        try
        {
            owner.AsSpan().Fill(0x2A);
            DemoAssert.True(owner.AsSpan().ToArray().All(value => value == 0x2A), "SafeHandle 管理的原生缓冲区应可安全读写");
        }
        finally
        {
            owner.Dispose();
        }

        context.WriteProperty("pinned heap stable", pinnedBefore == pinnedAfter);
        context.WriteProperty("GCHandle stable", handleBefore == handleAfter);
        context.WriteProperty("native endianness", BitConverter.IsLittleEndian ? "little-endian" : "big-endian");
        context.WriteProperty("native int view", $"0x{nativeFirst:X8}, 0x{nativeSecond:X8}");
        context.WriteProperty("aligned address mod 64", alignedAddress % alignment);
        context.WriteProperty("aligned checksum", alignedChecksum);
        context.WriteProperty("SafeHandle releases", releaseState.ReleaseCount);
        context.WriteLine("  固定会限制 GC 移动对象；只在需要稳定地址的最短时间内固定，并用 SafeHandle 表达原生所有权。");

        DemoAssert.Equal(pinnedBefore, pinnedAfter, "pinned object heap 数组地址在压缩 GC 前后应稳定");
        DemoAssert.Equal(handleBefore, handleAfter, "GCHandleType.Pinned 生效期间地址应稳定");
        DemoAssert.True(
            BitConverter.IsLittleEndian
                ? nativeFirst == 0x01020304 && nativeSecond == 0x04030201
                : nativeFirst == 0x04030201 && nativeSecond == 0x01020304,
            "MemoryMarshal 原生视图应反映平台端序，而 BinaryPrimitives 应显式编码端序");
        DemoAssert.Equal((nuint)0, alignedAddress % alignment, "AlignedAlloc 返回地址应满足 64 字节对齐");
        DemoAssert.Equal(1792, alignedChecksum, "对齐缓冲区内容校验和应正确");
        DemoAssert.Equal(1, releaseState.ReleaseCount, "SafeHandle 应精确释放一次 NativeMemory 缓冲区");
        return ValueTask.CompletedTask;
    }

    private static void ForceCompactingCollection()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
    }

    private sealed unsafe class NativeBuffer : SafeHandleZeroOrMinusOneIsInvalid
    {
        private readonly int byteLength;
        private readonly ReleaseState releaseState;

        public NativeBuffer(int byteLength, ReleaseState releaseState)
            : base(ownsHandle: true)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(byteLength);
            this.byteLength = byteLength;
            this.releaseState = releaseState;
            var pointer = NativeMemory.Alloc((nuint)byteLength);
            if (pointer is null)
            {
                throw new InvalidOperationException("NativeMemory.Alloc 未能分配缓冲区。");
            }

            SetHandle((nint)pointer);
        }

        public Span<byte> AsSpan()
        {
            ObjectDisposedException.ThrowIf(IsClosed, this);
            return new Span<byte>((void*)handle, byteLength);
        }

        protected override bool ReleaseHandle()
        {
            NativeMemory.Free((void*)handle);
            Interlocked.Increment(ref releaseState.ReleaseCount);
            return true;
        }
    }

    private sealed class ReleaseState
    {
        public int ReleaseCount;
    }
}
