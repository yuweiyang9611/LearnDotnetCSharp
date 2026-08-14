using System.Collections.Concurrent;

namespace LearnDotnetCSharp.Infrastructure;

/// <summary>
/// Describes one deterministic fault injection point.
/// </summary>
/// <param name="Point">Stable name of the boundary where the fault is injected.</param>
/// <param name="Invocation">One-based invocation number that should fail.</param>
internal readonly record struct FaultRule(string Point, int Invocation);

/// <summary>
/// A thread-safe, deterministic fault schedule for demos and tests.
/// </summary>
/// <remarks>
/// A rule fires only when its named point reaches the configured one-based invocation.
/// Independent names have independent counters, so callers do not need timing-based sleeps
/// to reproduce a transient failure.
/// </remarks>
internal sealed class FaultPlan
{
    private readonly ConcurrentDictionary<string, FaultPointState> points;

    public FaultPlan(IEnumerable<FaultRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        points = new ConcurrentDictionary<string, FaultPointState>(StringComparer.Ordinal);
        foreach (var rule in rules)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(rule.Point);
            ArgumentOutOfRangeException.ThrowIfLessThan(rule.Invocation, 1);

            if (!points.TryAdd(rule.Point, new FaultPointState(rule.Invocation)))
            {
                throw new ArgumentException($"故障点 '{rule.Point}' 只能配置一次。", nameof(rules));
            }
        }
    }

    public static FaultPlan None { get; } = new([]);

    public static FaultPlan FailOn(string point, int invocation) =>
        new([new FaultRule(point, invocation)]);

    public bool ShouldInject(string point)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(point);
        return points.TryGetValue(point, out var state) && state.ShouldInject();
    }

    public int GetInvocationCount(string point)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(point);
        return points.TryGetValue(point, out var state) ? state.InvocationCount : 0;
    }

    private sealed class FaultPointState(int targetInvocation)
    {
        private int invocationCount;

        public int InvocationCount => Volatile.Read(ref invocationCount);

        public bool ShouldInject() => Interlocked.Increment(ref invocationCount) == targetInvocation;
    }
}
