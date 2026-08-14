namespace LearnDotnetCSharp.Infrastructure;

public sealed record DemoMetadata(
    string Id,
    string Category,
    string Title,
    string Description,
    IReadOnlyList<int> PdfChapters,
    IReadOnlyList<string> Highlights);
