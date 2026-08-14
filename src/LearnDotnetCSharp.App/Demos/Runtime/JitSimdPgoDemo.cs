using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;
using LearnDotnetCSharp.Infrastructure;

namespace LearnDotnetCSharp.Demos.Runtime;

public sealed class JitSimdPgoDemo : IDemo
{
    public DemoMetadata Metadata { get; } = new(
        "runtime.jit-simd-pgo",
        "runtime",
        "JIT、SIMD、硬件内建函数与动态 PGO 边界",
        "比较标量与 Vector<T> 结果，按 CPU 能力选择 X86/Arm 内建函数，并区分预热、环境覆盖项和真实性能测量。",
        [1, 5, 13, 23],
        ["JIT", "tiered compilation", "dynamic PGO", "Vector<T>", "Vector128", "hardware intrinsics", "capability check"]);

    public ValueTask RunAsync(DemoContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var values = Enumerable.Range(1, 1025).ToArray();
        var scalarSum = ScalarSum(values);
        var portableVectorSum = PortableVectorSum(values);
        var intrinsicResult = RunSupportedIntrinsic();

        var coldResult = BranchHeavyScore(values);
        var warmChecksum = 0L;
        for (var iteration = 0; iteration < 2_000; iteration++)
        {
            warmChecksum += BranchHeavyScore(values);
        }

        var warmResult = BranchHeavyScore(values);
        var tieredCompilationOverride = ReadRuntimeOverride("DOTNET_TieredCompilation", "COMPlus_TieredCompilation");
        var tieredPgoOverride = ReadRuntimeOverride("DOTNET_TieredPGO", "COMPlus_TieredPGO");

        context.WriteProperty("scalar sum", scalarSum);
        context.WriteProperty("Vector<T> sum", portableVectorSum);
        context.WriteProperty("Vector accelerated", Vector.IsHardwareAccelerated);
        context.WriteProperty("Vector<int> lanes", Vector<int>.Count);
        context.WriteProperty("Vector128 accelerated", Vector128.IsHardwareAccelerated);
        context.WriteProperty("Vector256 accelerated", Vector256.IsHardwareAccelerated);
        context.WriteProperty("intrinsic path", intrinsicResult.Path);
        context.WriteProperty("intrinsic values", string.Join(", ", intrinsicResult.Values));
        context.WriteProperty("tiered compilation override", tieredCompilationOverride);
        context.WriteProperty("dynamic PGO override", tieredPgoOverride);
        context.WriteProperty("warm-up checksum", warmChecksum);
        context.WriteLine("  环境变量未设置表示采用运行时默认值，不等于关闭；预热也不证明某次调用已经完成分层重编译或 PGO 优化。");
        context.WriteLine("  性能结论应使用 Release、固定运行环境和 BenchmarkDotNet/硬件计数器，而不是本实验的单次耗时。");

        DemoAssert.Equal(scalarSum, portableVectorSum, "Vector<T> 求和结果应与标量实现一致");
        DemoAssert.SequenceEqual([11, 22, 33, 44], intrinsicResult.Values, "能力分支或软件后备应得到相同向量加法结果");
        DemoAssert.True(coldResult == warmResult && warmChecksum == (long)coldResult * 2_000, "预热前后业务结果必须保持一致");
        return ValueTask.CompletedTask;
    }

    private static int ScalarSum(ReadOnlySpan<int> values)
    {
        var sum = 0;
        foreach (var value in values)
        {
            sum += value;
        }

        return sum;
    }

    private static int PortableVectorSum(ReadOnlySpan<int> values)
    {
        var vectorSum = Vector<int>.Zero;
        var index = 0;
        for (; index <= values.Length - Vector<int>.Count; index += Vector<int>.Count)
        {
            vectorSum += new Vector<int>(values.Slice(index, Vector<int>.Count));
        }

        var sum = 0;
        for (var lane = 0; lane < Vector<int>.Count; lane++)
        {
            sum += vectorSum[lane];
        }

        for (; index < values.Length; index++)
        {
            sum += values[index];
        }

        return sum;
    }

    private static IntrinsicResult RunSupportedIntrinsic()
    {
        var left = Vector128.Create(10, 20, 30, 40);
        var right = Vector128.Create(1, 2, 3, 4);
        Vector128<int> sum;
        string path;

        if (Sse2.IsSupported)
        {
            sum = Sse2.Add(left, right);
            path = "Sse2.Add";
        }
        else if (AdvSimd.IsSupported)
        {
            sum = AdvSimd.Add(left, right);
            path = "AdvSimd.Add";
        }
        else
        {
            sum = left + right;
            path = "Vector128 software/portable fallback";
        }

        return new IntrinsicResult(path, [sum.GetElement(0), sum.GetElement(1), sum.GetElement(2), sum.GetElement(3)]);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int BranchHeavyScore(ReadOnlySpan<int> values)
    {
        var score = 0;
        foreach (var value in values)
        {
            score += (value % 5) switch
            {
                0 => value / 5,
                1 => value * 2,
                2 => -value,
                3 => value + 17,
                _ => value - 11,
            };
        }

        return score;
    }

    private static string ReadRuntimeOverride(string dotnetName, string legacyName)
    {
        var dotnetValue = Environment.GetEnvironmentVariable(dotnetName);
        if (!string.IsNullOrWhiteSpace(dotnetValue))
        {
            return $"{dotnetName}={dotnetValue}";
        }

        var legacyValue = Environment.GetEnvironmentVariable(legacyName);
        return string.IsNullOrWhiteSpace(legacyValue)
            ? "<unset; runtime default>"
            : $"{legacyName}={legacyValue}";
    }

    private sealed record IntrinsicResult(string Path, int[] Values);
}
