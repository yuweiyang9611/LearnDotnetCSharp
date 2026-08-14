using LearnDotnetCSharp.Infrastructure;

namespace LearnDotnetCSharp.Tests;

[TestClass]
public sealed class DemoExecutionTests
{
    [TestMethod]
    public async Task RunAsyncReturnsPassedWhenDemoCompletes()
    {
        var output = new StringWriter();
        var demo = new StubDemo(DemoAvailability.Supported, static _ => ValueTask.CompletedTask);

        var result = await new DemoRunner(new DemoContext(output))
            .RunAsync(demo, CancellationToken.None);

        Assert.AreEqual(DemoStatus.Passed, result.Status);
        StringAssert.Contains(output.ToString(), "--- passed in");
    }

    [TestMethod]
    public async Task RunAsyncReturnsSkippedWithoutInvokingUnavailableDemo()
    {
        var invoked = false;
        var output = new StringWriter();
        var demo = new StubDemo(
            DemoAvailability.Skip("requires a platform capability"),
            _ =>
            {
                invoked = true;
                return ValueTask.CompletedTask;
            });

        var result = await new DemoRunner(new DemoContext(output))
            .RunAsync(demo, CancellationToken.None);

        Assert.AreEqual(DemoStatus.Skipped, result.Status);
        Assert.IsFalse(invoked);
        Assert.AreEqual("requires a platform capability", result.Message);
        StringAssert.Contains(output.ToString(), "--- skipped:");
    }

    [TestMethod]
    public async Task RunAsyncReturnsFailedWithOriginalException()
    {
        var expected = new InvalidOperationException("deterministic failure");
        var demo = new StubDemo(
            DemoAvailability.Supported,
            _ => ValueTask.FromException(expected));

        var result = await new DemoRunner(new DemoContext(new StringWriter()))
            .RunAsync(demo, CancellationToken.None);

        Assert.AreEqual(DemoStatus.Failed, result.Status);
        Assert.AreSame(expected, result.Error);
        Assert.AreEqual(expected.Message, result.Message);
    }

    [TestMethod]
    public void CliInfrastructureHandlesExitCodesAndArguments()
    {
        DemoStatus[] statuses =
        [
            DemoStatus.Passed,
            DemoStatus.Skipped,
            DemoStatus.Failed,
            DemoStatus.Timeout,
        ];

        foreach (var status in statuses)
        {
            var exitCode = DemoExitCodes.FromStatus(status);
            Assert.AreEqual(status, DemoExitCodes.ToStatus(exitCode));
        }

        Assert.AreEqual(DemoStatus.Failed, DemoExitCodes.ToStatus(137));
        Assert.AreEqual(DemoExitCodes.Passed, Program.GetSelfTestExitCode(failed: 0, timedOut: 0));
        Assert.AreEqual(DemoExitCodes.Timeout, Program.GetSelfTestExitCode(failed: 0, timedOut: 1));
        Assert.AreEqual(DemoExitCodes.Failed, Program.GetSelfTestExitCode(failed: 1, timedOut: 0));
        Assert.AreEqual(DemoExitCodes.Failed, Program.GetSelfTestExitCode(failed: 1, timedOut: 1));

        string[] arguments = ["help", "HELP", "Help", "--help", "--HELP", "-h", "-H", "/?"];

        foreach (var argument in arguments)
        {
            Assert.IsTrue(Program.IsHelp(argument), argument);
        }

        Assert.IsTrue(Program.HasValidArgumentCount("list", 1));
        Assert.IsTrue(Program.HasValidArgumentCount("list", 2));
        Assert.IsFalse(Program.HasValidArgumentCount("list", 3));

        Assert.IsTrue(Program.HasValidArgumentCount("run-all", 1));
        Assert.IsFalse(Program.HasValidArgumentCount("run-all", 2));

        Assert.IsTrue(Program.HasValidArgumentCount("self-test", 1));
        Assert.IsFalse(Program.HasValidArgumentCount("self-test", 2));
    }

    [TestMethod]
    public async Task BoundedOutputContinuesDrainingAndMarksTruncation()
    {
        const string input = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

        var captured = await Program.ReadBoundedOutputAsync(new StringReader(input), maximumCharacters: 10);

        StringAssert.StartsWith(captured, "0123456789");
        StringAssert.Contains(captured, "<output truncated after 10 characters>");
        Assert.DoesNotContain('A', captured);
    }

    private sealed class StubDemo(
        DemoAvailability availability,
        Func<CancellationToken, ValueTask> run) : IDemo
    {
        public DemoMetadata Metadata { get; } = new(
            "test.stub",
            "test",
            "Stub",
            "Test-only demo.",
            [],
            []);

        public DemoAvailability Availability { get; } = availability;

        public ValueTask RunAsync(DemoContext context, CancellationToken cancellationToken) =>
            run(cancellationToken);
    }
}
