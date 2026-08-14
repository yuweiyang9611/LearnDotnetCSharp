using System.Runtime.InteropServices;
using System.Text;
using LearnDotnetCSharp.Infrastructure;

namespace LearnDotnetCSharp.Demos.Interop;

public sealed unsafe class CppAbiInteropDemo : IDemo
{
    public DemoMetadata Metadata { get; } = new(
        "interop.cpp-opaque-handle",
        "interop",
        "C++：extern C 边界与不透明对象句柄",
        "C++ 在库内保留 class、vector、string 与异常；跨 .NET 边界只暴露 C ABI、状态码、UTF-8 缓冲区和 SafeHandle。",
        [25],
        ["C++", "extern C", "opaque handle", "SafeHandle", "UTF-8 buffer", "exception boundary"]);

    public DemoAvailability Availability =>
        OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64
            ? DemoAvailability.Supported
            : DemoAvailability.Skip("The repository builds the C++ ABI library only for Windows x64.");

    public ValueTask RunAsync(DemoContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var createStatus = LearnCpp.learn_cpp_counter_create(10, out var nativeHandle);
        DemoAssert.True(createStatus == 0 && nativeHandle != 0, "C++ 工厂应创建有效的不透明句柄");
        using var counter = new CppCounterHandle(nativeHandle);

        var firstStatus = LearnCpp.learn_cpp_counter_add(counter, 5, out var firstValue);
        var overflowStatus = LearnCpp.learn_cpp_counter_add(counter, int.MaxValue, out _);
        var secondStatus = LearnCpp.learn_cpp_counter_add(counter, -3, out var secondValue);

        var history = new int[8];
        int historyStatus;
        int historyWritten;
        fixed (int* historyPointer = history)
        {
            historyStatus = LearnCpp.learn_cpp_counter_copy_history(
                counter,
                historyPointer,
                history.Length,
                out historyWritten);
        }

        var utf8 = new byte[128];
        int descriptionStatus;
        int descriptionWritten;
        fixed (byte* utf8Pointer = utf8)
        {
            descriptionStatus = LearnCpp.learn_cpp_counter_describe_utf8(
                counter,
                utf8Pointer,
                utf8.Length,
                out descriptionWritten);
        }

        var description = Encoding.UTF8.GetString(utf8, 0, descriptionWritten);
        var managedHistory = history.AsSpan(0, historyWritten).ToArray();

        context.WriteProperty("values", $"{firstValue} -> overflow status {overflowStatus} -> {secondValue}");
        context.WriteProperty("C++ vector history", string.Join(", ", managedHistory));
        context.WriteProperty("C++ string as UTF-8", description);
        context.WriteProperty("SafeHandle valid", !counter.IsInvalid);

        DemoAssert.True(
            firstStatus == 0 && secondStatus == 0 && historyStatus == 0 && descriptionStatus == 0,
            "正常 C++ 操作应返回成功状态码");
        DemoAssert.Equal(2, overflowStatus, "C++ 数值越界应转换为状态码而不是让异常跨 ABI");
        DemoAssert.SequenceEqual([10, 15, 12], managedHistory, "C++ vector 应只保留成功更新的历史");
        DemoAssert.Equal("Counter(value=12, history=3)", description, "UTF-8 缓冲区应承载 C++ string 的稳定副本");

        return ValueTask.CompletedTask;
    }
}

public sealed class CppCounterHandle : SafeHandle
{
    public CppCounterHandle()
        : base(IntPtr.Zero, ownsHandle: true)
    {
    }

    internal CppCounterHandle(nint handle)
        : this()
    {
        SetHandle(handle);
    }

    public override bool IsInvalid => handle == IntPtr.Zero;

    protected override bool ReleaseHandle()
    {
        LearnCpp.learn_cpp_counter_destroy(handle);
        return true;
    }
}

internal static partial class LearnCpp
{
    private const string LibraryName = "learn_cpp";

    [LibraryImport(LibraryName)]
    internal static partial int learn_cpp_counter_create(int initialValue, out nint result);

    [LibraryImport(LibraryName)]
    internal static partial int learn_cpp_counter_add(
        CppCounterHandle counter,
        int delta,
        out int value);

    [LibraryImport(LibraryName)]
    internal static unsafe partial int learn_cpp_counter_copy_history(
        CppCounterHandle counter,
        int* output,
        int capacity,
        out int written);

    [LibraryImport(LibraryName)]
    internal static unsafe partial int learn_cpp_counter_describe_utf8(
        CppCounterHandle counter,
        byte* output,
        int capacity,
        out int written);

    [LibraryImport(LibraryName)]
    internal static partial void learn_cpp_counter_destroy(nint counter);
}
