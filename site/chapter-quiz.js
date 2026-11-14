// The self-test of each chapter: three questions written from the chapter text, answered on the chapter page.
// A pass is recorded with the revision hash the build computes from these questions (like chapter-tasks.js),
// so a rewritten question asks again. Keep three questions per chapter and one correct answer index each.
export const chapterQuizzes = Object.freeze(
  [
    {
      chapter: 1,
      questions: [
        {
          prompt: "在 Linux 等尚未构建仓库 C/C++ 动态库的平台上，受影响的实验在 self-test 中应该显示为什么？",
          options: ["Passed，因为代码本身没有错误", "Skipped，并记录缺失的平台能力", "Failed，直到补齐原生库为止"],
          answer: 1,
          explanation: "两种环境的共同验收条件是 Failed = 0 且 Timeout = 0；当前平台不支持的能力必须显式显示为 Skipped，不能把未执行的能力算作成功。",
        },
        {
          prompt: "仓库不锁定 SDK 版本。用比 .NET 11 更新的 SDK 构建时，什么保持不变？",
          options: ["什么都不固定，目标框架和语言版本都跟着 SDK 升级", "目标框架 net11.0 和 C# 15，它们固定在项目文件中", "SDK 版本，global.json 仍会把它拉回 11.0"],
          answer: 1,
          explanation: "仓库不锁定 SDK，由本机安装的最新 SDK 负责构建；目标框架 net11.0 和 C# 15 固定在项目文件中，不随 SDK 变化。global.json 现在只选择测试运行器，运行实验和测试仍需要 .NET 11 运行时。",
        },
        {
          prompt: "HTTP/3 实验在缺少平台 QUIC 能力时内部报告了 SKIPPED，这说明什么？",
          options: ["HTTP/2 的验证也失败了", "这不等于 HTTP/2 验证失败", "整个网络章节必须跳过"],
          answer: 1,
          explanation: "排查顺序第 6 条明确指出：平台 QUIC 能力可能让实验内部报告 SKIPPED，这不等于 HTTP/2 验证失败。",
        },
      ],
    },
    {
      chapter: 2,
      questions: [
        {
          prompt: "run <id> 与 self-test 的根本区别是什么？",
          options: ["run 在当前进程中运行，self-test 为每个 ID 启动全新的子进程", "run 只打印元数据，self-test 才真正执行代码", "两者完全相同，只是输出格式不同"],
          answer: 0,
          explanation: "run <id> 在当前进程中运行，便于调试；self-test 为每个 ID 启动全新的子进程，独立捕获输出，并用进程树超时隔离进程级状态。",
        },
        {
          prompt: "子进程退出码 77 表示哪种结果？",
          options: ["Failed", "Timeout", "Skipped，因平台能力缺失"],
          answer: 2,
          explanation: "退出码 0 表示 Passed，77 表示因平台能力缺失而 Skipped；断言或未处理异常是 Failed，超过 15 秒则记录为 Timeout。",
        },
        {
          prompt: "为什么同一进程中的 CancellationToken 不能替代 self-test 的进程隔离？",
          options: ["因为 CancellationToken 只能用于异步方法", "因为线程池设置、静态缓存、AssemblyLoadContext 等是进程级状态，取消无法强制恢复它们", "因为 CancellationToken 会让实验变慢"],
          answer: 1,
          explanation: "线程池设置、静态缓存、AssemblyLoadContext、非托管崩溃、未释放句柄和死锁都是进程级状态，只有新进程才能把它们彻底清除。",
        },
      ],
    },
    {
      chapter: 3,
      questions: [
        {
          prompt: "五步学习闭环的第一步是什么？",
          options: ["运行代码并记录通过", "不运行代码，先写下关键输出、线程/资源状态和可能的异常", "直接修改多个变量观察结果"],
          answer: 1,
          explanation: "第一步是预测：不运行代码，写下关键输出、线程/资源状态和可能的异常，之后才运行、解释、扰动与复盘。",
        },
        {
          prompt: "“扰动”这一步的要求是什么？",
          options: ["尽量多改几个参数以节省时间", "只改变一个变量，例如容量、超时、比较器或协议版本", "把实验改写成另一种语言"],
          answer: 1,
          explanation: "扰动只改变一个变量，这样结果变化才能归因；同时改多个变量会掩盖概念边界。",
        },
        {
          prompt: "导入学习数据备份时，笔记冲突如何处理？",
          options: ["以导入内容覆盖本地笔记", "保留本地正文并另存副本", "直接拒绝整个备份"],
          answer: 1,
          explanation: "导入先预览再合并：同版本步骤取并集，笔记冲突保留本地正文并另存副本，旧章节版本不自动计入当前验收。",
        },
      ],
    },
    {
      chapter: 4,
      questions: [
        {
          prompt: "推荐进度表中的“建议时间”应当如何看待？",
          options: ["是必须达到的考核指标", "不是考核指标；不能脱离代码解释关键输出就还没完成实验", "只是为了估算总课时"],
          answer: 1,
          explanation: "时间不是考核指标。如果你不能在不看代码的情况下解释关键输出，就还没有完成该实验。",
        },
        {
          prompt: "推荐进度中第 3 阶段的主题是什么？",
          options: ["异步、线程、并发、并行", "反射、编译器、正则与密码学", "F#/VB/Python/C/C++ 互操作"],
          answer: 0,
          explanation: "第 3 阶段是异步、线程、并发、并行，阶段产出是可取消并发管线。",
        },
        {
          prompt: "第 6 阶段的阶段产出是什么？",
          options: ["语言/运行时边界笔记", "插件/代码分析小项目", "明确所有权的跨语言模块"],
          answer: 2,
          explanation: "第 6 阶段覆盖 F#/VB/Python/C/C++ 互操作，产出是明确所有权的跨语言模块。",
        },
      ],
    },
    {
      chapter: 5,
      questions: [
        {
          prompt: "FrameworkDescription 与 Environment.Version 描述的是什么？",
          options: ["C# 语言版本", "运行环境，而不是语言版本", "编译时使用的 reference assemblies 版本"],
          answer: 1,
          explanation: "它们描述运行环境；语言版本由 LangVersion 决定，编译时的绑定用的是 net11.0 reference assemblies。",
        },
        {
          prompt: "RuntimeFeature.IsDynamicCodeSupported 决定什么能力是否可用？",
          options: ["Server GC 是否开启", "反射发射、表达式编译等动态代码路径", "进程架构是 x64 还是 Arm64"],
          answer: 1,
          explanation: "IsDynamicCodeSupported/IsDynamicCodeCompiled 决定反射发射、表达式编译等能力是否可用；NativeAOT 环境可能不允许这些路径。",
        },
        {
          prompt: "关于分层编译与动态 PGO，下面哪一句是正确的？",
          options: ["循环预热足以证明某个方法已经完成重编译", "DOTNET_TieredPGO 打印 <unset> 表示该功能已关闭", "<unset> 表示使用运行时默认值，预热只制造调用历史"],
          answer: 2,
          explanation: "<unset> 表示使用运行时默认值而不是关闭；循环预热只制造调用历史，不能证明某个方法在某一刻完成了重编译。",
        },
      ],
    },
    {
      chapter: 6,
      questions: [
        {
          prompt: "C# 14 的 extension member 在调度本质上是什么？",
          options: ["仍是静态成员，调用形式不改变其调度本质", "会变成实例成员并参与虚调用", "会在运行时生成新的类型"],
          answer: 0,
          explanation: "extension member 仍是静态成员；调用形式不会改变其调度本质。",
        },
        {
          prompt: "target?.Value = Expensive() 在 target 为 null 时会怎样？",
          options: ["抛出 NullReferenceException", "右侧仍会计算，只是不赋值", "右侧不会被计算"],
          answer: 2,
          explanation: "null-conditional assignment 在 target 为 null 时不会计算右侧，实验用计数器证明这一点。",
        },
        {
          prompt: "普通运算符与 checked 用户定义运算符并存时，由什么决定选择哪一个？",
          options: ["运算符声明的顺序", "调用点的 checked context", "操作数的运行时类型"],
          answer: 1,
          explanation: "调用点的 checked context 决定选择 op_Addition 还是 op_CheckedAddition、op_Explicit 还是 op_CheckedExplicit。",
        },
      ],
    },
    {
      chapter: 7,
      questions: [
        {
          prompt: "string.Length 回答的是四种文本长度中的哪一种？",
          options: ["UTF-16 code unit 数", "Unicode scalar value 数", "用户感知文本元素数"],
          answer: 0,
          explanation: "同一段文本至少有四种长度：UTF-16 code unit、Unicode scalar value、文本元素和编码后的字节数；string.Length 只回答第一种。",
        },
        {
          prompt: "标识符和协议字段应当使用哪种比较方式？",
          options: ["当前用户区域性的文化比较", "ordinal 比较", "Turkish culture 比较"],
          answer: 1,
          explanation: "标识符、协议字段常用 ordinal 比较；只有面向人的排序或搜索才考虑文化规则。",
        },
        {
          prompt: "DateOnly/TimeOnly 适合表达什么？",
          options: ["时间线上的确切时间点", "没有时区含义的日历值", "时区规则转换"],
          answer: 1,
          explanation: "DateTimeOffset 表示时间线上的时间点，TimeZoneInfo 负责时区转换，DateOnly/TimeOnly 表达没有时区含义的日历值。",
        },
      ],
    },
    {
      chapter: 8,
      questions: [
        {
          prompt: "选择集合类型时应该先问什么？",
          options: ["哪个集合最快", "需要什么语义，例如先进先出或唯一性", "哪个集合占内存最少"],
          answer: 1,
          explanation: "不要先问哪个集合最快，先问需要什么语义：先进先出、后进先出、按优先级取出、唯一性等各有典型选择。",
        },
        {
          prompt: "构建后高频只读查找的典型选择是什么？",
          options: ["FrozenDictionary<TKey,TValue>", "SortedSet<T>", "Queue<T>"],
          answer: 0,
          explanation: "构建后高频只读查找选 FrozenDictionary；排序后的唯一集合选 SortedSet；先进先出选 Queue。",
        },
        {
          prompt: "自定义 IReadOnlyList<T> 的环形缓冲区必须保证什么？",
          options: ["物理数组顺序与逻辑顺序相同", "Count、索引器和枚举的逻辑顺序一致", "容量必须是 2 的幂"],
          answer: 1,
          explanation: "环形缓冲区的物理数组顺序不等于使用者看到的逻辑顺序，但 Count、索引器和枚举必须保持一致的逻辑顺序。",
        },
      ],
    },
    {
      chapter: 9,
      questions: [
        {
          prompt: "对普通 List 调用 AsQueryable() 后会发生什么？",
          options: ["查询会被翻译成 SQL", "仍是 LINQ to Objects provider，不会凭空产生 SQL", "查询立即执行并物化"],
          answer: 1,
          explanation: "AsQueryable() 用在普通 List 上仍是 LINQ to Objects provider；只有真实 Provider（如 EF Core）才把表达式树翻译成 SQL。",
        },
        {
          prompt: "Single 与 First 的区别应该怎样理解？",
          options: ["Single 只是 First 的更严格拼写", "Single 表达“必须恰好一个”的业务不变量", "两者在空序列上的行为相同"],
          answer: 1,
          explanation: "Single 表达“必须恰好一个”的业务不变量，不是 First 的更严格拼写；要重点分析空序列、重复键等边界。",
        },
        {
          prompt: "在 EF Core 查询中，为什么不要用 AsEnumerable 掩盖无法翻译的筛选？",
          options: ["因为 AsEnumerable 会抛出异常", "因为它可能把大量数据拉到客户端，并改变性能和一致性语义", "因为 AsEnumerable 会关闭变更跟踪"],
          answer: 1,
          explanation: "关键边界：AsEnumerable 之后的筛选在本地执行，可能把大量数据拉到客户端，并改变性能和一致性语义。",
        },
      ],
    },
    {
      chapter: 10,
      questions: [
        {
          prompt: "LINQ to XML 中故意省略 XNamespace 查询带命名空间的元素时会怎样？",
          options: ["抛出异常", "查询结果为空而不是抛异常", "自动匹配任何命名空间"],
          answer: 1,
          explanation: "限定名由命名空间和本地名组成，省略 XNamespace 的查询匹配不到元素，结果为空而不是抛异常。",
        },
        {
          prompt: "对象树/DOM 与流式 Reader/Writer 的主要差异是什么？",
          options: ["DOM 单遍低驻留内存，流式需要随机访问", "DOM 易于随机访问和修改但内存随文档增长；流式单遍、低驻留内存、需要显式维护读取状态", "两者只是 API 风格不同"],
          answer: 1,
          explanation: "对象树易于随机访问和修改，内存通常随文档增长；流式 Reader/Writer 单遍、低驻留内存，但需要显式维护读取状态。",
        },
        {
          prompt: "契约版本化实验用什么机制保留未来字段？",
          options: ["BinaryFormatter", "JsonExtensionData", "反射动态生成属性"],
          answer: 1,
          explanation: "实验通过 JsonExtensionData 保留未来字段，并用白名单判别器恢复多态运行时类型；它明确不恢复 BinaryFormatter。",
        },
      ],
    },
    {
      chapter: 11,
      questions: [
        {
          prompt: "关于 CancellationToken，下面哪一句正确？",
          options: ["取消是合作协议，不会强行终止任意代码", "取消会立即终止正在运行的任何代码", "取消等同于创建一个新线程"],
          answer: 0,
          explanation: "async 表示可暂停的控制流，不等于创建线程；取消是合作协议，CancellationToken 不会强行终止任意代码。",
        },
        {
          prompt: "什么时候适合使用 ValueTask<T>？",
          options: ["任何异步方法都应默认使用它替代 Task<T>", "同步完成概率高且性能证据充分的路径", "需要多次 await 同一结果时"],
          answer: 1,
          explanation: "ValueTask<T> 适合同步完成概率高且性能证据充分的路径，不应默认替代 Task<T>，也不要多次 await 同一个 ValueTask。",
        },
        {
          prompt: "ConfigureAwait(false) 对 ExecutionContext 和 AsyncLocal<T> 有什么影响？",
          options: ["会阻止它们流动", "不恢复 SynchronizationContext 的调度目标，但不会自动阻止 ExecutionContext 和 AsyncLocal<T> 流动", "会把 AsyncLocal 清空"],
          answer: 1,
          explanation: "ConfigureAwait(false) 只是不回到专用上下文；ExecutionContext 与 AsyncLocal<T> 仍然流动，SuppressFlow 才影响新排队的工作。",
        },
      ],
    },
    {
      chapter: 12,
      questions: [
        {
          prompt: "AsyncLocal<T> 属于哪种上下文？",
          options: ["线程本地存储", "逻辑调用上下文，不等于线程本地存储", "进程全局静态状态"],
          answer: 1,
          explanation: "AsyncLocal<T> 是随逻辑执行流传播的逻辑调用上下文，不等于线程本地存储。",
        },
        {
          prompt: "要限制并发度，应该优先考虑哪个原语？",
          options: ["ManualResetEventSlim", "SemaphoreSlim", "CountdownEvent"],
          answer: 1,
          explanation: "限制并发度选 SemaphoreSlim；一次性打开闸门选 ManualResetEventSlim；等待 N 个参与者完成选 CountdownEvent。",
        },
        {
          prompt: "Interlocked.CompareExchange 循环适合什么场景？",
          options: ["跨多个字段自动维持业务不变量", "单个值的读-改-写", "替代所有 lock"],
          answer: 1,
          explanation: "原子不等于无竞争；CompareExchange 循环适合单个值的读-改-写，保护复合不变量仍需要 lock 等互斥机制。",
        },
      ],
    },
    {
      chapter: 13,
      questions: [
        {
          prompt: "有界 Channel<T> 写满后会发生什么？",
          options: ["丢弃最新消息", "产生背压", "自动扩容"],
          answer: 1,
          explanation: "有界 Channel 同时表达容量上限和写满后的背压，以及完成与失败传播。",
        },
        {
          prompt: "Parallel.For 与 PLINQ 面向什么场景？",
          options: ["异步 I/O 的替代品", "CPU 并行，不是异步 I/O 的替代品", "任何循环都应该并行化"],
          answer: 1,
          explanation: "Parallel.For 与 PLINQ 面向 CPU 并行；工作量不足以覆盖调度和分区成本时，并行版本不一定更快。",
        },
        {
          prompt: "只比较值的 CAS 为什么无法发现 ABA 问题？",
          options: ["因为 CAS 不是原子的", "因为状态从 A 改成 B 再改回 A 后，值比较看不出中间变化", "因为 Volatile.Read 会缓存旧值"],
          answer: 1,
          explanation: "ABA 对照刻意把状态从 A 改成 B 再改回 A；只比较值的 CAS 无法知道中间发生过变化，把版本号打包进同一个原子字才能拒绝陈旧比较。",
        },
      ],
    },
    {
      chapter: 14,
      questions: [
        {
          prompt: "PipeReader 的 AdvanceTo(consumed, examined) 告诉管道什么？",
          options: ["缓冲区应该扩容多少", "哪些数据已消费、哪些已检查", "下一次读取的超时时间"],
          answer: 1,
          explanation: "AdvanceTo 告诉管道哪些数据已消费、哪些已检查；设置错误会导致重复读取或停滞。",
        },
        {
          prompt: "Stream.ReadAsync 的一个关键事实是什么？",
          options: ["它保证填满整个缓冲区", "它允许短读", "它总是读到流结尾"],
          answer: 1,
          explanation: "Stream.ReadAsync 允许短读，所以解析器必须自己处理数据边界和读到一部分的情况。",
        },
        {
          prompt: "RandomAccess 与普通 Stream 的差别是什么？",
          options: ["它以文件句柄和显式偏移工作，没有共享 Position", "它只能顺序读取", "它自动把文件映射进内存"],
          answer: 0,
          explanation: "RandomAccess 以文件句柄和显式偏移工作，没有共享 Position，适合并发处理独立区间。",
        },
      ],
    },
    {
      chapter: 15,
      questions: [
        {
          prompt: "TCP 是什么样的传输？",
          options: ["保留消息边界的可靠传输", "可靠、有序的字节流，但没有消息边界", "不保证到达顺序的数据报"],
          answer: 1,
          explanation: "TCP 是可靠、有序的字节流，但没有消息边界，一次 Write 不保证对应一次 Read，所以必须使用 framing 规则。",
        },
        {
          prompt: "HTTP/2 与 HTTP/3 实验中，请求版本与响应版本的关系是什么？",
          options: ["请求版本只是意图，响应版本才是结果", "请求版本决定最终协议", "两者总是相同"],
          answer: 0,
          explanation: "请求版本只是意图，响应版本才是结果；实验用 RequestVersionExact 验证真实协商结果。",
        },
        {
          prompt: "ResponseHeadersRead 完成选项意味着什么？",
          options: ["SendAsync 读完整个正文后才返回", "SendAsync 在响应头可用后返回，正文需要调用方继续读取并负责释放 response", "响应头会被忽略"],
          answer: 1,
          explanation: "ResponseHeadersRead 让 SendAsync 在响应头可用后返回；正文需要调用方继续读取、设置取消并负责释放 response。",
        },
      ],
    },
    {
      chapter: 16,
      questions: [
        {
          prompt: "对象是否被 GC 回收取决于什么？",
          options: ["变量是否离开肉眼可见的代码块", "可达性", "对象的大小"],
          answer: 1,
          explanation: "对象是否回收取决于可达性，不取决于变量是否离开肉眼可见的代码块；弱引用不阻止回收。",
        },
        {
          prompt: "Span<T> 与 Memory<T> 的核心区别是什么？",
          options: ["Span<T> 可以跨 await，Memory<T> 不能", "Span<T> 是受限生命周期的同步视图；Memory<T> 可以存入对象并跨 await", "Memory<T> 自动拥有底层数据"],
          answer: 1,
          explanation: "Span<T> 是受限生命周期的同步视图；Memory<T> 可以存入对象并跨 await。它们都不自动拥有底层数据。",
        },
        {
          prompt: "成功 Dispose 后为什么要调用 GC.SuppressFinalize？",
          options: ["避免不必要的终结", "强制立即回收内存", "让对象重新可用"],
          answer: 0,
          explanation: "Dispose 提供确定性释放；finalizer 只是昂贵的最后防线，成功 Dispose 后用 GC.SuppressFinalize 避免不必要终结。",
        },
      ],
    },
    {
      chapter: 17,
      questions: [
        {
          prompt: "Activity/trace 回答的主要问题是什么？",
          options: ["系统整体趋势和分布怎样", "一次操作经过了哪些组件", "一段代码经历了多少时间"],
          answer: 1,
          explanation: "日志回答发生了什么，Activity/trace 回答一次操作经过了哪些组件，Meter/metric 回答整体趋势和分布，Stopwatch 回答耗时。",
        },
        {
          prompt: "实验验证了 Activity.Current 的什么特性？",
          options: ["跨 await 传播", "只在创建线程上可见", "在 EventListener 中不可用"],
          answer: 0,
          explanation: "实验验证 Activity.Current 跨 await 传播、Meter listener 收到 counter/histogram、EventListener 收到强类型事件。",
        },
        {
          prompt: "给 Activity 添加高基数 tag 会带来什么问题？",
          options: ["不会有任何代价", "增加存储与查询代价，还可能带入敏感数据", "会让 trace ID 失效"],
          answer: 1,
          explanation: "生产设计要问 tag 是否包含高基数或敏感数据，以及没有 listener 时是否避免昂贵 payload。",
        },
      ],
    },
    {
      chapter: 18,
      questions: [
        {
          prompt: "如果插件契约程序集在两个 AssemblyLoadContext 各加载一份会怎样？",
          options: ["类型身份自动合并", "即使全名相同，类型身份也可能不兼容", "第二份加载会失败"],
          answer: 1,
          explanation: "契约程序集必须由 Default 上下文共享；在两个上下文各加载一份时，即使全名相同，类型身份也可能不兼容。",
        },
        {
          prompt: "AssemblyLoadContext.Unload() 调用后会立刻回收插件吗？",
          options: ["会，调用后内存立即释放", "不会，Unload 只是发起卸载，真正回收要等所有根消失", "只有调用 GC.Collect 后才能卸载"],
          answer: 1,
          explanation: "Unload() 只是发起卸载，真正回收要等线程、静态字段、委托、反射对象和实例等所有根消失。",
        },
        {
          prompt: "compiler.roslyn-il 实验为什么要核对 PEReader 与反射取得同一串 CIL 字节？",
          options: ["为了证明元数据行、PE 方法体、反射对象和反汇编文本都在谈同一个方法", "为了比较 Debug 与 Release 的性能", "为了验证 NativeAOT 可用"],
          answer: 0,
          explanation: "这条核对证明元数据行、PE 方法体、加载后的反射对象和反汇编文本都在谈同一个方法，修复了“编译一个方法、反汇编另一个方法”的问题。",
        },
      ],
    },
    {
      chapter: 19,
      questions: [
        {
          prompt: "“低层 C#”在本章中的准确含义是什么？",
          options: ["Roslyn 的正式中间输出语言", "为教学手写的语义等价展开，不是 Roslyn 的正式输出", "ECMA-335 定义的文本格式"],
          answer: 1,
          explanation: "低层 C# 是为教学手写的语义等价展开；Roslyn 内部的 bound tree 和 lowering 不是公共、稳定的“低层 C#”。",
        },
        {
          prompt: "checked(sum + value) 在 CIL 中对应什么证据？",
          options: ["普通 add", "add.ovf，而不是普通 add", "一条 call 指令"],
          answer: 1,
          explanation: "有符号溢出必须抛出 OverflowException，所以 CIL 使用 add.ovf 而不是普通 add。",
        },
        {
          prompt: "metadata token 0x06000001 表示什么？",
          options: ["进程中的内存地址", "MethodDef 表第 1 行", "方法体的 RVA"],
          answer: 1,
          explanation: "高字节 0x06 表示 MethodDef 表，低三字节表示行号 1；token 像模块内的持久句柄，不是进程地址。",
        },
      ],
    },
    {
      chapter: 20,
      questions: [
        {
          prompt: "为什么把 MethodHandle.GetFunctionPointer() 附近的字节当作“该方法唯一汇编”不可靠？",
          options: ["因为它可能指向 stub，也可能只代表当时的某个 tier", "因为函数指针总是指向 CIL 字节", "因为 JIT 会把机器码回写到程序集"],
          answer: 0,
          explanation: "分层编译可以先发布较快版本再替换热点方法，调用点可能经过可修补入口；函数指针可能指向 stub 或某个 tier 的版本。",
        },
        {
          prompt: "NativeAOT 是什么？",
          options: ["Roslyn 直接把 C# 变成汇编", "完全没有运行时的程序", "发布时由 ILC、后端和平台链接器生成目标程序，运行时没有 JIT 但保留 GC、异常、类型系统等能力"],
          answer: 2,
          explanation: "NativeAOT 仍先有 CIL/元数据语义输入，再由 AOT 工具链生成目标文件并链接运行时组件；它省去部署后的 JIT，但保留必要运行时能力。",
        },
        {
          prompt: "用 DOTNET_JitDisasm 观察汇编时为什么要先 Release 构建再用 --no-build 启动新进程？",
          options: ["因为 Debug 构建不能运行", "因为环境变量在进程启动时读取，--no-build 可以避免把 MSBuild/Roslyn 自身的 JIT 输出混进来", "因为 --no-build 会关闭分层编译"],
          answer: 1,
          explanation: "环境变量在进程启动时读取；--no-build 避免 MSBuild/Roslyn 自身的大量 JIT 输出混入反汇编文件。",
        },
      ],
    },
    {
      chapter: 21,
      questions: [
        {
          prompt: "什么时候应该考虑 [GeneratedRegex]？",
          options: ["模式固定且高频时", "模式来自用户输入时", "需要反向引用时"],
          answer: 0,
          explanation: "模式固定且高频时考虑 [GeneratedRegex]；对不可信输入则要设置超时。",
        },
        {
          prompt: "NonBacktracking 引擎能提供什么边界？",
          options: ["支持所有回溯型结构", "接近输入长度线性的匹配边界，但有功能限制", "无限制的递归语法解析"],
          answer: 1,
          explanation: "能接受功能限制时，NonBacktracking 可提供接近输入长度线性的边界；反向引用等高级特性可能不受支持。",
        },
        {
          prompt: "正则表达式适合什么？",
          options: ["替代完整的递归语法解析器", "局部文本语言", "解析任意嵌套结构"],
          answer: 1,
          explanation: "正则适合局部文本语言，不适合替代完整的递归语法解析器。",
        },
      ],
    },
    {
      chapter: 22,
      questions: [
        {
          prompt: "SHA-256 提供什么？",
          options: ["无密钥摘要，不提供身份认证", "共享密钥消息认证", "加密并认证明文"],
          answer: 0,
          explanation: "SHA-256 是无密钥摘要，不提供身份认证；HMAC-SHA-256 才是共享密钥消息认证，AES-GCM 加密并认证。",
        },
        {
          prompt: "AES-GCM 的 nonce 必须满足什么要求？",
          options: ["保密即可，可以重复", "在同一 key 下必须唯一", "必须与 salt 相同"],
          answer: 1,
          explanation: "nonce 在同一 AES-GCM key 下必须满足唯一性要求；salt 可以公开但应随机且按记录保存。",
        },
        {
          prompt: "关于清零托管缓冲区，下面哪句正确？",
          options: ["可以保证所有副本立刻被擦除", "不能保证所有副本、寄存器和内部实现状态都被立刻擦除", "清零后无需任何其他密钥管理"],
          answer: 1,
          explanation: "清零托管缓冲区不能保证所有副本、寄存器和内部实现状态都被立刻擦除；生产系统还需要密钥托管、轮换和威胁建模。",
        },
      ],
    },
    {
      chapter: 23,
      questions: [
        {
          prompt: "C#、F#、VB 为什么可以直接互相引用？",
          options: ["因为它们共享 CLR、CTS 和程序集元数据", "因为编译器会把它们翻译成 C#", "因为它们使用相同的语法"],
          answer: 0,
          explanation: "三种语言共享 CLR、CTS 和程序集元数据，因此可以直接引用；但语言特有结构会影响公共 API 的 C# 体验。",
        },
        {
          prompt: "公共边界应优先使用什么类型？",
          options: ["F# 的 option 和 discriminated union", "稳定、可空性清晰的 CLR 类型", "各语言自己的内部习惯类型"],
          answer: 1,
          explanation: "公共边界优先使用稳定、可空性清晰的 CLR 类型；不要让调用方必须理解另一语言的内部习惯才能安全调用。",
        },
        {
          prompt: "从 F# 返回 option 时，练习要求设计什么？",
          options: ["一个 C# 友好的 facade", "一个新的 CLR 类型系统", "把 C# 代码改写成 F#"],
          answer: 0,
          explanation: "练习：观察 C# 看到的实际 API 形状，再设计一个 C# 友好的 facade。",
        },
      ],
    },
    {
      chapter: 24,
      questions: [
        {
          prompt: "LibraryImport 通过什么机制建立平台调用？",
          options: ["运行时反射", "源生成", "动态代码发射"],
          answer: 1,
          explanation: "LibraryImport 通过源生成建立平台调用，调用代码在编译期生成。",
        },
        {
          prompt: "使用 P/Invoke 时必须核对的内容不包括哪一项？",
          options: ["calling convention 与结构布局", "字符串编码和内存由谁分配释放", "C# 源码的缩进风格"],
          answer: 2,
          explanation: "必须核对目标库和导出符号、calling convention、整数宽度与结构布局、字符串编码、错误码规则、内存所有权和平台可用性。",
        },
        {
          prompt: "平台分支在公共 API 中的地位是什么？",
          options: ["让测试通过的技巧", "公共 API 能力的一部分", "可以省略的细节"],
          answer: 1,
          explanation: "平台分支不是“让测试通过”的技巧，而是公共 API 能力的一部分。",
        },
      ],
    },
    {
      chapter: 25,
      questions: [
        {
          prompt: "Python 进程协议中 stderr 的用途是什么？",
          options: ["传输 JSON Lines 响应", "只用于诊断信息", "传输请求"],
          answer: 1,
          explanation: "stdin 承载 UTF-8 JSON Lines request，stdout 承载 response，stderr 只用于 diagnostics。",
        },
        {
          prompt: "进程隔离的 Python 互操作有什么优点？",
          options: ["没有序列化开销", "ABI 隔离、部署模型清晰、Python 崩溃不直接破坏 CLR", "不需要处理超时"],
          answer: 1,
          explanation: "优点是 ABI 隔离、部署模型清晰、Python 崩溃不直接破坏 CLR；代价是序列化、进程启动、协议版本和错误映射。",
        },
        {
          prompt: "worker 输出一行非法 JSON 时，练习要求区分什么？",
          options: ["协议错误与非零退出码", "stdout 与 stdin", "Python 版本与 .NET 版本"],
          answer: 0,
          explanation: "练习：输出一行非法 JSON，区分协议错误与非零退出码；让 worker 超时则确认宿主终止并回收子进程。",
        },
      ],
    },
    {
      chapter: 26,
      questions: [
        {
          prompt: "C ABI 边界应使用哪类类型？",
          options: ["C++ class 与 std::string", "固定宽度类型、blittable struct、指针+长度、整数状态码", "托管泛型集合"],
          answer: 1,
          explanation: "C ABI 边界使用固定宽度类型、blittable struct、指针+长度、整数状态码。",
        },
        {
          prompt: "UnmanagedCallersOnly 回调必须保证什么？",
          options: ["允许托管异常穿过原生栈", "不让托管异常穿过原生栈，并把错误转换为明确状态码", "回调可以在任意时刻被释放"],
          answer: 1,
          explanation: "必须保证函数入口在调用期间有效、不让托管异常穿过原生栈、不越界读取指针、布局与 calling convention 一致、错误转为状态码。",
        },
        {
          prompt: "给结构体增加字段但只修改一侧会带来什么风险？",
          options: ["没有风险，运行时会自动适配", "ABI 版本不一致", "编译器会报错阻止构建"],
          answer: 1,
          explanation: "练习：分别修改一侧和两侧，观察 ABI 版本不一致的风险；结构布局必须完全一致。",
        },
      ],
    },
    {
      chapter: 27,
      questions: [
        {
          prompt: "为什么不直接把 C++ class 暴露给 C#？",
          options: ["因为 C++ 对象布局和异常不是稳定的跨语言边界", "因为 C# 不支持指针", "因为 C++ 没有导出函数"],
          answer: 0,
          explanation: "不要直接把 C++ class、异常、std::string、std::vector 暴露给 C#；用 extern \"C\" 和 opaque handle 隐藏对象布局。",
        },
        {
          prompt: "C# 侧用什么确保 C++ 对象被释放？",
          options: ["GC 自动释放", "SafeHandle", "finalizer 直接调用 delete"],
          answer: 1,
          explanation: "SafeHandle 确保 C# 侧释放对象；状态码阻止 C++ 异常跨边界。",
        },
        {
          prompt: "缓冲区过小时推荐的 API 形状是什么？",
          options: ["抛出 C++ 异常", "先查询所需长度，再复制的两阶段 API", "让 C++ 侧分配托管数组"],
          answer: 1,
          explanation: "练习：模拟缓冲区过小，设计“先查询所需长度，再复制”的两阶段 API，并确认所有失败路径都不泄漏句柄。",
        },
      ],
    },
    {
      chapter: 28,
      questions: [
        {
          prompt: "数据管线的语义是什么？",
          options: ["凭空得到 exactly-once", "at-least-once 读取 + 幂等 sink", "at-most-once 读取"],
          answer: 1,
          explanation: "这里的语义是 at-least-once 读取 + 幂等 sink，不是凭空得到 exactly-once；ContiguousCheckpointTracker 只推进连续终态前缀。",
        },
        {
          prompt: "版本化本地服务最关键的故障点被故意放在哪里？",
          options: ["SQLite 提交之前", "提交之后、发送响应之前", "客户端发送请求之前"],
          answer: 1,
          explanation: "故障点位于提交之后、发送响应之前：客户端不知道服务是否已提交，只能用相同业务幂等键重试，从 SQLite 逐字节重放。",
        },
        {
          prompt: "插件宿主为什么把异常复制成 PluginFailure(TypeName, Message)？",
          options: ["为了让原始异常对象留在宿主里", "为了不让插件反射对象和异常逃出可收集边界，使旧 ALC 能被回收", "为了减少日志大小"],
          answer: 1,
          explanation: "边界外只返回共享契约结果、复制后的失败描述、描述符和 WeakReference；原始异常会成为 GC 根，阻止 ALC 卸载。",
        },
      ],
    },
    {
      chapter: 29,
      questions: [
        {
          prompt: "为什么 async 不等于创建线程？",
          options: ["因为 async 只是可暂停的控制流，continuation 由上下文调度", "因为 async 方法总在线程池上运行", "因为 async 只能用于 UI 程序"],
          answer: 0,
          explanation: "async 表示可暂停的控制流；它不创建线程，continuation 在哪里运行由 SynchronizationContext 等机制决定。",
        },
        {
          prompt: "为什么拥有 SafeHandle 的类通常不需要 finalizer？",
          options: ["因为 SafeHandle 已经把关键原生释放放在可靠抽象中", "因为 finalizer 会阻止 Dispose", "因为原生句柄不需要释放"],
          answer: 0,
          explanation: "SafeHandle 把关键原生释放放在可靠抽象中，拥有者通常不再需要自己的 finalizer；Dispose 提供确定性释放。",
        },
        {
          prompt: "服务在提交后断开或返回 503 时，客户端为什么必须重用业务幂等键？",
          options: ["因为只有幂等键才能消解“是否已提交”的歧义，让服务重放而不是再执行一次", "因为 HTTP 协议要求重试必须相同", "因为 nonce 不能更换"],
          answer: 0,
          explanation: "客户端不知道服务是否已经提交；重试使用新请求、新 nonce 但相同业务幂等键，服务端从持久化响应重放。",
        },
      ],
    },
    {
      chapter: 30,
      questions: [
        {
          prompt: "最终验收清单对 self-test 的要求是什么？",
          options: ["全部 53 项都必须 Passed", "Failed 与 Timeout 均为 0，当前平台支持的实验全部 Passed，不支持项明确 Skipped", "只要没有 Failed 即可"],
          answer: 1,
          explanation: "self-test 的 Failed 与 Timeout 均为 0；当前平台支持的实验全部 Passed，不支持项明确 Skipped。",
        },
        {
          prompt: "每个实验至少要完成怎样的一轮？",
          options: ["只运行并记录通过", "预测 - 运行 - 修改 - 复盘", "阅读源码即可"],
          answer: 1,
          explanation: "清单要求每个实验至少完成一次“预测 - 运行 - 修改 - 复盘”。",
        },
        {
          prompt: "关于五个综合项目，清单要求能解释哪些恢复状态？",
          options: ["管线中断、提交后 503、插件失败回退和 Python 崩溃各自的恢复状态", "只需解释最终输出", "只需说明依赖关系图"],
          answer: 0,
          explanation: "五个综合项目都至少完成一次修改，并能解释管线中断、提交后 503、插件失败回退和 Python 崩溃各自的恢复状态。",
        },
      ],
    },
  ].map((quiz) => Object.freeze({ ...quiz, questions: Object.freeze(quiz.questions.map((question) => Object.freeze({ ...question, options: Object.freeze([...question.options]) }))) })),
);
