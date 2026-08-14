# Microsoft 官方延伸资料

本项目的源码是机制入口；下面的官方文档用于核对正式语义、平台边界和生产建议。

## .NET 10 与 C# 14

- [.NET 10 新特性概览](https://learn.microsoft.com/dotnet/core/whats-new/dotnet-10/overview)
- [C# 14 新特性](https://learn.microsoft.com/dotnet/csharp/whats-new/csharp-14)
- [C# 14 extension members 教程](https://learn.microsoft.com/dotnet/csharp/whats-new/tutorials/extension-members)
- [运算符重载](https://learn.microsoft.com/dotnet/csharp/language-reference/operators/operator-overloading)
- [用户定义显式与隐式转换](https://learn.microsoft.com/dotnet/csharp/language-reference/operators/user-defined-conversion-operators)
- [泛型类型参数约束](https://learn.microsoft.com/dotnet/csharp/programming-guide/generics/constraints-on-type-parameters)
- [泛型接口中的协变与逆变](https://learn.microsoft.com/dotnet/csharp/programming-guide/concepts/covariance-contravariance/variance-in-generic-interfaces)
- [静态抽象/虚接口成员教程](https://learn.microsoft.com/dotnet/csharp/advanced-topics/interface-implementation/static-virtual-interface-members)
- [使用 Attribute 创建声明性信息](https://learn.microsoft.com/dotnet/csharp/advanced-topics/reflection-and-attributes/creating-custom-attributes)

## 框架基础、集合、LINQ、XML 与 JSON

- [字符串与文本的最佳实践](https://learn.microsoft.com/dotnet/standard/base-types/best-practices-strings)
- [时区概述](https://learn.microsoft.com/dotnet/standard/datetime/time-zone-overview)
- [集合和数据结构](https://learn.microsoft.com/dotnet/standard/collections/)
- [LINQ 标准查询运算符](https://learn.microsoft.com/dotnet/csharp/linq/standard-query-operators/)
- [.NET 10 `LeftJoin` 与 `RightJoin`](https://learn.microsoft.com/dotnet/csharp/linq/standard-query-operators/join-operations)
- [EF Core 入门与 SQLite](https://learn.microsoft.com/ef/core/get-started/overview/first-app)
- [EF Core 10 新特性](https://learn.microsoft.com/ef/core/what-is-new/ef-core-10.0/whatsnew)
- [EF Core SQLite provider](https://learn.microsoft.com/ef/core/providers/sqlite/)
- [LINQ to XML 概述](https://learn.microsoft.com/dotnet/standard/linq/linq-xml-overview)
- [XmlReader/XmlWriter](https://learn.microsoft.com/dotnet/standard/data/xml/)
- [`System.Text.Json` 源生成](https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/source-generation)
- [`System.Text.Json` 多态序列化](https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/polymorphism)
- [自定义 JSON 契约](https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/custom-contracts)
- [XmlSerializer 的显式 XML 形状](https://learn.microsoft.com/dotnet/standard/serialization/introducing-xml-serialization)
- [`BinaryFormatter` 从 .NET 9 起的移除说明](https://learn.microsoft.com/dotnet/core/compatibility/serialization/9.0/binaryformatter-removal)

## 异步、并发和网络

- [使用异步流](https://learn.microsoft.com/dotnet/csharp/asynchronous-programming/generate-consume-asynchronous-stream)
- [`Task.WhenEach` API](https://learn.microsoft.com/dotnet/api/system.threading.tasks.task.wheneach?view=net-10.0)
- [`ExecutionContext` 与 `SynchronizationContext`](https://learn.microsoft.com/dotnet/standard/asynchronous-programming-patterns/executioncontext-synchronizationcontext)
- [`Volatile` 内存操作](https://learn.microsoft.com/dotnet/api/system.threading.volatile?view=net-10.0)
- [`Interlocked.CompareExchange`](https://learn.microsoft.com/dotnet/api/system.threading.interlocked.compareexchange?view=net-10.0)
- [.NET 线程安全集合与 lock-free 机制](https://learn.microsoft.com/dotnet/standard/collections/thread-safe/)
- [Channels 库](https://learn.microsoft.com/dotnet/core/extensions/channels)
- [.NET 网络概览](https://learn.microsoft.com/dotnet/fundamentals/networking/overview)
- [`UdpClient` API](https://learn.microsoft.com/dotnet/api/system.net.sockets.udpclient?view=net-10.0)
- [ASP.NET Core WebSocket](https://learn.microsoft.com/aspnet/core/fundamentals/websockets?view=aspnetcore-10.0)
- [`SslStream` API](https://learn.microsoft.com/dotnet/api/system.net.security.sslstream?view=net-10.0)
- [Kestrel 使用 HTTP/3](https://learn.microsoft.com/aspnet/core/fundamentals/servers/kestrel/http3?view=aspnetcore-10.0)
- [HttpClient 使用 HTTP/3](https://learn.microsoft.com/dotnet/core/extensions/httpclient-http3)
- [`HttpCompletionOption.ResponseHeadersRead`](https://learn.microsoft.com/dotnet/api/system.net.http.httpcompletionoption?view=net-10.0)
- [`System.IO.Pipelines`](https://learn.microsoft.com/dotnet/standard/io/pipelines)
- [`RandomAccess` API](https://learn.microsoft.com/dotnet/api/system.io.randomaccess?view=net-10.0)
- [内存映射文件](https://learn.microsoft.com/dotnet/standard/io/memory-mapped-files)

## 运行时、生命周期、诊断、程序集、反射与编译器

- [垃圾回收基础](https://learn.microsoft.com/dotnet/standard/garbage-collection/fundamentals)
- [`GCHandle` 与固定对象](https://learn.microsoft.com/dotnet/api/system.runtime.interopservices.gchandle?view=net-10.0)
- [GC 性能与过度固定](https://learn.microsoft.com/dotnet/standard/garbage-collection/performance)
- [`NativeMemory` 原生分配 API](https://learn.microsoft.com/dotnet/api/system.runtime.interopservices.nativememory?view=net-10.0)
- [实现 Dispose 方法](https://learn.microsoft.com/dotnet/standard/garbage-collection/implementing-dispose)
- [对象终结](https://learn.microsoft.com/dotnet/csharp/programming-guide/classes-and-structs/finalizers)
- [.NET 诊断概览](https://learn.microsoft.com/dotnet/core/diagnostics/)
- [使用 OpenTelemetry 的 .NET 可观测性](https://learn.microsoft.com/dotnet/core/diagnostics/observability-with-otel)
- [理解 AssemblyLoadContext](https://learn.microsoft.com/dotnet/core/dependency-loading/understanding-assemblyloadcontext)
- [可收集程序集的卸载与调试](https://learn.microsoft.com/dotnet/standard/assembly/unloadability)
- [.NET 反射](https://learn.microsoft.com/dotnet/fundamentals/reflection/reflection)
- [`System.Reflection.Emit.OpCodes`](https://learn.microsoft.com/dotnet/api/system.reflection.emit.opcodes?view=net-10.0)
- [Roslyn SDK 概览](https://learn.microsoft.com/dotnet/csharp/roslyn-sdk/)
- [`IIncrementalGenerator` API](https://learn.microsoft.com/dotnet/api/microsoft.codeanalysis.iincrementalgenerator)
- [编写 Roslyn 分析器与代码修复](https://learn.microsoft.com/dotnet/csharp/roslyn-sdk/tutorials/how-to-write-csharp-analyzer-code-fix)
- [.NET SIMD 加速类型](https://learn.microsoft.com/dotnet/standard/simd)
- [分层编译与动态 PGO 运行时配置](https://learn.microsoft.com/dotnet/core/runtime-config/compilation)

## 密码学、正则与互操作

- [.NET 密码学模型](https://learn.microsoft.com/dotnet/standard/security/cryptography-model)
- [`AesGcm` API](https://learn.microsoft.com/dotnet/api/system.security.cryptography.aesgcm?view=net-10.0)
- [`Rfc2898DeriveBytes.Pbkdf2` API](https://learn.microsoft.com/dotnet/api/system.security.cryptography.rfc2898derivebytes.pbkdf2?view=net-10.0)
- [`CryptographicOperations.FixedTimeEquals` API](https://learn.microsoft.com/dotnet/api/system.security.cryptography.cryptographicoperations.fixedtimeequals?view=net-10.0)
- [正则表达式源生成器](https://learn.microsoft.com/dotnet/standard/base-types/regular-expression-source-generators)
- [正则表达式最佳实践](https://learn.microsoft.com/dotnet/standard/base-types/best-practices-regex)
- [原生互操作最佳实践](https://learn.microsoft.com/dotnet/standard/native-interop/best-practices)
- [平台调用 P/Invoke](https://learn.microsoft.com/dotnet/standard/native-interop/pinvoke)
- [原生库加载规则](https://learn.microsoft.com/dotnet/standard/native-interop/native-library-loading)
- [`UnmanagedCallersOnlyAttribute`](https://learn.microsoft.com/dotnet/api/system.runtime.interopservices.unmanagedcallersonlyattribute?view=net-10.0)
- [`SafeHandle`](https://learn.microsoft.com/dotnet/api/system.runtime.interopservices.safehandle?view=net-10.0)
- [`ProcessStartInfo.ArgumentList`](https://learn.microsoft.com/dotnet/api/system.diagnostics.processstartinfo.argumentlist?view=net-10.0)
- [Python `venv`](https://docs.python.org/3/library/venv.html)
