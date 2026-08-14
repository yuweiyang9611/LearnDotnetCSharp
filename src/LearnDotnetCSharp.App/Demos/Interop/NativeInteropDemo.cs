using System.Runtime.InteropServices;
using LearnDotnetCSharp.Infrastructure;

namespace LearnDotnetCSharp.Demos.Interop;

public sealed class NativeInteropDemo : IDemo
{
    public DemoMetadata Metadata { get; } = new(
        "interop.native-pinvoke",
        "interop",
        "LibraryImport 与平台原生 API",
        "用源生成 P/Invoke 在 Windows 调用 GetCurrentProcessId，在 Linux/macOS 调用 getpid；未知平台会明确跳过。",
        [25],
        ["LibraryImport", "P/Invoke", "native ABI", "cross-platform"]);

    public DemoAvailability Availability =>
        OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()
            ? DemoAvailability.Supported
            : DemoAvailability.Skip($"No native process-id binding is defined for {RuntimeInformation.OSDescription}.");

    public ValueTask RunAsync(DemoContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryGetNativeProcessId(out var nativeProcessId, out var nativeApi))
        {
            throw new InvalidOperationException("The platform availability check and native binding selection disagree.");
        }

        context.WriteProperty("native API", nativeApi);
        context.WriteProperty("native process id", nativeProcessId);
        context.WriteProperty("Environment.ProcessId", Environment.ProcessId);
        context.WriteProperty("IDs agree", nativeProcessId == Environment.ProcessId);
        DemoAssert.Equal((long)Environment.ProcessId, nativeProcessId, "原生 API 与托管 API 应返回同一进程 ID");

        return ValueTask.CompletedTask;
    }

    private static bool TryGetNativeProcessId(out long processId, out string api)
    {
        if (OperatingSystem.IsWindows())
        {
            processId = NativeMethods.GetCurrentProcessId();
            api = "kernel32!GetCurrentProcessId";
            return true;
        }

        if (OperatingSystem.IsLinux())
        {
            processId = NativeMethods.GetPidLinux();
            api = "libc!getpid";
            return true;
        }

        if (OperatingSystem.IsMacOS())
        {
            processId = NativeMethods.GetPidMacOS();
            api = "libSystem!getpid";
            return true;
        }

        processId = 0;
        api = string.Empty;
        return false;
    }
}

internal static partial class NativeMethods
{
    [LibraryImport("kernel32.dll")]
    internal static partial uint GetCurrentProcessId();

    [LibraryImport("libc", EntryPoint = "getpid")]
    internal static partial int GetPidLinux();

    [LibraryImport("/usr/lib/libSystem.B.dylib", EntryPoint = "getpid")]
    internal static partial int GetPidMacOS();
}
