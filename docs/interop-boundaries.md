# Python、C 与 C++ 互操作边界

基础实验刻意采用不同边界，分别对应进程隔离、稳定原生 ABI 和带对象生命周期的原生库；综合项目再验证这些边界如何长期运行、崩溃恢复并参与同一工作流。

| 实验 | 边界 | 数据与生命周期 |
|---|---|---|
| `interop.python-process-json` | 工作区 `.venv` 子进程 + JSON Lines | JSON 值复制；C# 管理进程、超时、标准流与退出码 |
| `interop.c-abi` | C ABI + `LibraryImport` | 固定数组指针、blittable 结构体；C 反向调用 `UnmanagedCallersOnly` C# 函数指针 |
| `interop.cpp-opaque-handle` | C++ 实现 + `extern "C"` 外壳 | class/vector/string 留在 C++；C# 用 `SafeHandle` 管理不透明指针 |
| `project.polyglot-compute` | 双 Python 常驻 worker + C + C++ | Python 崩溃后以相同请求 ID 重试；C/C++ 仍遵守指针和句柄所有权 |
| `project.resilient-analytics-workflow` | Python worker + 可收集 .NET 插件 | 跨平台组合 checkpoint、进程替换、ALC 卸载和 SQLite 重放 |

## Python：进程协议

运行 `scripts\setup-python.cmd` 会复用 `.venv`，不存在时用本机 Python 3 创建。虚拟环境只提供隔离解释器，本实验不安装第三方包。

`python/interop_worker.py` 每次从标准输入读取一行 JSON，并向标准输出写回一行 JSON。协议包含 `protocolVersion`、请求 ID、操作名和显式错误对象。C# 使用 `ProcessStartInfo.ArgumentList` 传参，以 UTF-8 配置标准流，设置五秒超时，并检查解释器路径、venv 状态、响应内容与退出码。

这种单次交换方式牺牲一部分进程启动和序列化性能，换来明确的故障/内存隔离以及不依赖 CPython 内部 ABI。不要为每个标量启动一个进程；大量小调用应改为长期 worker、批处理请求或专门 RPC。

`LearnDotnetCSharp.Capstones.Polyglot.PythonWorkerPool` 给出长期 worker 的完整实验：有界 `Channel` 为请求施加背压，N 个消费者各自拥有一个持续读取 JSON Lines 的 Python session。启动时先执行 handshake，响应必须带回相同请求 ID、进程 PID 和 generation；同一 ID 仍在途时，池会在写入协议前拒绝重复请求。stderr 始终由独立异步泵读取，只保留固定长度尾部，避免子进程因错误流管道写满而死锁。

worker 在 `analyze` 响应前以 `os._exit(86)` 确定性崩溃时，session 会把 EOF 转换成包含 stderr 尾部的 `PythonWorkerCrashedException`，关闭旧进程并建立下一 generation。只有明确为幂等的分析请求会沿用原 ID 自动重试一次；`crash` 控制请求本身不重试。取消发生在一次请求中途时，协议已无法证明下一行属于哪个请求，因此池会废弃整个 session，而不是把可能错位的 stdout 交给下一调用。

这仍不是任意 Python 代码的安全沙箱。工作进程提供比进程内 CPython 嵌入更清晰的崩溃和内存隔离，但文件、网络、CPU、内存与操作系统权限仍需由容器、作业对象、cgroup 或独立账户限制。自动重试也只适用于无副作用或具有业务幂等键的操作。

## C：稳定 ABI 与反向调用

`native/learn_c` 只公开固定宽度整数、指针、函数指针和顺序布局结构体。C# 侧用 `fixed` 固定托管数组，以 `LibraryImport` 调用 C；反向调用使用带 `CallConvCdecl` 的 `UnmanagedCallersOnly` 静态方法。

反向回调不得让托管异常越过原生边界。回调上下文在本例中指向调用栈上的整数，且 C 只在同步调用期间使用该指针；如果原生库要保存回调或上下文，必须建立更长的所有权协议，并正确管理 `GCHandle` 或原生内存。

## C++：不导出 C++ ABI

`native/learn_cpp` 内部使用 class、`std::vector`、`std::string` 和异常，但导出面只有 `extern "C"` 函数。C++ 异常全部在库内捕获并转换为状态码，字符串复制到调用方提供的 UTF-8 缓冲区，对象以不透明指针表示。

C# 把该指针包装为 `SafeHandle`，即使执行路径抛出托管异常，也会调用原生 destroy 函数。不要跨 DLL 边界直接暴露 STL 容器、C++ 引用、编译器名称修饰或 C++ 异常；它们会把调用方绑定到编译器、运行库和具体 ABI。

## 构建和运行

Windows x64 的原生库由 `scripts\build-native.cmd` 构建。脚本通过 Visual Studio Installer 的 `vswhere.exe` 定位带 C++ 工作负载的 MSVC，不要求当前终端预先配置 `cl.exe`。`LearnDotnetCSharp.App.csproj` 在源文件变化时增量构建，并把两个 DLL 复制到应用输出目录。

```powershell
.\scripts\setup-python.cmd
.\scripts\build-native.cmd
dotnet run --project .\src\LearnDotnetCSharp.App -- run interop.python-process-json
dotnet run --project .\src\LearnDotnetCSharp.App -- run interop.c-abi
dotnet run --project .\src\LearnDotnetCSharp.App -- run interop.cpp-opaque-handle
dotnet run --project .\src\LearnDotnetCSharp.App -- run project.polyglot-compute
dotnet run --project .\src\LearnDotnetCSharp.App -- run project.resilient-analytics-workflow
```

当前仓库只为 C/C++ 示例提供 Windows x64 构建脚本；其他平台会明确跳过这两个实验。C 源码和 C++ 的 C ABI 外壳本身可移植，后续可增加 `cc`/`c++` 的 `.so`/`.dylib` 构建分支。
