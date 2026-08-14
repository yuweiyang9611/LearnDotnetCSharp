namespace LearnDotnetCSharp.Infrastructure;

public interface IDemo
{
    DemoMetadata Metadata { get; }

    DemoAvailability Availability => DemoAvailability.Supported;

    ValueTask RunAsync(DemoContext context, CancellationToken cancellationToken);
}
