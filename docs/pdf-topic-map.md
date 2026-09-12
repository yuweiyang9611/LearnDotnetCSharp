# PDF 目录与现代示例映射

本表依据所提供 PDF 的目录页整理。它用于确定学习范围，不复制书中正文。书的基准是 C# 8 / 早期 .NET Core；本项目把相同主题更新到 .NET 10 / C# 14，并以 53 个可运行实验完成验证。

| PDF 章节 | 原目录主题 | 本项目中的现代落点 | 覆盖策略 |
|---:|---|---|---|
| 1 | C# 和 .NET Core 简介 | `runtime` / `compiler` | CLR、BCL，以及 C# → CIL/元数据 → JIT/AOT → 目标机器码的完整层次 |
| 2 | C# 语言基础 | CLI 与所有示例 | 作为前置知识，不重复基础语法教程 |
| 3 | 在 C# 中创建类型 | `language` | record、值/引用语义、接口、型变、泛型约束、静态抽象成员、自定义值类型及其运算符契约 |
| 4 | C# 的高级特性 | `language` / `compiler` | C# 14、模式、委托/事件/迭代器、Attribute、Span、unsafe，以及 `using`/`foreach`/async 的 lowering 观察 |
| 5 | 框架概述 | `runtime` | .NET 10 运行时与程序集边界 |
| 6 | 框架基础 | `framework` | Unicode、全球化、格式化/解析、时间/时区、数值、枚举、Guid、比较器 |
| 7 | 集合 | `collections` / `concurrency` | 顺序/优先级/集合/字典选择、自定义集合契约与并发集合 |
| 8 | LINQ 查询 | `linq` | 查询语法、延迟执行、物化、子查询、表达式树，以及 EF Core/SQLite 的真实 provider 翻译 |
| 9 | LINQ 运算符 | `linq` / `concurrency` | 完整操作族、.NET 10 外连接/按键聚合、SQL 翻译，以及 PLINQ 顺序差异 |
| 10 | LINQ to XML | `xml` | 函数式构造、命名空间、查询、修改与 `XObject` 注解 |
| 11 | 其他 XML 与 JSON 技术 | `serialization` | 异步 XmlReader/XmlWriter、源生成 System.Text.Json 与严格输入 |
| 12 | 对象销毁与垃圾回收 | `memory` | SafeHandle、同步/异步 Dispose、SuppressFinalize、受控终结、代际 GC、固定对象、弱引用与池化 |
| 13 | 诊断 | `diagnostics` / `runtime` / `compiler` | TraceSource、ActivitySource、Meter、EventSource、Roslyn 诊断、PE/CIL 读取与 `DOTNET_JitDisasm` 本机代码观察 |
| 14 | 并发与异步 | `async` | Task、取消、超时、异步流、continuation、SynchronizationContext/ExecutionContext 与异步状态机 |
| 15 | 流与 I/O | `io` / `async` / `networking` | 异步文件流、压缩、管道、位置式 I/O、内存映射、HTTP 正文流与背压 |
| 16 | 网络 | `networking` | loopback DNS/TCP/UDP、WebSocket、SslStream/TLS、HTTP 流/重试/取消、HTTP/1.1、HTTP/2 与条件式 QUIC HTTP/3 |
| 17 | 序列化 | `serialization` / `interop` | XmlSerializer 显式形状、源生成 JSON、多态判别器、未知字段保留、版本化与跨语言边界 |
| 18 | 程序集 | `runtime` / `reflection` / `compiler` / `interop` | PE/CLI 封装、程序集清单、元数据表/heap、MethodDef token、AssemblyLoadContext 与可收集卸载 |
| 19 | 反射和元数据 | `language` / `reflection` / `compiler` | Attribute、泛型调用、Emit、PEReader、RVA/方法体头、CIL 指令和求值栈 |
| 20 | 动态编程 | `reflection` / `language` | 动态代码、表达式或运行时绑定 |
| 21 | 加密 | `crypto` | CSPRNG、SHA-256、HMAC、PBKDF2、AES-GCM、RSA-PSS、ECDSA、篡改拒绝与敏感内存清零 |
| 22 | 高级线程处理 | `threading` / `concurrency` | Lock、事件、线程池、Volatile/CAS、锁自由结构、ABA 与内存可见性 |
| 23 | 并行编程 | `concurrency` / `runtime` | Parallel、PLINQ、并发集合、异常传播、SIMD 数据并行与 PGO 观察边界 |
| 24 | `Span<T>` 和 `Memory<T>` | `language` / `memory` | 栈上视图、池化、C# 14 Span 转换、`allows ref struct` 与原生内存视图 |
| 25 | 原生程序和 COM 组件互操作性 | `interop` / `memory` | 系统 P/Invoke、C ABI/反向回调、C++ 不透明句柄、固定/对齐原生内存，以及 Python/F#/VB 交互 |
| 26 | 正则表达式 | `regex` | 源生成、超时、非回溯、捕获与替换 |
| 27 | Roslyn 编译器 | `compiler` | SyntaxTree、绑定、内部 lowering、生成器/分析器、同一 PE 的元数据/CIL 核对与 async 状态机 |

## 第 4–11 章直接运行入口

