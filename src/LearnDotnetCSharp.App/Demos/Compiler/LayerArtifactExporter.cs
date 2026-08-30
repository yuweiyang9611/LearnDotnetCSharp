using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LearnDotnetCSharp.Demos.Compiler;

internal static class LayerArtifactExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static async Task ExportAsync(
        string outputPath,
        bool captureJitAssembly,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        var fullOutputPath = Path.GetFullPath(outputPath);

        if (!captureJitAssembly)
        {
            var portableSnapshot = CreatePortableSnapshot(cancellationToken);
            await WriteSnapshotAsync(fullOutputPath, portableSnapshot, cancellationToken).ConfigureAwait(false);
            return;
        }

        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"learn-dotnet-csharp-layers-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        var childSnapshotPath = Path.Combine(temporaryDirectory, "portable.json");
        var jitOutputPath = Path.Combine(temporaryDirectory, "jit.asm");

        try
        {
            using var process = new Process
            {
                StartInfo = CreateChildStartInfo(childSnapshotPath, jitOutputPath),
            };
            if (!process.Start())
            {
                throw new InvalidOperationException("无法启动 JIT 制品捕获子进程。");
            }

            var standardOutputTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var standardErrorTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
            using var captureCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            captureCancellation.CancelAfter(TimeSpan.FromSeconds(90));
            try
            {
                await process.WaitForExitAsync(captureCancellation.Token).ConfigureAwait(false);
            }
            catch
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                }

                throw;
            }
            var standardOutput = await standardOutputTask.ConfigureAwait(false);
            var standardError = await standardErrorTask.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"JIT 制品捕获失败（exit={process.ExitCode}）：{standardError.Trim()} {standardOutput.Trim()}");
            }

            var snapshot = JsonSerializer.Deserialize<LayerArtifactSnapshot>(
                    await File.ReadAllTextAsync(childSnapshotPath, cancellationToken).ConfigureAwait(false),
                    JsonOptions)
                ?? throw new InvalidDataException("子进程没有生成有效的四层代码制品。");
            var assembly = NormalizeJitListing(
                await File.ReadAllTextAsync(jitOutputPath, cancellationToken).ConfigureAwait(false));
            if (!assembly.Contains("Assembly listing for method DynamicSample.LayeredSample:HighLevel", StringComparison.Ordinal) ||
                !assembly.Contains("CORINFO_HELP_OVERFLOW", StringComparison.Ordinal))
            {
                throw new InvalidDataException("JIT 输出没有包含目标 HighLevel 方法或 checked overflow 证据。");
            }

            var completedSnapshot = snapshot with
            {
                Environment = snapshot.Environment with
                {
                    CaptureMode = "FullOpts · tiering disabled · observed snapshot",
                },
                Stages = snapshot.Stages with { Assembly = assembly },
            };
            ValidateFocusMappings(completedSnapshot);
            await WriteSnapshotAsync(fullOutputPath, completedSnapshot, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private static LayerArtifactSnapshot CreatePortableSnapshot(CancellationToken cancellationToken)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14);
        var syntaxTree = CSharpSyntaxTree.ParseText(
            CompilerAndIlDemo.SampleSource,
            parseOptions,
            path: "LayeredSample.cs",
            cancellationToken: cancellationToken);
        var root = syntaxTree.GetCompilationUnitRoot(cancellationToken);
        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>().ToArray();
        var highLevelSyntax = methods.Single(method => method.Identifier.ValueText == "HighLevel");
        var lowLevelSyntax = methods.Single(method => method.Identifier.ValueText == "LowLevelEquivalent");
        var compilation = CSharpCompilation.Create(
            "DynamicSample",
            [syntaxTree],
            GetRuntimeReferences(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                deterministic: true));

        using var peStream = new MemoryStream();
        var emitResult = compilation.Emit(peStream, cancellationToken: cancellationToken);
        if (!emitResult.Success)
        {
            var errors = emitResult.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            throw new InvalidOperationException($"Roslyn 发射失败：{string.Join(" | ", errors)}");
        }

        var peBytes = peStream.ToArray();
        var methodEvidence = ReadMethodEvidence(peBytes, "HighLevel");
        var loadContext = new AssemblyLoadContext("LayerArtifactExport", isCollectible: true);
        try
        {
            using var assemblyStream = new MemoryStream(peBytes, writable: false);
            var assembly = loadContext.LoadFromStream(assemblyStream);
            var sampleType = assembly.GetType("DynamicSample.LayeredSample", throwOnError: true)
                ?? throw new TypeLoadException("DynamicSample.LayeredSample");
            var highLevelMethod = sampleType.GetMethod("HighLevel", BindingFlags.Public | BindingFlags.Static)
                ?? throw new MissingMethodException(sampleType.FullName, "HighLevel");
            var lowLevelMethod = sampleType.GetMethod("LowLevelEquivalent", BindingFlags.Public | BindingFlags.Static)
                ?? throw new MissingMethodException(sampleType.FullName, "LowLevelEquivalent");
            var disposeCountProperty = sampleType.GetProperty("DisposeCount", BindingFlags.Public | BindingFlags.Static)
                ?? throw new MissingMemberException(sampleType.FullName, "DisposeCount");
            var values = new[] { -2, 1, 3, 11 };
            var highLevelResult = highLevelMethod.Invoke(null, [values]);
            var lowLevelResult = lowLevelMethod.Invoke(null, [values]);
            var normalDisposeCount = disposeCountProperty.GetValue(null);
            if (!Equals(highLevelResult, 4) || !Equals(lowLevelResult, 4) || !Equals(normalDisposeCount, 2))
            {
                throw new InvalidOperationException("高级写法与教学低层写法的正常路径或释放语义不等价。");
            }

            var highLevelFailure = InvokeAndCaptureInnerException(highLevelMethod, [null]);
            var lowLevelFailure = InvokeAndCaptureInnerException(lowLevelMethod, [null]);
            var exceptionalDisposeCount = disposeCountProperty.GetValue(null);
            if (highLevelFailure is not NullReferenceException ||
                lowLevelFailure is not NullReferenceException ||
                !Equals(exceptionalDisposeCount, 4))
            {
                throw new InvalidOperationException("高级写法与教学低层写法的异常路径或 finally 释放语义不等价。");
            }

            var highLevelIl = string.Join('\n', IlDisassembler.Disassemble(highLevelMethod, 128));
            var lowLevelIl = string.Join('\n', IlDisassembler.Disassemble(lowLevelMethod, 128));
            if (!highLevelIl.Contains("add.ovf", StringComparison.Ordinal) ||
                !highLevelIl.Contains("endfinally", StringComparison.Ordinal) ||
                !lowLevelIl.Contains("add.ovf", StringComparison.Ordinal) ||
                !lowLevelIl.Contains("endfinally", StringComparison.Ordinal))
            {
                throw new InvalidDataException("高级写法或教学低层写法的 CIL 缺少 checked/finally 证据。");
            }
            ValidateStackInstructions(highLevelIl, CreateStackSteps());

            return new LayerArtifactSnapshot(
                1,
                Sha256(CompilerAndIlDemo.SampleSource.ReplaceLineEndings("\n")),
                Convert.ToHexStringLower(SHA256.HashData(peBytes)),
                new LayerArtifactEnvironment(
                    RuntimeInformation.FrameworkDescription,
                    RuntimeInformation.OSDescription,
                    RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
                    "portable snapshot; JIT capture pending"),
                new LayerArtifactMethod(
                    "DynamicSample.LayeredSample.HighLevel(int[])",
                    $"0x{methodEvidence.MetadataToken:X8}",
                    $"0x{methodEvidence.RelativeVirtualAddress:X8}",
                    methodEvidence.MaxStack,
                    methodEvidence.IlByteCount,
                    methodEvidence.ExceptionRegionCount),
                new LayerArtifactStages(
                    FormatSourceMethod(highLevelSyntax),
                    FormatSourceMethod(lowLevelSyntax),
                    highLevelIl,
                    string.Empty),
                CreateFocuses(),
                CreateStackSteps(),
                lowLevelIl);
        }
        finally
        {
            loadContext.Unload();
        }
    }

    private static ProcessStartInfo CreateChildStartInfo(string snapshotPath, string jitOutputPath)
    {
        var processPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("当前进程没有可复用的可执行入口。");
        var startInfo = new ProcessStartInfo
        {
            FileName = processPath,
            WorkingDirectory = Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var entryAssemblyPath = Assembly.GetEntryAssembly()?.Location;
            if (string.IsNullOrWhiteSpace(entryAssemblyPath))
            {
                throw new InvalidOperationException("当前托管入口程序集路径不可用。");
            }

            startInfo.ArgumentList.Add(entryAssemblyPath);
        }

        startInfo.ArgumentList.Add("export-code-layers-child");
        startInfo.ArgumentList.Add(snapshotPath);
        startInfo.Environment["DOTNET_JitDisasm"] = "DynamicSample.LayeredSample:HighLevel";
        startInfo.Environment["DOTNET_JitDisasmDiffable"] = "1";
        startInfo.Environment["DOTNET_JitDisasmOnlyOptimized"] = "1";
        startInfo.Environment["DOTNET_JitStdOutFile"] = jitOutputPath;
        startInfo.Environment["DOTNET_TieredCompilation"] = "0";
        startInfo.Environment["DOTNET_ReadyToRun"] = "0";
        return startInfo;
    }

    private static PortableExecutableReference[] GetRuntimeReferences()
    {
        var trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrWhiteSpace(trustedAssemblies))
        {
            throw new PlatformNotSupportedException("当前运行时未公开可信平台程序集列表。");
        }

        return trustedAssemblies
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToArray();
    }

    private static LayerMethodEvidence ReadMethodEvidence(byte[] peBytes, string methodName)
    {
        using var stream = new MemoryStream(peBytes, writable: false);
        using var peReader = new PEReader(stream);
        var metadataReader = peReader.GetMetadataReader();
        foreach (var typeHandle in metadataReader.TypeDefinitions)
        {
            var typeDefinition = metadataReader.GetTypeDefinition(typeHandle);
            if (metadataReader.GetString(typeDefinition.Namespace) != "DynamicSample" ||
                metadataReader.GetString(typeDefinition.Name) != "LayeredSample")
            {
                continue;
            }

            foreach (var methodHandle in typeDefinition.GetMethods())
            {
                var methodDefinition = metadataReader.GetMethodDefinition(methodHandle);
                if (metadataReader.GetString(methodDefinition.Name) != methodName)
                {
                    continue;
                }

                var body = peReader.GetMethodBody(methodDefinition.RelativeVirtualAddress);
                return new LayerMethodEvidence(
                    MetadataTokens.GetToken(methodHandle),
                    methodDefinition.RelativeVirtualAddress,
                    body.MaxStack,
                    body.GetILBytes()?.Length ?? 0,
                    body.ExceptionRegions.Length);
            }
        }

        throw new MissingMethodException("DynamicSample.LayeredSample", methodName);
    }

    private static string NormalizeJitListing(string value) => value
        .ReplaceLineEndings("\n")
        .Trim();

    private static Exception InvokeAndCaptureInnerException(MethodInfo method, object?[] arguments)
    {
        try
        {
            method.Invoke(null, arguments);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            return exception.InnerException;
        }

        throw new InvalidOperationException($"{method.Name} 没有按预期抛出异常。");
    }

    private static void ValidateStackInstructions(string cil, IReadOnlyList<LayerStackStep> steps)
    {
        var normalizedCil = string.Join(' ', cil.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var position = 0;
        foreach (var step in steps)
        {
            var instruction = string.Join(' ', step.Instruction.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            position = normalizedCil.IndexOf(instruction, position, StringComparison.Ordinal);
            if (position < 0)
            {
                throw new InvalidDataException($"CIL 求值栈步骤 '{step.Instruction}' 与实际方法体不一致。");
            }

            position += instruction.Length;
        }
    }

    private static void ValidateFocusMappings(LayerArtifactSnapshot snapshot)
    {
        var stages = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["highLevel"] = snapshot.Stages.HighLevel,
            ["lowLevel"] = snapshot.Stages.LowLevel,
            ["cil"] = snapshot.Stages.Cil,
            ["assembly"] = snapshot.Stages.Assembly,
        };
        foreach (var focus in snapshot.Focuses)
        {
            foreach (var (stage, code) in stages)
            {
                if (!focus.Patterns.TryGetValue(stage, out var patterns) ||
                    !patterns.Any(pattern => code.Contains(pattern, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidDataException($"语义切片 '{focus.Id}' 在 {stage} 层没有可验证的匹配证据。");
                }
            }
        }
    }

    private static string FormatSourceMethod(MethodDeclarationSyntax method)
    {
        var rawLines = method.ToFullString()
            .ReplaceLineEndings("\n")
            .Split('\n');
        var firstContent = Array.FindIndex(rawLines, line => !string.IsNullOrWhiteSpace(line));
        var lastContent = Array.FindLastIndex(rawLines, line => !string.IsNullOrWhiteSpace(line));
        var lines = firstContent < 0 ? [] : rawLines[firstContent..(lastContent + 1)];
        var indentation = lines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.TakeWhile(char.IsWhiteSpace).Count())
            .DefaultIfEmpty(0)
            .Min();
        return string.Join('\n', lines.Select(line => line.Length >= indentation ? line[indentation..] : line));
    }

    private static string Sha256(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static async Task WriteSnapshotAsync(
        string outputPath,
        LayerArtifactSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)
            ?? throw new InvalidOperationException("输出路径没有父目录。"));
        await File.WriteAllTextAsync(
                outputPath,
                $"{JsonSerializer.Serialize(snapshot, JsonOptions)}{Environment.NewLine}",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static LayerFocus[] CreateFocuses() =>
    [
        new(
            "checked",
            "checked 溢出",
            "可观察语义是“溢出必须抛异常”：C# 的 checked 变成 CIL add.ovf，目标 ISA 用条件分支进入 overflow helper。",
            new Dictionary<string, string[]>
            {
                ["highLevel"] = ["checked"], ["lowLevel"] = ["checked"],
                ["cil"] = ["add.ovf"], ["assembly"] = ["add ", "jo ", "overflow"],
            }),
        new(
            "loop",
            "foreach 循环",
            "数组 foreach 可降为索引循环；CIL 显示 ldelem/ldlen/回跳，JIT 再映射成地址与计数器操作。",
            new Dictionary<string, string[]>
            {
                ["highLevel"] = ["foreach"], ["lowLevel"] = ["while", "values[index]", "index++"],
                ["cil"] = ["ldelem", "ldlen", "blt.s"], ["assembly"] = ["mov ", "add ", "dec ", "jne "],
            }),
        new(
            "dispose",
            "using / finally",
            "必须保持资源释放。低层模型显式写 finally，CIL 用异常区域与 endfinally，JIT 快照包含正常和异常释放路径。",
            new Dictionary<string, string[]>
            {
                ["highLevel"] = ["using var"], ["lowLevel"] = ["finally", "dispose"],
                ["cil"] = ["leave.s", "callvirt", "endfinally"], ["assembly"] = ["getdynamic", "inc ", "ig10", "ig11"],
            }),
    ];

    private static LayerStackStep[] CreateStackSteps() =>
    [
        new("ldloc.1", "[]", "[sum]", "读取局部变量 sum 并压入抽象求值栈。"),
        new("ldloc.s V_4", "[sum]", "[sum, value]", "再压入当前数组元素 value。"),
        new("add.ovf", "[sum, value]", "[newSum]", "消费两个操作数；有符号溢出时抛出 OverflowException。"),
        new("stloc.1", "[newSum]", "[]", "弹出结果并写回 sum，语句边界处栈再次为空。"),
    ];

    private sealed record LayerMethodEvidence(
        int MetadataToken,
        int RelativeVirtualAddress,
        int MaxStack,
        int IlByteCount,
        int ExceptionRegionCount);
}

internal sealed record LayerArtifactSnapshot(
    int SchemaVersion,
    string SourceSha256,
    string PeSha256,
    LayerArtifactEnvironment Environment,
    LayerArtifactMethod Method,
    LayerArtifactStages Stages,
    IReadOnlyList<LayerFocus> Focuses,
    IReadOnlyList<LayerStackStep> StackSteps,
    string LowLevelCil);

internal sealed record LayerArtifactEnvironment(string Runtime, string Os, string Architecture, string CaptureMode);
internal sealed record LayerArtifactMethod(
    string Signature,
    string MetadataToken,
    string RelativeVirtualAddress,
    int MaxStack,
    int IlByteCount,
    int ExceptionRegionCount);
internal sealed record LayerArtifactStages(string HighLevel, string LowLevel, string Cil, string Assembly);
internal sealed record LayerFocus(
    string Id,
    string Label,
    string Explanation,
    IReadOnlyDictionary<string, string[]> Patterns);
internal sealed record LayerStackStep(string Instruction, string Before, string After, string Explanation);
