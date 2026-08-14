namespace LearnDotnetCSharp.Infrastructure;

public sealed class DemoContext(TextWriter output)
{
    public TextWriter Output { get; } = output ?? throw new ArgumentNullException(nameof(output));

    public void WriteLine(string value = "") => Output.WriteLine(value);

    public void WriteProperty(string name, object? value) => Output.WriteLine($"  {name,-24}: {value}");
}
