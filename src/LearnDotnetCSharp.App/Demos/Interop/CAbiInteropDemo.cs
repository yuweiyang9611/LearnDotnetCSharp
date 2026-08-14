using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using LearnDotnetCSharp.Infrastructure;

namespace LearnDotnetCSharp.Demos.Interop;

public sealed unsafe class CAbiInteropDemo : IDemo
{
    public DemoMetadata Metadata { get; } = new(
        "interop.c-abi",
        "interop",
        "C ABI：结构体、指针与反向回调",
        "调用仓库内编译的 C 动态库，传递 blittable 数组/结构体，并以 UnmanagedCallersOnly 函数指针从 C 回调 C#。",
        [25],
        ["C ABI", "LibraryImport", "blittable struct", "fixed", "function pointer", "UnmanagedCallersOnly"]);

    public DemoAvailability Availability =>
        OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64
            ? DemoAvailability.Supported
            : DemoAvailability.Skip("The repository builds the C ABI library only for Windows x64.");

    public ValueTask RunAsync(DemoContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        double[] samples = [1.5, 2.5, 10.0];
        CStatistics statistics;
        int analyzeStatus;
        fixed (double* samplesPointer = samples)
        {
            analyzeStatus = LearnC.learn_c_analyze_f64(samplesPointer, samples.Length, out statistics);
        }

        int[] values = [1, 2, 3, 4];
        var bias = 10;
        long transformedSum;
        int callbackStatus;
        fixed (int* valuesPointer = values)
        {
            callbackStatus = LearnC.learn_c_transform_sum_i32(
                valuesPointer,
                values.Length,
                (nint)(delegate* unmanaged[Cdecl]<int, void*, int>)&SquareAndAddBias,
                &bias,
                out transformedSum);
        }

        context.WriteProperty("C struct", $"count={statistics.Count}, sum={statistics.Sum}, mean={statistics.Mean:F6}");
        context.WriteProperty("C -> C# callback sum", transformedSum);
        context.WriteProperty("status codes", $"analyze={analyzeStatus}, callback={callbackStatus}");

        DemoAssert.True(analyzeStatus == 0 && callbackStatus == 0, "C API 应以零状态码表示成功");
        DemoAssert.True(
            statistics.Count == 3 && statistics.Sum == 14.0 && Math.Abs(statistics.Mean - (14.0 / 3.0)) < 1e-12,
            "C 结构体布局和 double 数组指针应与 C# 声明一致");
        DemoAssert.Equal(70L, transformedSum, "C 调用的 C# unmanaged 回调应计算平方加偏置之和");

        return ValueTask.CompletedTask;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int SquareAndAddBias(int value, void* context)
    {
        var bias = *(int*)context;
        return (value * value) + bias;
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct CStatistics
{
    public int Count;
    public double Sum;
    public double Mean;
}

internal static partial class LearnC
{
    private const string LibraryName = "learn_c";

    [LibraryImport(LibraryName)]
    internal static unsafe partial int learn_c_analyze_f64(
        double* values,
        int length,
        out CStatistics result);

    [LibraryImport(LibraryName)]
    internal static unsafe partial int learn_c_transform_sum_i32(
        int* values,
        int length,
        nint transform,
        void* context,
        out long result);
}
