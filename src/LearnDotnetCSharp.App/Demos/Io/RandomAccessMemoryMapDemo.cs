using System.Buffers.Binary;
using System.IO.MemoryMappedFiles;
using System.Text;
using LearnDotnetCSharp.Infrastructure;

namespace LearnDotnetCSharp.Demos.Io;

public sealed class RandomAccessMemoryMapDemo : IDemo
{
    public DemoMetadata Metadata { get; } = new(
        "io.random-access-memory-map",
        "io",
        "RandomAccess 与内存映射文件",
        "在同一文件的不同偏移量执行无共享位置指针的并发 I/O，并验证内存映射视图与 RandomAccess 彼此可见。",
        [15],
        ["RandomAccess", "SafeFileHandle", "offset I/O", "MemoryMappedFile", "binary data"]);

    public async ValueTask RunAsync(DemoContext context, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(Path.GetTempPath(), "LearnDotnetCSharp", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "random-access.bin");

        try
        {
            await using (var file = new FileStream(
                             path,
                             FileMode.CreateNew,
                             FileAccess.ReadWrite,
                             FileShare.ReadWrite,
                             bufferSize: 1,
                             FileOptions.Asynchronous | FileOptions.RandomAccess))
            {
                file.SetLength(4096);
            }

            using var handle = File.OpenHandle(
                path,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.ReadWrite,
                FileOptions.Asynchronous | FileOptions.RandomAccess);

            var header = new byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(header, 2026);
            var message = Encoding.UTF8.GetBytes("C# 14 random access");
            await Task.WhenAll(
                    RandomAccess.WriteAsync(handle, header, fileOffset: 0, cancellationToken).AsTask(),
                    RandomAccess.WriteAsync(handle, message, fileOffset: 1024, cancellationToken).AsTask())
                .ConfigureAwait(false);

            var headerRead = new byte[header.Length];
            var messageRead = new byte[message.Length];
            var reads = await Task.WhenAll(
                    RandomAccess.ReadAsync(handle, headerRead, fileOffset: 0, cancellationToken).AsTask(),
                    RandomAccess.ReadAsync(handle, messageRead, fileOffset: 1024, cancellationToken).AsTask())
                .ConfigureAwait(false);

            using (var mappingStream = new FileStream(
                       path,
                       FileMode.Open,
                       FileAccess.ReadWrite,
                       FileShare.ReadWrite))
            using (var mappedFile = MemoryMappedFile.CreateFromFile(
                       mappingStream,
                       mapName: null,
                       capacity: 0,
                       MemoryMappedFileAccess.ReadWrite,
                       HandleInheritability.None,
                       leaveOpen: true))
            using (var view = mappedFile.CreateViewAccessor(2048, sizeof(long), MemoryMappedFileAccess.ReadWrite))
            {
                view.Write(0, 0x0102_0304_0506_0708L);
                view.Flush();
            }

            var mappedBytes = new byte[sizeof(long)];
            var mappedRead = await RandomAccess
                .ReadAsync(handle, mappedBytes, fileOffset: 2048, cancellationToken)
                .ConfigureAwait(false);

            var headerValue = BinaryPrimitives.ReadInt32LittleEndian(headerRead);
            var messageValue = Encoding.UTF8.GetString(messageRead);
            var mappedValue = BinaryPrimitives.ReadInt64LittleEndian(mappedBytes);

            context.WriteProperty("random reads", string.Join(", ", reads));
            context.WriteProperty("header", headerValue);
            context.WriteProperty("message", messageValue);
            context.WriteProperty("mapped value", $"0x{mappedValue:X16}");

            DemoAssert.True(reads.SequenceEqual([header.Length, message.Length]), "位置式读取应返回各自请求的字节数");
            DemoAssert.True(headerValue == 2026 && messageValue == "C# 14 random access", "不同偏移量的并发写入不应互相覆盖");
            DemoAssert.True(mappedRead == sizeof(long) && mappedValue == 0x0102_0304_0506_0708L, "内存映射写入应能由 RandomAccess 观察到");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
