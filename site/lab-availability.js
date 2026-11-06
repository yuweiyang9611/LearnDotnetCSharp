// The labs that cannot run in the browser, with the reason the panel shows next to 需要本机.
// The list is the browser's verdict: scripts/survey-browser-labs.mjs runs all 53 labs on the local build
// (2026-11-06: 23 finished, these 30 did not); a lab absent here gets a 运行 button. Reasons are in the
// reader's words, grouped by cause.
const sockets = "浏览器里的 .NET 没有 Socket 和回环网络，HttpClient 也只能走浏览器的 fetch。";
const kestrel = "需要 Kestrel（Microsoft.AspNetCore）在本机监听回环端口，浏览器里没有服务器。";
const threads = "需要真正的线程和阻塞等待，浏览器里的 .NET 运行在单线程上，Thread.Start 和 Wait 会抛 PlatformNotSupportedException。";
const generator = "源码用了源生成器（LibraryImport / GeneratedRegex / JSON 源生成），浏览器里的编译器不运行生成器。";
const native = "要加载仓库编译出的 C/C++ 动态库，浏览器里没有原生代码可调用。";
const projects = "引用 Capstones、PluginContract 等其他项目，并用到 SQLite、文件、进程或 Kestrel，这些都在浏览器之外。";

export const localOnly = Object.freeze({
  "runtime.collectible-plugin": "要从磁盘加载插件 DLL 和卫星资源到可收集的 AssemblyLoadContext，浏览器里没有文件系统和 PluginContract 程序集。",
  "framework.text-globalization": "浏览器里的 .NET 只带精简的 ICU 数据，德语区域性的数字解析结果与本机不同，实验的不变量会失败。",
  "linq.efcore-sqlite-provider": "需要 EF Core 和进程内 SQLite 原生库，浏览器里没有这两样。",
  "serialization.xml-json": generator,
  "serialization.contract-versioning": generator,
  "async.continuations-context": threads,
  "threading.execution-context": threads,
  "threading.synchronization-primitives": threads,
  "networking.tcp-loopback": sockets,
  "networking.httpclient-loopback": sockets,
  "networking.http2-http3": kestrel,
  "networking.http-streaming-resilience": kestrel,
  "networking.udp-websocket-tls": kestrel,
  "memory.gc": "浏览器里的 .NET 用的是另一套 GC，没有本机 CoreCLR 的分代晋升和收集计数，实验的不变量会失败。",
  "memory.disposal-finalization": "浏览器里的 .NET 没有终结器线程，未释放对象不会按实验预期被终结一次。",
  "diagnostics.observability": "TraceSource 监听器和 EventSource 在浏览器里的 .NET 中不写入，实验的不变量会失败。",
  "compiler.roslyn-il": "需要 Microsoft.CodeAnalysis 包和当前运行环境的实现程序集，浏览器里的编译器不把 Roslyn 暴露给实验代码。",
  "compiler.incremental-generator-analyzer": "需要 Microsoft.CodeAnalysis 包来运行生成器与分析器，浏览器里的编译器不把 Roslyn 暴露给实验代码。",
  "crypto.modern-primitives": "AES-GCM 等原语在浏览器里的 .NET 中不受支持（Cryptography_AlgorithmNotSupported）。",
  "regex.advanced": generator,
  "interop.managed-languages": "要引用 F# 和 Visual Basic 项目编出的程序集，浏览器里只有 C# 编译器。",
  "interop.native-pinvoke": "LibraryImport 平台调用需要源生成器和本机操作系统 API。",
  "interop.python-process-json": "要启动工作区 .venv 里的 Python 进程并通过标准输入输出交换 JSON，浏览器里没有进程。",
  "interop.c-abi": native,
  "interop.cpp-opaque-handle": native,
  "project.cancellable-data-pipeline": projects,
  "project.versioned-local-service": projects,
  "project.collectible-plugin-host": projects,
  "project.polyglot-compute": projects,
  "project.resilient-analytics-workflow": projects,
});