| 章节 | 实验 ID |
|---:|---|
| 4 | `language.csharp14`、`language.advanced`、`language.operators-conversions`、`language.generics-variance-constraints`、`language.attributes`、`language.delegates-events-iterators` |
| 5 | `runtime.overview`、`runtime.jit-simd-pgo` |
| 6 | `framework.text-globalization`、`framework.time-numerics-identity` |
| 7 | `collections.core-custom-buffer`、`concurrency.channel-pipeline` |
| 8 | `linq.query-semantics`、`linq.efcore-sqlite-provider` |
| 9 | `linq.operators`、`linq.efcore-sqlite-provider`、`concurrency.parallel-plinq` |
| 10 | `xml.linq` |
| 11 | `serialization.xml-json` |

每个入口都包含可执行断言，既能单独用 `run <id>` 观察，也会被 `self-test` 隔离验证。第 8–9 章先用 LINQ to Objects 区分语言/运算符语义，再用 EF Core 10 + 进程内 SQLite 展示真实 `IQueryable` provider 如何把受支持的表达式树翻译成参数化 SQL，以及何时应显式切换回客户端计算。

## 高级实验入口

| 实验 ID | 对应 PDF 章节 | 现代补充主题 |
|---|---|---|
| `language.generics-variance-constraints` | 3、4、24 | 型变、约束、静态抽象接口成员、`allows ref struct` |
| `async.continuations-context` | 14、22 | continuation、同步上下文、执行上下文与 sync-over-async |
| `concurrency.memory-model-lock-free` | 14、22、23 | safe publication、CAS、Treiber stack、ABA 与伪共享 |
| `networking.http-streaming-resilience` | 15、16 | 响应头完成边界、流式 JSON、幂等重试与取消传播 |
| `memory.pinning-native-memory` | 12、24、25 | 固定、端序视图、对齐原生内存与 SafeHandle 所有权 |
| `compiler.roslyn-il` | 1、4、13、18、19、27 | 高级/低层等价 C#、MethodDef/RVA、CIL 求值栈、async 状态机与 JIT 汇编观察入口 |
| `compiler.incremental-generator-analyzer` | 13、19、27 | 增量源生成、语义分析与诊断报告 |
| `runtime.jit-simd-pgo` | 1、5、13、23 | 可移植 SIMD、硬件内建函数、分层编译与动态 PGO 边界 |

## `projects` 分类综合项目入口

这些入口不是新的孤立知识点，而是学习路线的收尾阶段：每个项目把多个 PDF 章节和前置实验组合到一条端到端流程中。可复用机制位于独立的 `LearnDotnetCSharp.Capstones` Core 项目，App 只负责 CLI、真实平台适配和教学断言；45 个正式测试可直接验证恢复状态，而 53 项 `self-test` 继续验证完整边界。

| 实验 ID | 对应 PDF 章节 | 综合应用 |
|---|---|---|
| `project.cancellable-data-pipeline` | 8、11、13、14、15、22、26 | UTF-8 字节 checkpoint、原子状态、幂等死信、有界 Channel、取消与中断后 resume |
| `project.versioned-local-service` | 14、15、16、17、21 | loopback HTTP、HMAC、SQLite 幂等、提交后 503、重启重放与 NDJSON 流式响应 |
| `project.collectible-plugin-host` | 4、8、13、17、18、19、21、26 | v1/v2 Attribute 元数据、能力路由、热切换、隔离回退与可收集加载上下文 |
| `project.polyglot-compute` | 14、17、22、25 | Python 常驻池、崩溃重建、C ABI、C++ 不透明句柄、取消与跨语言不变量 |
| `project.resilient-analytics-workflow` | 8、11、13、14、15、17、18、21、22、25、26 | 管线恢复、Python 自愈、插件路由和 SQLite 重开重放的跨平台端到端组合 |

## 与原书基准的关键差异

- `.NET Core` 已统一为现代 `.NET`；目标框架为 `net10.0`。
- C# 14 增加 extension members、`field`、null-conditional assignment、unbound generic `nameof`、更多 Span 转换、partial constructor/event 等语言能力。
- 异步示例以取消、超时、异步流和结构化生命周期为中心，不使用已过时的 EAP/APM 模式。
- 网络示例使用 `HttpClient`、Kestrel、DNS、TCP/UDP、WebSocket 与 `SslStream`，HTTP/2/3 以精确版本策略验证；真实网络端点全部在进程内 loopback，流式客户端完成边界使用可控分段 `HttpContent` 验证。
- HTTP/3 建立在 QUIC 与 TLS 1.3 上；平台缺少 QUIC/MsQuic 时明确跳过，不把 HTTP/2 回退误报为 HTTP/3 成功。
- 序列化示例使用显式 XML/JSON 契约、白名单多态与未知字段保留；已从现代 .NET 移除的 `BinaryFormatter` 只作为迁移警示，不恢复成可运行示例。
- 加密实验只组合 BCL 已实现的原语，并演示随机数、认证加密、签名、定时安全比较和密钥清零；它不是自创算法或生产密钥管理系统。
- COM 是平台专有技术；项目以 C ABI 演示核心 P/Invoke 和反向回调，以 `extern "C"` + `SafeHandle` 隔离 C++ ABI，并单独标注平台构建行为。
- Python 示例选择工作区 venv 子进程和版本化 JSON Lines 协议，避免把学习项目绑定到 CPython 内部 ABI 或第三方嵌入包；综合项目进一步演示有界常驻池、请求关联、stderr drain、进程树终止与崩溃后替换。
- 综合项目的可恢复机制从 CLI 中抽到 `LearnDotnetCSharp.Capstones`：checkpoint/DLQ、SQLite single-flight、插件能力路由和 Python worker 生命周期可以通过正式测试独立验证，再由第五个项目证明这些边界能够共同工作。
