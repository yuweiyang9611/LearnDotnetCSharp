using System.Buffers;
using System.IO.Compression;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text;
using LearnDotnetCSharp.Infrastructure;

namespace LearnDotnetCSharp.Demos.Io;

public sealed class StreamsPipelinesCompressionDemo : IDemo
{
    public DemoMetadata Metadata { get; } = new(
        "io.streams-pipelines-compression",
        "io",
        "异步流、压缩与 System.IO.Pipelines",
        "把 UTF-8 数据异步写入文件，以 GZip 流压缩/解压，再通过 PipeReader 跨缓冲区解析行记录。",
        [15],
        ["FileStream", "IAsyncEnumerable", "GZipStream", "PipeReader", "ReadOnlySequence", "cancellation"]);

    public async ValueTask RunAsync(DemoContext context, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(Path.GetTempPath(), "LearnDotnetCSharp", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "records.txt");
        string[] expectedLines = ["C# 14", "LINQ", "HTTP/2", "HTTP/3", "管道-I/O"];
        var payload = Encoding.UTF8.GetBytes(string.Join('\n', expectedLines) + "\n");

        try
        {
            await using (var file = new FileStream(
                             path,
                             new FileStreamOptions
                             {
                                 Mode = FileMode.CreateNew,
                                 Access = FileAccess.Write,
                                 Share = FileShare.Read,
                                 Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                             }))
            {
                await file.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            }

            var streamedLines = new List<string>();
            await foreach (var line in ReadFileLinesAsync(path, cancellationToken).ConfigureAwait(false))
            {
                streamedLines.Add(line);
            }

            var compressed = await CompressAsync(payload, cancellationToken).ConfigureAwait(false);
            var decompressed = await DecompressAsync(compressed, cancellationToken).ConfigureAwait(false);

            var pipe = new Pipe(new PipeOptions(minimumSegmentSize: 8));
            var producer = WriteFragmentedAsync(pipe.Writer, decompressed, cancellationToken);
            var parsedLines = await ReadLinesAsync(pipe.Reader, cancellationToken).ConfigureAwait(false);
            await producer.ConfigureAwait(false);

            context.WriteProperty("file bytes", payload.Length);
            context.WriteProperty("GZip bytes", compressed.Length);
            context.WriteProperty("async stream", string.Join(" | ", streamedLines));
            context.WriteProperty("pipeline parsed", string.Join(" | ", parsedLines));

            DemoAssert.SequenceEqual(expectedLines, streamedLines, "IAsyncEnumerable 应逐行流式读取文件");
            DemoAssert.SequenceEqual(payload, decompressed, "GZip 压缩与解压应无损往返");
            DemoAssert.SequenceEqual(expectedLines, parsedLines, "PipeReader 应处理跨缓冲区的 UTF-8 行记录");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async IAsyncEnumerable<string> ReadFileLinesAsync(
        string path,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var file = new FileStream(
            path,
            new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            });
        using var reader = new StreamReader(file, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);

        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            yield return line;
        }
    }

    private static async Task<byte[]> CompressAsync(byte[] payload, CancellationToken cancellationToken)
    {
        await using var destination = new MemoryStream();
        await using (var gzip = new GZipStream(destination, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            await gzip.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        }

        return destination.ToArray();
    }

    private static async Task<byte[]> DecompressAsync(byte[] compressed, CancellationToken cancellationToken)
    {
        await using var source = new MemoryStream(compressed, writable: false);
        await using var gzip = new GZipStream(source, CompressionMode.Decompress);
        await using var destination = new MemoryStream();
        await gzip.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        return destination.ToArray();
    }

    private static async Task WriteFragmentedAsync(
        PipeWriter writer,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        try
        {
            for (var offset = 0; offset < payload.Length; offset += 3)
            {
                var length = Math.Min(3, payload.Length - offset);
                await writer.WriteAsync(payload.AsMemory(offset, length), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            await writer.CompleteAsync().ConfigureAwait(false);
        }
    }

    private static async Task<string[]> ReadLinesAsync(
        PipeReader reader,
        CancellationToken cancellationToken)
    {
        var lines = new List<string>();

        try
        {
            while (true)
            {
                var result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                var buffer = result.Buffer;

                while (TryReadLine(ref buffer, out var line))
                {
                    lines.Add(Encoding.UTF8.GetString(line));
                }

                reader.AdvanceTo(buffer.Start, buffer.End);
                if (result.IsCompleted)
                {
                    DemoAssert.True(buffer.IsEmpty, "完成的管道不应留下未终止的记录");
                    break;
                }
            }
        }
        finally
        {
            await reader.CompleteAsync().ConfigureAwait(false);
        }

        return lines.ToArray();
    }

    private static bool TryReadLine(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> line)
    {
        var position = buffer.PositionOf((byte)'\n');
        if (position is null)
        {
            line = default;
            return false;
        }

        line = buffer.Slice(0, position.Value);
        buffer = buffer.Slice(buffer.GetPosition(1, position.Value));
        return true;
    }
}
