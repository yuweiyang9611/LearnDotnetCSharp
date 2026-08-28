# 推荐学习路线

本文是 53 个可运行实验的快速路线图；需要逐实验的学习目标、机制解析、修改练习、综合项目和结业验收时，请使用 [《.NET 10 与 C# 14 高级特性学习指导》](advanced-dotnet-csharp-study-guide.md)。该文档同时是可转换为 PDF 的内容源。

## 1. 建立运行时直觉

先运行 `runtime.overview`，确认 CLR 版本、进程架构、GC 模式和动态代码能力；再运行 `runtime.jit-simd-pgo`，把可移植向量、硬件能力分支、分层编译覆盖项和真实性能测量边界分开。可收集插件放到第 6 节再看。

观察点：

- 语言版本是编译期概念，`.NET` 版本是目标框架和运行时概念。
- 相同 IL 在不同 JIT、CPU 和 GC 模式下可能有不同机器码与性能。
- SIMD 能力标志和预热次数只是观察数据，不是性能提升证明；基准必须使用 Release、固定环境和专业工具。
- 详细学习指导第 19–20 章把这条链拆成“高级 C# → Roslyn lowering → PE/元数据 + CIL → JIT/AOT → x64/Arm64 机器码”。

## 2. 现代 C# 语言

运行 `language` 分类。先看 C# 14 的增量语法，再看高级类型系统、模式、运算符/转换、泛型约束与型变、`Span<T>`、受控指针和动态绑定。

观察点：

- 哪些能力只是语法糖，哪些需要新的运行时/BCL 支持。
- `ref struct`、Span 和普通堆对象在逃逸规则上的差异。
- 静态抽象接口成员如何把运算符纳入泛型约束。
- `out`/`in` 型变解决的是引用类型安全替换；`notnull`、`unmanaged`、`new()` 和 `allows ref struct` 分别声明不同能力或生命周期责任。
- 运算符重载如何维护值语义；隐式转换为何只适合无损、不抛异常的映射，`checked` 如何选择另一组用户定义运算符。
- C# 的封闭 record 层次只能模拟判别联合，并不提供原生穷尽性检查。
- `unsafe` 和 `dynamic` 分别放弃部分内存安全或静态检查，应限制在清晰的边界内。

## 3. 框架基础、集合与 LINQ

建议顺序：`framework` -> `collections` -> `linq` -> `xml` -> `serialization`。这条路径直接对应 PDF 第 5–11 章。

观察点：

- `string.Length`、Unicode 标量值和用户感知文本元素是三个不同层次；编码又把它们映射为字节。
- 文本、数字、时间和排序都要显式决定区域性、时区或比较器，避免依赖当前机器默认值。
- 集合选择首先由顺序、唯一性、优先级和查找语义决定；自定义集合要兑现索引、枚举和边界契约。
- `IEnumerable<T>` 执行委托，`IQueryable<T>` 保存表达式树；外部 provider 只翻译自己支持的表达式。
- `linq.efcore-sqlite-provider` 会同时打印表达式树形状和 SQLite SQL；`.NET 10 LeftJoin` 在这里由 EF Core 10 真正翻译，而不再只是 LINQ to Objects。
- `ToArrayAsync` 是服务器查询的执行/物化边界；`AsEnumerable` 是有意识地切换到客户端委托，不应用来掩盖意外的不可翻译表达式。
- LINQ 查询通常延迟执行；需要稳定快照或避免重复副作用时，明确物化为数组/列表。
- LINQ to XML 适合树形查询与修改；`XmlReader`/`XmlWriter` 适合大型或单遍流式数据；JSON 源生成减少运行时反射。
- 持久化契约要显式决定名称、命名空间、多态白名单和未知字段策略；类型本身能序列化并不等于协议可以安全演进。

## 4. 异步、线程与并发

建议顺序：`async` -> `threading` -> `concurrency`。

观察点：

- `async` 不等于新建线程；I/O 等待期间没有线程被占住。
- `SynchronizationContext` 决定 continuation 去哪里，`ExecutionContext` 决定 ambient 状态带什么；`ConfigureAwait(false)` 不等于抑制 `AsyncLocal<T>`。
- 取消是协作协议，必须在拥有资源和循环的边界上传递。
- 原子操作、互斥、信号和消息传递解决的是不同问题；CAS 循环还要处理竞争重试、ABA 和节点生命周期。
- 并行加速需要足够大的 CPU 工作量，并且要考虑顺序与异常聚合。

## 5. I/O 与本地网络

先运行 `io`，再运行 `networking`。文件实验使用独立临时目录；真实网络端点都在当前进程和 loopback 上，流式客户端选项另用可控分段 `HttpContent` 消除小包缓冲的偶然性，所有实验均不访问公网。

观察点：

- TCP 是字节流，单次读取不等于一条完整消息。
- UDP 保留数据报边界但不保证送达、顺序或去重；示例的回显成功不能被外推成公网可靠性。
- WebSocket 是消息/帧协议；`SslStream` 是经身份验证的加密字节流。示例分别检查文本帧、证书指纹、TLS 版本和 ALPN。
- `System.IO.Pipelines` 的重点是缓冲区所有权和 `AdvanceTo`，不是把所有数据复制成单一数组。
- `RandomAccess` 没有共享流位置，适合并行处理固定偏移；内存映射适合随机共享视图，但必须管理刷新与生命周期。
- HTTP 客户端生命周期、超时和取消需要统一管理。
- `ResponseHeadersRead` 只把完成边界移动到响应头；正文仍需独立取消、增量读取和及时释放。重试必须考虑幂等性、请求体能否重放与总时间预算。
- HTTP/2 使用 TCP + TLS/ALPN，HTTP/3 使用 QUIC + TLS 1.3；应检查实际响应版本，而不是假设请求版本一定成功。
- 监听器、连接和流都必须在异常路径上释放。

## 6. 生命周期、诊断、程序集、反射与从源码到机器码

建议顺序：`memory` -> `diagnostics` -> `runtime.collectible-plugin` -> `reflection` -> `compiler`。运行 `compiler.roslyn-il` 后，按学习指导第 19 章逐层核对同一份 PE，再按第 20 章用 `DOTNET_JitDisasm` 启动一个新的 Release 进程观察机器汇编。

观察点：

- 分配量、存活时间和保留图比“手动调用 GC”更重要。
- 原生句柄优先包进 `SafeHandle`；拥有资源的类型再用 `IDisposable` / `IAsyncDisposable` 编排托管资源，通常不需要自己写终结器。
- 固定托管对象只解决短期地址稳定；原生分配必须使用匹配释放函数，并让对齐、端序、长度和所有权都进入契约。
- `ActivitySource` 传播因果上下文，`Meter` 发布指标，`EventSource` 提供低层事件；只有监听器存在时才应承担相应采集成本。
- 可卸载插件必须避免默认加载上下文、静态字段、线程或反射对象继续引用插件；`Unload()` 发起回收，真正卸载要等所有根消失。
- “低层 C#”是便于解释 `using`、`foreach`、模式和状态机的手写语义等价展开，不是 Roslyn 会输出的正式 `.cs`；Roslyn 实际使用内部 bound tree/rewrite。
- 反射看到元数据和加载后的运行时类型；CIL 展示编译器发射的托管控制流，JIT 后的最终控制流仍可能改变。
- `compiler.roslyn-il` 会从同一 PE 的 MethodDef token 与 RVA 找到方法体，并核对 PEReader 和反射读取到相同 CIL 字节；读取指令前先看 `.maxstack`、locals 和异常区域。
- CIL 求值栈是抽象执行模型，不要求机器码使用物理栈；load/运算/store 的值常被 JIT 放进寄存器。
- 增量生成器增加编译输入，分析器报告诊断；两者都应保持确定性、并发安全并控制误报，目标框架工具要使用 reference assemblies。
- async/iterator、模式和高层语法往往会生成状态机或辅助成员。
- `DOTNET_JitDisasm` 只观察当前运行时、ISA、ABI、tier 和 PGO 状态下的一次编译；同一 CIL 不存在唯一永久的“对应汇编”。

## 7. 密码学、正则与互操作

最后运行 `crypto`、`regex` 和 `interop`。

观察点：

- 密码、密钥、nonce、salt、哈希和签名各有不同语义；优先使用认证加密（如 AES-GCM）和明确的签名填充，不自行发明协议。
- 固定时间比较和敏感缓冲区清零只是降低侧信道/残留风险的一部分，生产系统还需要密钥存储、轮换、权限和协议审计。
- 正则必须设置复杂度边界：超时或使用非回溯引擎。
- 跨语言调用依赖公共 CTS/CLS 类型；语言特有类型会改变 C# 调用体验。
- Python 子进程边界需要协议版本、UTF-8、超时、错误响应与退出码；venv 隔离依赖，不等于嵌入 CPython。
- P/Invoke 的真正难点是 ABI、字符集、所有权和生命周期，而不是一条 `extern` 声明。
- C 适合公开固定布局的稳定 ABI；反向回调不能让托管异常越过边界。
- C++ 类型和 STL 容器留在库内，以 `extern "C"`、不透明句柄和状态码形成稳定外壳；C# 使用 `SafeHandle` 释放对象。

## 8. 用综合项目收尾

完成按主题拆分的实验后，运行 `projects` 分类，把前面的语言、运行时和框架能力组合到同一条可验证的数据流中。建议先理解四条独立恢复边界，再运行最终组合：`project.cancellable-data-pipeline` -> `project.versioned-local-service` -> `project.collectible-plugin-host` -> `project.polyglot-compute` -> `project.resilient-analytics-workflow`。

五个综合项目分别覆盖：

- `project.cancellable-data-pipeline`：用 UTF-8 字节偏移 checkpoint、原子 JSON 状态和幂等 NDJSON 死信形成 at-least-once 管线；确定性中断后从连续终态前缀 resume，sink 仍只提交每个业务 ID 一次。
- `project.versioned-local-service`：把 SQLite 写入放在 503 之前，模拟“服务已经提交、客户端只看到失败”的提交歧义；自动重试、显式重放和重启 Kestrel/Store 后都返回相同原始 NDJSON 字节。
- `project.collectible-plugin-host`：扫描 v1/v2 元数据，按能力选择最高兼容版本；v2 普通异常只以复制后的失败描述越过 ALC 边界，随后隔离 v2、回退 v1，并通过刷新路由快照恢复 v2。
- `project.polyglot-compute`：两个 Python 常驻 worker 复用 JSON Lines 会话；其中一个确定性崩溃后由池重建并重试一次幂等请求，再与 C ABI、C++ `SafeHandle` 结果比较。
- `project.resilient-analytics-workflow`：把 checkpoint/DLQ/resume、Python 崩溃恢复、v2 能力路由、可收集 ALC 和 SQLite 重开重放串成跨平台最终项目。

可复用机制位于 `LearnDotnetCSharp.Capstones`，App 中的五个 Demo 只负责接入真实文件、SQLite、插件程序集、Python/C/C++ 和教学断言。这个边界让 `LearnDotnetCSharp.Tests` 的 31 个正式测试可以直接验证连续 checkpoint、single-flight、能力路由和 worker 替换，而不必从控制台输出反推内部状态。

收尾阶段不要只核对最终数字；还要逐项确认 checkpoint 何时才可前移、死信是否重复、提交后失败为何必须重用幂等键、插件异常是否钉住 ALC，以及 worker 崩溃时谁完成旧请求并创建替代进程。五个项目既可单独用 `run <id>` 观察，也会进入 53 项 `self-test` 回归；后者为每项创建独立子进程，并分别汇总 `Passed / Skipped / Failed / Timeout`，所以平台缺失能力不会伪装成成功，超时也不会淹没在普通失败中。

最终提交前运行 `scripts\verify.cmd`：它会检查格式、Release 构建、至少 31 个 Microsoft.Testing.Platform 正式测试和进程隔离回归。综合项目中的中断、提交后 503 与 Python 崩溃都由可控 gate、协议字段或确定性的 `FaultPlan` 触发，不使用随机故障或固定 `Sleep` 猜测时序。

## 实验方法

每次只改一个变量，先写下预测，再运行并对照输出。如果探索并发或 GC，重复多次并区分“语义保证”和“这一次调度碰巧如此”。性能结论应在 Release 模式、无调试器、预热后用专业基准工具复核；本仓库的输出用于理解机制，不是微基准排名。
