using LearnDotnetCSharp.Infrastructure;

namespace LearnDotnetCSharp.Tests;

[TestClass]
public sealed class FaultPlanTests
{
    [TestMethod]
    public void ShouldInjectFiresOnlyOnConfiguredInvocation()
    {
        var plan = FaultPlan.FailOn("service.transient", invocation: 2);

        Assert.IsFalse(plan.ShouldInject("service.transient"));
        Assert.IsTrue(plan.ShouldInject("service.transient"));
        Assert.IsFalse(plan.ShouldInject("service.transient"));
        Assert.AreEqual(3, plan.GetInvocationCount("service.transient"));
    }

    [TestMethod]
    public void ShouldInjectIsAtomicUnderConcurrency()
    {
        const int calls = 200;
        var plan = FaultPlan.FailOn("parallel.boundary", invocation: 73);
        var injected = 0;

        Parallel.For(0, calls, _ =>
        {
            if (plan.ShouldInject("parallel.boundary"))
            {
                Interlocked.Increment(ref injected);
            }
        });

        Assert.AreEqual(1, injected);
        Assert.AreEqual(calls, plan.GetInvocationCount("parallel.boundary"));
    }

    [TestMethod]
    public void ConstructorRejectsDuplicateAndInvalidRules()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new FaultPlan(
        [
            new FaultRule("duplicate", 1),
            new FaultRule("duplicate", 2),
        ]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new FaultPlan([new FaultRule("invalid", 0)]));
    }

    [TestMethod]
    public void NoneNeverInjectsOrCountsUnknownPoints()
    {
        Assert.IsFalse(FaultPlan.None.ShouldInject("unconfigured"));
        Assert.AreEqual(0, FaultPlan.None.GetInvocationCount("unconfigured"));
    }
}
