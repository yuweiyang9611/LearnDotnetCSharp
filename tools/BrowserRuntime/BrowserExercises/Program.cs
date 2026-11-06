using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using ExerciseEngine;

// The WebAssembly host: compiler-worker.js calls these three exports. A lab is several named files
// (the demo, the Infrastructure files, an entry), and its entry awaits RunAsync, so Execute is asynchronous.
[SupportedOSPlatform("browser")]
public static partial class BrowserCompiler
{
    private static readonly ExerciseCompiler Compiler = new();
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public static void Main() { }
    [JSExport] public static void AddReference(byte[] bytes) => Compiler.AddReference(bytes);
    [JSExport] public static string Compile(string filesJson, string checks) => JsonSerializer.Serialize(Compiler.Compile(filesJson, checks), Json);
    [JSExport] public static async Task<string> Execute() => JsonSerializer.Serialize(await Compiler.ExecuteAsync().ConfigureAwait(false), Json);
}
