using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using LearnDotnetCSharp.Infrastructure;
using LearnDotnetCSharp.PluginContract;

namespace LearnDotnetCSharp.Demos.Runtime;

public sealed class CollectiblePluginDemo : IDemo
{
    private const string PluginAssemblyName = "LearnDotnetCSharp.SamplePlugin.dll";

    public DemoMetadata Metadata { get; } = new(
        "runtime.collectible-plugin",
        "runtime",
        "可回收 AssemblyLoadContext 插件与卫星资源",
        "用共享契约、AssemblyDependencyResolver 和可回收加载上下文隔离插件，读取日语卫星资源并验证协作式卸载。",
        [18],
        ["AssemblyLoadContext", "AssemblyDependencyResolver", "plugin contract", "satellite resources", "collectible unload"]);

    public ValueTask RunAsync(DemoContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var pluginPath = Path.Combine(AppContext.BaseDirectory, "plugins", "sample", PluginAssemblyName);
        DemoAssert.True(File.Exists(pluginPath), "示例插件应由 MSBuild 复制到应用输出目录");

        var execution = ExecuteAndBeginUnload(pluginPath);
        WaitForUnload(execution.LoadContextReference, cancellationToken);

        context.WriteProperty("plugin", execution.Result.PluginId);
        context.WriteProperty("plugin output", execution.Result.Output);
        context.WriteProperty("satellite resource", execution.Result.Greeting);
        context.WriteProperty("plugin assembly version", execution.Result.AssemblyVersion);
        context.WriteProperty("collectible context", execution.WasCollectible);
        context.WriteProperty("unloaded", !execution.LoadContextReference.IsAlive);

        DemoAssert.Equal("sample.uppercase", execution.Result.PluginId, "插件应通过默认上下文中的共享契约返回结果");
        DemoAssert.Equal("ASSEMBLY LOAD CONTEXT", execution.Result.Output, "隔离插件应执行自己的实现");
        DemoAssert.Equal("プラグインからこんにちは", execution.Result.Greeting, "ResourceManager 应从 ja-JP 卫星程序集读取资源");
        DemoAssert.True(execution.WasCollectible && !execution.LoadContextReference.IsAlive, "插件加载上下文应可回收并完成卸载");

        return ValueTask.CompletedTask;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static PluginExecution ExecuteAndBeginUnload(string pluginPath)
    {
        var loadContext = new PluginLoadContext(pluginPath);
        var weakReference = new WeakReference(loadContext, trackResurrection: true);
        var assembly = loadContext.LoadFromAssemblyPath(pluginPath);
        var pluginType = assembly.GetTypes()
            .Single(type => typeof(ILearningPlugin).IsAssignableFrom(type) && !type.IsAbstract);
        var plugin = (ILearningPlugin?)Activator.CreateInstance(pluginType)
            ?? throw new InvalidOperationException("无法创建示例插件实例。");
        var wasCollectible = AssemblyLoadContext.GetLoadContext(pluginType.Assembly)?.IsCollectible == true;
        var result = plugin.Execute("assembly load context", "ja-JP");

        loadContext.Unload();
        return new PluginExecution(result, weakReference, wasCollectible);
    }

    private static void WaitForUnload(WeakReference loadContextReference, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 10 && loadContextReference.IsAlive; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }

    private sealed class PluginLoadContext(string pluginPath)
        : AssemblyLoadContext(name: "LearnDotnetCSharp.SamplePlugin", isCollectible: true)
    {
        private readonly AssemblyDependencyResolver resolver = new(pluginPath);
        private readonly string pluginDirectory = Path.GetDirectoryName(pluginPath)
            ?? throw new ArgumentException("插件路径缺少目录。", nameof(pluginPath));

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name == typeof(ILearningPlugin).Assembly.GetName().Name)
            {
                return null;
            }

            if (!string.IsNullOrEmpty(assemblyName.CultureName))
            {
                var satellitePath = Path.Combine(
                    pluginDirectory,
                    assemblyName.CultureName,
                    $"{assemblyName.Name}.dll");
                if (File.Exists(satellitePath))
                {
                    return LoadFromAssemblyPath(satellitePath);
                }
            }

            var path = resolver.ResolveAssemblyToPath(assemblyName);
            return path is null ? null : LoadFromAssemblyPath(path);
        }

        protected override nint LoadUnmanagedDll(string unmanagedDllName)
        {
            var path = resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return path is null ? nint.Zero : LoadUnmanagedDllFromPath(path);
        }
    }

    private sealed record PluginExecution(
        PluginResult Result,
        WeakReference LoadContextReference,
        bool WasCollectible);
}
