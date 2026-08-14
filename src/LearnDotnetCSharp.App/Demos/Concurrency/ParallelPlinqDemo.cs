using LearnDotnetCSharp.Infrastructure;

namespace LearnDotnetCSharp.Demos.Concurrency;

public sealed class ParallelPlinqDemo : IDemo
{
    public DemoMetadata Metadata { get; } = new(
        "concurrency.parallel-plinq",
        "concurrency",
        "Parallel 与 PLINQ",
        "使用 Parallel.For 写入互不重叠的槽位，再用有序 PLINQ 并行筛选质数并与顺序查询核对。",
        [23],
        ["Parallel.For", "PLINQ", "AsOrdered", "Cancellation"]);

    public ValueTask RunAsync(DemoContext context, CancellationToken cancellationToken)
    {
        var numbers = Enumerable.Range(1, 2_000).ToArray();
        var squares = new long[numbers.Length];
        var degree = Math.Max(1, Math.Min(Environment.ProcessorCount, 4));
        var options = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = degree,
        };

        var loopResult = Parallel.For(
            0,
            numbers.Length,
            options,
            index => squares[index] = checked((long)numbers[index] * numbers[index]));

        var expectedSquareSum = numbers.Sum(number => checked((long)number * number));
        var parallelSquareSum = squares.Sum();

        var parallelPrimes = numbers
            .AsParallel()
            .AsOrdered()
            .WithDegreeOfParallelism(degree)
            .WithCancellation(cancellationToken)
            .Where(IsPrime)
            .ToArray();
        var sequentialPrimes = numbers.Where(IsPrime).ToArray();
        var plinqMatches = parallelPrimes.SequenceEqual(sequentialPrimes);

        if (!loopResult.IsCompleted || parallelSquareSum != expectedSquareSum || !plinqMatches)
        {
            throw new InvalidOperationException("Parallel 或 PLINQ 结果与顺序基线不一致。");
        }

        context.WriteProperty("Max degree", degree);
        context.WriteProperty("Parallel loop completed", loopResult.IsCompleted);
        context.WriteProperty("Square sum exact", parallelSquareSum == expectedSquareSum);
        context.WriteProperty("Prime count", parallelPrimes.Length);
        context.WriteProperty("PLINQ ordered & exact", plinqMatches);

        return ValueTask.CompletedTask;
    }

    private static bool IsPrime(int candidate)
    {
        if (candidate < 2)
        {
            return false;
        }

        for (var divisor = 2; divisor <= candidate / divisor; divisor++)
        {
            if (candidate % divisor == 0)
            {
                return false;
            }
        }

        return true;
    }
}
