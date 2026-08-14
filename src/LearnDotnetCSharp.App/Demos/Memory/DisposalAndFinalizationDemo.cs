using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using LearnDotnetCSharp.Infrastructure;
using Microsoft.Win32.SafeHandles;

namespace LearnDotnetCSharp.Demos.Memory;

public sealed class DisposalAndFinalizationDemo : IDemo
{
    public DemoMetadata Metadata { get; } = new(
        "memory.disposal-finalization",
        "memory",
        "Dispose、异步释放、SafeHandle 与终结器",
        "以非托管缓冲区演示 SafeHandle 所有权、IDisposable/IAsyncDisposable，并用受控 GC 实验验证终结器与 SuppressFinalize。",
        [12],
        ["IDisposable", "IAsyncDisposable", "SafeHandle", "finalizer", "GC.SuppressFinalize", "unmanaged memory"]);

    public async ValueTask RunAsync(DemoContext context, CancellationToken cancellationToken)
    {
        var releaseState = new ReleaseState();
        await using (var owner = new NativeBufferOwner(sizeof(int), releaseState))
        {
            owner.WriteInt32(2026);
            DemoAssert.Equal(2026, owner.ReadInt32(), "SafeHandle 管理的非托管缓冲区应可读写");
            await owner.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        var finalizationState = new FinalizationState();
        CreateAndDisposeProbe(finalizationState);
        var abandoned = CreateAbandonedProbe(finalizationState);
        ForceFinalization(abandoned);

        context.WriteProperty("SafeHandle releases", releaseState.ReleaseCount);
        context.WriteProperty("Dispose calls", finalizationState.DisposeCount);
        context.WriteProperty("finalizer calls", finalizationState.FinalizerCount);
        context.WriteProperty("abandoned object collected", !abandoned.IsAlive);

        DemoAssert.Equal(1, releaseState.ReleaseCount, "SafeHandle 应恰好释放一次非托管内存");
        DemoAssert.Equal(1, finalizationState.DisposeCount, "显式 Dispose 应被记录一次");
        DemoAssert.Equal(1, finalizationState.FinalizerCount, "未释放对象应由终结器处理一次");
        DemoAssert.True(!abandoned.IsAlive, "只剩弱引用的终结器对象应被回收");

        context.WriteLine("  SafeHandle 自带可靠终结保障；拥有 SafeHandle 的托管类型通常不需要再写自己的终结器。终结器实验中的 GC.Collect 仅用于教学验证。");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CreateAndDisposeProbe(FinalizationState state)
    {
        using var probe = new FinalizationProbe(state);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateAbandonedProbe(FinalizationState state)
    {
        var probe = new FinalizationProbe(state);
        return new WeakReference(probe);
    }

    private static void ForceFinalization(WeakReference weakReference)
    {
        for (var attempt = 0; attempt < 5 && weakReference.IsAlive; attempt++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        }
    }

    private sealed class NativeBufferOwner : IDisposable, IAsyncDisposable
    {
        private SafeHGlobalBuffer? buffer;

        public NativeBufferOwner(int byteLength, ReleaseState releaseState)
        {
            buffer = new SafeHGlobalBuffer(byteLength, releaseState);
        }

        public int ReadInt32() => Marshal.ReadInt32(GetHandle());

        public void WriteInt32(int value) => Marshal.WriteInt32(GetHandle(), value);

        public async ValueTask FlushAsync(CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(buffer is null, this);
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
        }

        public void Dispose()
        {
            buffer?.Dispose();
            buffer = null;
            GC.SuppressFinalize(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        private nint GetHandle()
        {
            ObjectDisposedException.ThrowIf(buffer is null, this);
            return buffer.DangerousGetHandle();
        }
    }

    private sealed class SafeHGlobalBuffer : SafeHandleZeroOrMinusOneIsInvalid
    {
        private readonly ReleaseState releaseState;

        public SafeHGlobalBuffer(int byteLength, ReleaseState releaseState)
            : base(ownsHandle: true)
        {
            this.releaseState = releaseState;
            SetHandle(Marshal.AllocHGlobal(byteLength));
        }

        protected override bool ReleaseHandle()
        {
            Marshal.FreeHGlobal(handle);
            Interlocked.Increment(ref releaseState.ReleaseCount);
            return true;
        }
    }

    private sealed class FinalizationProbe(FinalizationState state) : IDisposable
    {
        private bool disposed;

        ~FinalizationProbe()
        {
            Interlocked.Increment(ref state.FinalizerCount);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            Interlocked.Increment(ref state.DisposeCount);
            GC.SuppressFinalize(this);
        }
    }

    private sealed class ReleaseState
    {
        public int ReleaseCount;
    }

    private sealed class FinalizationState
    {
        public int DisposeCount;
        public int FinalizerCount;
    }
}
