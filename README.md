# LearnDotnetCSharp

这是一个以 **.NET 10** 和 **C# 14** 为基准、包含 53 个可运行实验的高级特性实验室。项目参考《C# 8.0 核心技术指南》的目录组织知识范围，但示例使用当前运行时和语言写法，不复刻书中的旧代码。

> **在线学习站：** [yuweiyang9611.github.io/LearnDotnetCSharp](https://yuweiyang9611.github.io/LearnDotnetCSharp/)
>
> 可以按独立章节 URL 阅读完整教材，在正文、代码、配套资料和实验摘要中全文搜索，沿八阶段路线浏览课程，并在当前浏览器记录学习进度。[四层代码实验台](https://yuweiyang9611.github.io/LearnDotnetCSharp/layers/)还能用同源制品对照高级 C#、教学低层 C#、CIL/元数据与真实 JIT 汇编。教材网页和 PDF 都由同一份 Markdown 自动生成；实验仍需克隆仓库后在本地运行。

## 仓库迁移与隐私说明

本公开仓库由旧的私有仓库迁移而来。由于旧仓库的提交元数据、Pull Request 等记录暴露了私人邮箱等个人隐私，迁移时删除了全部 Git 提交历史和 PR 记录，并删除了旧私有仓库；当前仓库从全新的公开根提交开始。

本仓库的维护提交统一使用 GitHub 提供的 `noreply` 地址。仓库提供了提交前检查，并在 Pull Request 模板中要求检查提交元数据、描述、日志、截图和测试数据。Git 不会在克隆后自动启用仓库内的 hooks，因此首次克隆后请运行一次初始化脚本：

```powershell
.\scripts\enable-git-hooks.ps1 -NoreplyEmail "91787866+yuweiyang9611@users.noreply.github.com"
```

Linux、macOS 或 Git Bash 使用 `./scripts/enable-git-hooks.sh "YOUR_ID+YOUR_USERNAME@users.noreply.github.com"`。详细说明见 [GIT_HOOKS.md](GIT_HOOKS.md)。

重点覆盖：

- C# 14、Attribute、委托/事件/迭代器、运算符/转换、型变、泛型约束与现代 C# 高级语言特性
- 框架基础、集合、LINQ、EF Core/SQLite 查询翻译、LINQ to XML 与版本化序列化契约
- 异步 continuation/上下文、并发内存模型、线程同步、锁自由结构和并行计算
- 异步/随机访问/内存映射 I/O，以及 HTTP 流/重试/取消、本地 TCP、UDP、WebSocket、TLS、HTTP/1.1、HTTP/2、HTTP/3
- 反射、动态代码、可卸载插件、资源本地化，以及高级 C# → Roslyn lowering → PE/元数据 → CIL 的同源证据链
- GC、固定对象、对齐原生内存、SafeHandle、同步/异步释放、终结、内存池、`Span<T>` / `Memory<T>` 和资源生命周期
- CIL 求值栈、async 状态机、JIT 本机汇编、SIMD/硬件内建函数、分层编译、ReadyToRun、NativeAOT 与动态 PGO 的可观察边界
- Activity/Meter/EventSource/TraceSource 可观测性，以及现代密码学原语
- 正则表达式，包括源生成与非回溯引擎
- C# 与 F#、Visual Basic、工作区 Python venv、C ABI、C++ 不透明对象的互操作

## 环境基准

- SDK：`10.0.301`（`global.json` 允许在同一 major/minor 下向较新的 feature band 滚动）
- TFM：`net10.0`
- C#：`14.0`
- Nullable：启用
- .NET analyzers：启用，警告视为错误
- NuGet：`Microsoft.EntityFrameworkCore.Sqlite 10.0.9`；原生 SQLite bundle 显式锁定到已修复审计问题的 `SQLitePCLRaw.bundle_e_sqlite3 3.0.3`
- F# 使用 SDK 自带的 `FSharp.Core`
- Python：Python 3 + 工作区 `.venv`，只使用标准库
- 文档：独立 `.docs-venv` + 固定版本的 ReportLab/PyPDF；生成 PDF 时自动嵌入中文与等宽字体
- C/C++（Windows x64）：Visual Studio 的“使用 C++ 的桌面开发”工作负载

## 快速开始

### Codespaces / Dev Container

[在 GitHub Codespaces 中打开](https://codespaces.new/yuweiyang9611/LearnDotnetCSharp?quickstart=1)，即可获得预配置的 .NET 10、Python 3.11 和 Node.js 24 环境。容器首次创建时会自动生成工作区 `.venv` 并还原解决方案；在本机使用 VS Code 时，也可以安装 Dev Containers 扩展后选择 **Reopen in Container**。

初始化完成后，可以直接执行跨平台验证并预览学习站：

```bash
dotnet build LearnDotnetCSharp.slnx --configuration Release --no-restore
dotnet test --solution LearnDotnetCSharp.slnx --configuration Release --no-restore --no-build --minimum-expected-tests 45
dotnet run --project src/LearnDotnetCSharp.App --configuration Release --no-build -- self-test
dotnet run --project src/LearnDotnetCSharp.App --configuration Release --no-build -- export-code-layers site/layers/artifacts.json
node scripts/build-study-site.mjs
node scripts/serve-study-site.mjs
```

最后一条命令会启动端口 `4173`，Codespaces/Dev Containers 会提示打开转发后的学习站预览。

Dev Container 基于 Linux，适合跨平台托管代码、Python 互操作、测试和静态站构建；HTTP/3/QUIC 与 ISA 相关的 JIT 汇编仍以容器宿主能力为准。仓库自带的 Windows x64 C/C++ DLL、MSVC ABI 及其他 Windows 原生实验会按能力契约明确报告 `Skipped`，不会阻止容器初始化或跨平台学习；要观察这些实验，请在安装了 Visual Studio“使用 C++ 的桌面开发”工作负载的 Windows x64 主机上运行。

### 本地 Windows

```powershell
.\scripts\setup-python.cmd
dotnet restore .\LearnDotnetCSharp.slnx --ignore-failed-sources
dotnet build .\LearnDotnetCSharp.slnx --no-restore
dotnet test --solution .\LearnDotnetCSharp.slnx --no-build --minimum-expected-tests 45
dotnet run --project .\src\LearnDotnetCSharp.App -- list
dotnet run --project .\src\LearnDotnetCSharp.App -- run runtime.overview
dotnet run --project .\src\LearnDotnetCSharp.App -- self-test
```

也可以一次执行完整验证：

```powershell
.\scripts\verify.cmd
```

如果本机允许执行 PowerShell 脚本，也可以运行 `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify.ps1`。完整验证会依次检查格式、执行 Release 构建和正式测试，再以独立子进程运行每个实验。

在线教材会在 GitHub Pages 构建时由同一份 Markdown 自动生成，可从[教材目录](https://yuweiyang9611.github.io/LearnDotnetCSharp/guide/)进入 30 个独立章节、前言和 4 个附录，也可以使用[全文搜索](https://yuweiyang9611.github.io/LearnDotnetCSharp/search/)查找正文、代码、配套资料与实验。学习路线、主题映射、互操作专题和参考资料也会生成站内页面。需要离线阅读时，再生成带目录、书签和页码的 PDF：

```powershell
.\scripts\build-study-guide-pdf.cmd
```

首次运行会在仓库内创建 `.docs-venv` 并安装固定版本依赖；后续运行会复用已验证的环境。成品位于 `output\pdf\LearnDotnetCSharp-Study-Guide.pdf`，生成完成后还会自动检查页数、页面尺寸、标题、书签、乱码，并核对源码目录与学习指导中的全部 53 个实验 ID。该 PDF 作为可直接阅读的主线快照纳入版本控制；修改学习指导后应重新生成并同时提交 Markdown 与 PDF。推送相关文件到 `main` 时，GitHub Actions 也会自动构建并保留 30 天的 `LearnDotnetCSharp-Study-Guide` artifact；工作流支持手动触发。详细说明见[学习指导的附录 C](docs/advanced-dotnet-csharp-study-guide.md#附录-c从-markdown-生成-pdf)。

CLI 命令：

| 命令 | 用途 |
|---|---|
| `list [category]` | 列出全部示例，或只列一个分类 |
| `list-chapter <1..27>` | 按 PDF 章节列出关联实验 |
| `describe <id>` | 查看实验目标、章节和关键知识点 |
| `run <id>` | 运行单个实验；平台不支持时返回稳定的 `Skipped` 结果 |
| `run-category <category>` | 顺序运行一个分类 |
| `run-all` | 顺序运行全部本地安全实验 |
| `self-test` | 以独立子进程运行每个实验，单项硬超时为 15 秒，并汇总 `Passed / Skipped / Failed / Timeout` |
| `export-code-layers <json>` | 从同一份 Roslyn 输入生成高级/低层 C#、CIL/元数据并捕获当前运行时与 ISA 的 JIT 汇编快照 |

`self-test` 不再把平台分支中的提前返回误记为成功。子进程退出码 `0` 表示 `Passed`，`77` 表示 `Skipped`，`124` 保留给 `Timeout`；失败或超时会保留该实验有界的标准输出和标准错误摘要，并让整体命令失败。超时后运行器会终止整个子进程树，因此死锁、忽略取消或污染进程级状态的实验不会阻塞后续项目。

正式测试项目使用 .NET 10 的 Microsoft.Testing.Platform，当前包含 45 个测试，既覆盖目录/运行器基础设施，也直接验证可恢复管线、SQLite 幂等、插件路由和 Python worker 池。GitHub Actions 会在 Windows 与 Linux 上执行还原、Release 构建、格式检查、测试和完整 `self-test`，并在 Pull Request 中重新生成 PDF 与四层代码制品、检查前端 JavaScript 语法、构建静态站及验收关键产物；Windows 同时验证仓库自带的 C/C++ DLL，Linux 通过可用性契约测试和自检确认这些 Windows 专有实验明确报告 `Skipped`。Pages 发布任务也会先从当前提交的 Markdown 与 C# 源码重新生成 PDF、HTML 和 JIT 快照并核对站内副本，避免在线内容来自不同版本。

## 学习备份与浏览器验证

首页、教材目录和章节页提供“导出备份”和“导入备份”。备份包含实验完成记录、章节步骤与笔记、历史/冲突副本和阅读位置，不上传服务器。导入先预览再合并；同版本步骤取并集，冲突笔记保留本地正文并另存副本。旧版本章节与未知记录保留但不计入当前进度，重复导入不会重复生成副本。文件上限为 5 MiB。旧浏览器存储自动迁移，原始键保留；保存失败会持续提示“尚未保存”，当前编辑仍可重试或导出。“清空实验完成记录”不会删除章节笔记。

浏览器验收使用锁定的 Playwright 和 Chromium，覆盖桌面及手机尺寸，并通过 `/LearnDotnetCSharp/` 子路径运行真实生成站点：

```powershell
npm ci
npx playwright install chromium
node scripts/build-study-site.mjs
npm run test:site
```

当前包含 24 个浏览器用例，覆盖搜索、导航、复制、验收、迁移、合并和保存失败。CI 与 Pages 发布前均运行浏览器测试；失败截图和 trace 位于 `artifacts/playwright-results`，HTML 报告位于 `artifacts/playwright-report`。

数据管线通过 `IPipelineStateStore` 支持 JSON 教学快照和 SQLite 增量事务。两个综合实验使用 SQLite，保留 JSON v1 文件兼容性，不自动迁移。`MaxUncommittedRecords` 默认 64：有效帧和死信均占用额度，连续 checkpoint 成功持久化后才归还。NDJSON 在验收时显式导出；同一状态文件仅支持一个管线拥有者，业务处理器仍须幂等。

Python worker 的 `StartupTimeout` 默认 5 秒，与单次 `RequestTimeout` 独立。首次启动或替代 worker 启动失败会使整个池故障，结束在途/排队请求并拒绝后续请求；并发释放等待同一个清理任务，进程退出和标准错误泵均有界等待。

## 解决方案结构

```text
LearnDotnetCSharp.slnx
├─ .devcontainer/                            # Codespaces/Dev Container 的 .NET、Python 与初始化配置
├─ .github/workflows/
│  ├─ build-and-test.yml                     # Windows/Linux 代码、测试与全实验回归
│  ├─ build-study-guide-pdf.yml              # 学习指导 PDF 构建与 artifact
│  └─ deploy-pages.yml                       # 在线学习站构建与 GitHub Pages 发布
├─ site/                                     # 学习门户、全文搜索、分章阅读器、实验目录与本地进度交互
├─ src/
│  ├─ LearnDotnetCSharp.App/                 # CLI、示例发现器及全部 C# 实验
│  ├─ LearnDotnetCSharp.Capstones/           # 可独立测试的综合项目核心与持久化/进程边界
│  ├─ LearnDotnetCSharp.FSharpInterop/       # 可由 C# 直接调用的 F# 程序集
│  ├─ LearnDotnetCSharp.VisualBasicInterop/  # 可由 C# 直接调用的 VB 程序集
│  ├─ LearnDotnetCSharp.PluginContract/      # 默认加载上下文共享的插件契约
│  ├─ LearnDotnetCSharp.SamplePlugin/        # v1 示例插件与日语资源
│  └─ LearnDotnetCSharp.SamplePlugin.V2/     # 用于热切换、隔离和回退的 v2 插件
├─ tests/
│  └─ LearnDotnetCSharp.Tests/                # 45 个 MTP 正式测试：基础设施、Core 与边界恢复
├─ python/                                   # .venv 子进程加载的 Python JSON worker
├─ native/                                   # C API 与带 extern C 外壳的 C++ 实现
├─ docs/
│  ├─ pdf-topic-map.md                       # PDF 27 章到现代示例的映射
│  ├─ learning-path.md                       # 推荐学习顺序和观察点
│  ├─ advanced-dotnet-csharp-study-guide.md  # 53 个实验的详细课程、练习与 PDF 内容源
│  ├─ interop-boundaries.md                  # Python/C/C++ 的 ABI、协议与所有权说明
│  └─ references.md                          # 官方与规范性延伸资料
└─ scripts/
   ├─ setup-python.cmd                       # 创建或验证工作区 .venv
   ├─ setup-docs-python.cmd                  # 创建固定依赖的文档专用 .docs-venv
   ├─ markdown_to_pdf.py                     # Markdown 渲染、字体嵌入与 PDF 结构校验
   ├─ markdown_to_site.mjs                   # Markdown 到语义化 HTML 的构建期转换
   ├─ templates/                             # 教材目录、章节与资料页 HTML 模板
   ├─ build-study-guide-pdf.cmd              # 一键生成详细学习指导 PDF
   ├─ build-study-site.mjs                   # 组装并校验 GitHub Pages 静态产物
   ├─ serve-study-site.mjs                   # 本地预览静态学习站
   ├─ build-native.cmd                       # 定位 MSVC 并构建 C/C++ DLL
   ├─ verify.cmd                             # 格式、Release 构建、测试与进程隔离自检
   └─ verify.ps1                             # PowerShell 等价版本
```

应用在启动时通过反射发现 `IDemo` 实现，因此增加实验只需新增一个无参示例类并提供稳定的 `Metadata.Id`。所有真实网络端点只绑定 loopback，不访问公网；GC 和线程实验会明确区分教学操作与生产建议。

`LearnDotnetCSharp.Capstones` 把可复用且需要精确测试的机制从 CLI 演示中抽出，按 `DataPipeline`、`LocalService`、`PluginHost` 和 `Polyglot` 四个边界组织。依赖方向保持单向；`A -> B` 表示 A 依赖 B：

```text
LearnDotnetCSharp.App -> LearnDotnetCSharp.Capstones -> LearnDotnetCSharp.PluginContract
LearnDotnetCSharp.Tests -> LearnDotnetCSharp.Capstones
LearnDotnetCSharp.Tests -> LearnDotnetCSharp.App
```

Core 不依赖 `App`、`IDemo`、控制台输出或 `DemoAssert`。App 负责真实 Kestrel、临时文件、工作区 Python、C/C++ 和教学验收；正式测试可以直接调用 Core，使用 gate 和确定性故障代替 `Sleep` 与随机失败。

## 综合项目级实验

完成分项实验后，可运行 `projects` 分类中的五个综合项目级实验。它们不重复讲解单一 API，而是把前面的异步、并发、I/O、网络、序列化、反射、可观测性和跨语言互操作组合成带验收断言的完整数据流：

```powershell
dotnet run --project .\src\LearnDotnetCSharp.App -- run project.cancellable-data-pipeline
dotnet run --project .\src\LearnDotnetCSharp.App -- run project.versioned-local-service
dotnet run --project .\src\LearnDotnetCSharp.App -- run project.collectible-plugin-host
dotnet run --project .\src\LearnDotnetCSharp.App -- run project.polyglot-compute
dotnet run --project .\src\LearnDotnetCSharp.App -- run project.resilient-analytics-workflow
dotnet run --project .\src\LearnDotnetCSharp.App -- run-category projects
```

本轮综合项目深化了四条原有主线：数据管线增加 UTF-8 字节 checkpoint、幂等死信和中断后 resume；本地服务把 503 移到 SQLite 提交之后，并在重启 Kestrel/Store 后重放原始响应；插件宿主按能力在 v1/v2 中选择最高兼容版本，失败后隔离 v2 并回退 v1；跨语言项目改用两个 Python 常驻 worker，其中一个确定性崩溃后重建并重试。第五个 `project.resilient-analytics-workflow` 再把这四条恢复链组合到同一个跨平台分析流程中。

## 实验清单

| ID | 主要机制 |
|---|---|
| `runtime.overview` | CLR、JIT、GC 模式、运行时与平台能力 |
| `runtime.jit-simd-pgo` | 标量/`Vector<T>` 对照、硬件内建函数能力分支、分层编译与动态 PGO 观察边界 |
| `runtime.collectible-plugin` | `AssemblyLoadContext`、依赖解析、共享契约、卫星资源和可收集卸载 |
| `diagnostics.observability` | TraceSource、ActivitySource、Meter、EventSource、StackTrace 与 Stopwatch |
| `language.csharp14` | C# 14 extension members、`field`、空条件赋值、Span 转换、partial 成员、原地运算符 |
| `language.advanced` | 泛型数学、封闭 record 层次、模式、`ref struct`、`stackalloc`/指针、`dynamic` |
| `language.operators-conversions` | 算术/比较运算符、隐式/显式与 checked 转换、nullable 提升、`is`/`as`/`Convert` 边界 |
| `language.generics-variance-constraints` | `in`/`out` 型变、`notnull`/`unmanaged`/`new()`、静态抽象成员与 `allows ref struct` |
| `language.attributes` | `AttributeUsage`、多实例/继承、反射读取、参数标记、调用方信息、`Conditional` |
| `language.delegates-events-iterators` | 委托与闭包、事件、异常筛选、Flags、`yield`、匿名类型和命名元组 |
| `framework.text-globalization` | Rune/文本元素、UTF-8、区域性解析/格式化与显式字符串比较器 |
| `framework.time-numerics-identity` | 时间点/时区、日期值、`BigInteger`、位运算、舍入、Flags 与 UUID v7 |
| `collections.core-custom-buffer` | 队列/栈/优先队列/集合/冻结字典，以及自定义 `IReadOnlyList<T>` 环形缓冲区 |
| `linq.query-semantics` | 查询语法、延迟执行、物化快照、相关子查询与 `IQueryable` 表达式树 |
| `linq.operators` | LINQ 操作族、.NET 10 `LeftJoin`/`RightJoin`、`CountBy`/`AggregateBy` |
| `linq.efcore-sqlite-provider` | EF Core 10、SQLite、真实表达式树翻译、参数化 SQL 与客户端边界 |
| `xml.linq` | LINQ to XML 函数式构造、命名空间、查询、修改与对象注解 |
| `serialization.xml-json` | 异步 `XmlWriter`/`XmlReader`、源生成 JSON 和严格输入约束 |
| `serialization.contract-versioning` | XmlSerializer 显式形状、JSON 多态判别器、扩展数据和版本兼容 |
| `io.streams-pipelines-compression` | 异步文件流、异步枚举、GZip 与跨缓冲区 `PipeReader` 解析 |
| `io.random-access-memory-map` | 并发位置式 `RandomAccess` I/O 与内存映射文件的一致性 |
| `async.cancellation-stream-valuetask` | 取消、超时、异步流和 `ValueTask` 同步快路径 |
| `async.task-when-each` | 完成顺序与输入顺序、`Task.WhenEach` / `WhenAll` |
| `async.continuations-context` | continuation、`SynchronizationContext`、`ConfigureAwait(false)`、`ExecutionContext` 与同步阻塞症状 |
| `concurrency.channel-pipeline` | 有界 Channel、背压、并发集合、原子累加 |
| `concurrency.parallel-plinq` | `Parallel.For`、PLINQ、有序结果和取消 |
| `concurrency.memory-model-lock-free` | `Volatile` 发布、CAS 循环、Treiber stack、ABA 版本戳与伪共享布局 |
| `threading.execution-context` | 专用线程、线程池、`ExecutionContext` / `AsyncLocal` |
| `threading.synchronization-primitives` | `SemaphoreSlim`、事件、倒计数和原子最大值 |
| `networking.tcp-loopback` | TCP、`NetworkStream`、UTF-8 帧和资源释放 |
| `networking.httpclient-loopback` | `HttpClient`、HTTP/1.1、本地最小服务器、超时 |
| `networking.http2-http3` | Kestrel、TLS/ALPN、精确 HTTP/2 协商，以及平台支持时的 QUIC/HTTP/3 |
| `networking.http-streaming-resilience` | `ResponseHeadersRead`、流式 JSON、幂等 GET 重试、取消与 `RequestAborted` |
| `networking.udp-websocket-tls` | DNS、UDP 数据报、WebSocket 帧和带证书固定校验/ALPN 的 SslStream |
| `memory.gc` | 代际 GC、堆快照、弱引用、分配计数和 `ArrayPool<T>` |
| `memory.span-memory-ownership` | `Span<T>` / `Memory<T>`、跨 await 生命周期和池化缓冲区所有权 |
| `memory.disposal-finalization` | SafeHandle、IDisposable/IAsyncDisposable、SuppressFinalize 与受控终结观察 |
| `memory.pinning-native-memory` | pinned object heap、`GCHandle`、端序视图、对齐 `NativeMemory` 与 `SafeHandle` 所有权 |
| `reflection.advanced` | 特性、泛型反射、表达式树和 checked `Reflection.Emit` |
| `compiler.roslyn-il` | 高级/低层等价 C#、Roslyn lowering、同一 PE 的 MethodDef/元数据/CIL 核对和 async 状态机 |
| `compiler.incremental-generator-analyzer` | `IIncrementalGenerator`、生成源码、语义诊断与分析器报告 |
| `crypto.modern-primitives` | CSPRNG、哈希/HMAC、PBKDF2、AES-GCM、RSA-PSS、ECDSA 与定时安全比较 |
| `regex.advanced` | `GeneratedRegex`、有限超时、命名组、替换和非回溯引擎 |
| `interop.managed-languages` | C# 实际调用 F# 模块和 Visual Basic 类型 |
| `interop.native-pinvoke` | `LibraryImport`、Windows/POSIX 进程 API 和 ABI 边界 |
| `interop.python-process-json` | 工作区 `.venv`、子进程生命周期、UTF-8 JSON Lines、超时和退出码 |
| `interop.c-abi` | C ABI、blittable 结构体、固定数组指针与 `UnmanagedCallersOnly` 反向回调 |
| `interop.cpp-opaque-handle` | `extern "C"`、不透明 C++ 对象、状态码、UTF-8 缓冲区与 `SafeHandle` |
| `project.cancellable-data-pipeline` | UTF-8 字节 checkpoint、SQLite 增量事务、显式 NDJSON 死信导出、有界连续提交窗口与中断后恢复 |
| `project.versioned-local-service` | 版本化契约、HMAC、SQLite 幂等、提交后 503、服务重启重放与 NDJSON 流式响应 |
| `project.collectible-plugin-host` | v1/v2 能力路由、热切换、故障隔离、回退、异常复制与可收集加载上下文 |
| `project.polyglot-compute` | Python 常驻 worker 池和崩溃重建，以及 C ABI、C++ 不透明对象和跨语言不变量 |
| `project.resilient-analytics-workflow` | checkpoint/DLQ/resume、Python 崩溃重试、v2 插件路由、SQLite 重开重放的端到端组合 |

## 如何阅读一个实验

建议沿四层观察：

1. 源码层：高级语义、教学用低层等价展开、所有权、取消和异常边界。
2. 编译产物层：生成成员、PE 元数据、MethodDef token、方法体和 CIL 是否形成同源证据。
3. 运行时层：加载、线程、分配、GC、动态代码或网络状态的实际输出。
4. 本机层：JIT/AOT 在当前 ISA、ABI、tier 与 PGO 状态下生成的机器码；不要把一次反汇编当成永久契约。

详细课程见 [docs/advanced-dotnet-csharp-study-guide.md](docs/advanced-dotnet-csharp-study-guide.md)，完整章节映射见 [docs/pdf-topic-map.md](docs/pdf-topic-map.md)，推荐顺序见 [docs/learning-path.md](docs/learning-path.md)，互操作边界见 [docs/interop-boundaries.md](docs/interop-boundaries.md)，外部资料见 [docs/references.md](docs/references.md)；它们都会在 GitHub Pages 构建时转换为站内可搜索页面。

Roslyn 示例直接引用所选 SDK 的编译器程序集，因此能离线构建且版本与 `global.json` 一致；这依赖 SDK 的 `RoslynTargetsPath` 布局，项目在构建前会显式验证文件是否存在。Linux/macOS 的系统 P/Invoke 分支已按平台隔离；仓库自带的 C/C++ 动态库当前只配置并验证了 Windows x64 MSVC 构建，其他平台会明确跳过。

HTTP/2/3 实验使用进程内自签名证书且只信任本次生成证书的 SHA-256 指纹，不修改系统证书库。HTTP/3 需要平台 QUIC/MsQuic；不支持时实验仍会验证 HTTP/2，并把 HTTP/3 明确报告为 `SKIPPED`。需要 Kestrel 详细日志时可在运行前设置 `LEARN_DOTNET_HTTP_TRACE=1`。
