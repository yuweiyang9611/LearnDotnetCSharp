using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using LearnDotnetCSharp.Infrastructure;

namespace LearnDotnetCSharp.Demos.Language;

public sealed class AttributesAdvancedDemo : IDemo
{
    public DemoMetadata Metadata { get; } = new(
        "language.attributes",
        "language",
        "Attribute：声明、读取与编译器行为",
        "从 AttributeUsage、位置/命名参数、继承与多实例开始，演示反射读取、参数标记、调用方信息和 Conditional 编译行为。",
        [4, 19],
        ["AttributeUsage", "CustomAttributeData", "inheritance", "CallerMemberName", "Conditional"]);

    public ValueTask RunAsync(DemoContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var runtimeAttributes = typeof(AdvancedLesson)
            .GetCustomAttributes<LearningTopicAttribute>(inherit: true)
            .OrderBy(attribute => attribute.Order)
            .ToArray();
        var rawMetadata = CustomAttributeData
            .GetCustomAttributes(typeof(AdvancedLesson))
            .Single(attribute => attribute.AttributeType == typeof(LearningTopicAttribute));
        var audienceArgument = rawMetadata.NamedArguments
            .Single(argument => argument.MemberName == nameof(LearningTopicAttribute.Audience));

        context.WriteLine("[自定义 Attribute]");
        foreach (var attribute in runtimeAttributes)
        {
            context.WriteLine($"  {attribute.Order}: {attribute.Topic} ({attribute.Audience})");
        }

        context.WriteProperty("派生类型自身的构造参数", rawMetadata.ConstructorArguments[0].Value);
        context.WriteProperty("派生类型自身的命名参数", audienceArgument.TypedValue.Value);

        var normalizeMethod = typeof(AdvancedLesson).GetMethod(nameof(AdvancedLesson.Normalize))
            ?? throw new MissingMethodException(typeof(AdvancedLesson).FullName, nameof(AdvancedLesson.Normalize));
        var valueParameter = normalizeMethod.GetParameters().Single();
        var hasNotBlankContract = valueParameter.IsDefined(typeof(NotBlankAttribute), inherit: false);
        var normalized = ValidateAndInvoke(normalizeMethod, "  metadata  ");

        context.WriteLine("\n[参数 Attribute 驱动约定]");
        context.WriteProperty("参数带 NotBlank", hasNotBlankContract);
        context.WriteProperty("验证后反射调用", normalized);

        var caller = CaptureCaller();
        context.WriteLine("\n[编译器识别的 Attribute]");
        context.WriteProperty("CallerMemberName", caller.Member);
        context.WriteProperty("CallerFilePath", caller.File);
        context.WriteProperty("CallerLineNumber > 0", caller.Line > 0);

        var conditionalCallExecuted = false;
        DebugOnly(() => conditionalCallExecuted = true);
#if DEBUG
        const bool conditionalCallExpected = true;
        const string configuration = "Debug";
#else
        const bool conditionalCallExpected = false;
        const string configuration = "Release";
#endif
        context.WriteProperty($"Conditional ({configuration})", conditionalCallExecuted);

        DemoAssert.SequenceEqual(
            ["metadata", "reflection", "runtime"],
            runtimeAttributes.Select(attribute => attribute.Topic),
            "AllowMultiple 与 Inherited 应返回基类和派生类的三个主题");
        DemoAssert.Equal("advanced", audienceArgument.TypedValue.Value as string, "命名参数应保存在元数据中");
        DemoAssert.True(hasNotBlankContract && normalized == "METADATA", "参数 Attribute 应驱动验证并允许调用");
        DemoAssert.True(
            caller.Member == nameof(RunAsync) && caller.File == nameof(AttributesAdvancedDemo) + ".cs" && caller.Line > 0,
            "调用方信息 Attribute 应由编译器填充");
        DemoAssert.Equal(conditionalCallExpected, conditionalCallExecuted, "ConditionalAttribute 应按编译符号保留或移除调用");

        return ValueTask.CompletedTask;
    }

    private static string ValidateAndInvoke(MethodInfo method, string value)
    {
        var parameter = method.GetParameters().Single();
        if (parameter.IsDefined(typeof(NotBlankAttribute), inherit: false) && string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Attribute contract rejected a blank value.", parameter.Name);
        }

        return (string)(method.Invoke(null, [value])
            ?? throw new InvalidOperationException("Attributed method returned null."));
    }

    private static CallerObservation CaptureCaller(
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0) =>
        new(member, Path.GetFileName(file), line);

    [Conditional("DEBUG")]
    private static void DebugOnly(Action action) => action();

    private sealed record CallerObservation(string Member, string File, int Line);

    [LearningTopic("metadata", Order = 1, Audience = "all")]
    [LearningTopic("reflection", Order = 2, Audience = "advanced")]
    private abstract class LessonBase;

    [LearningTopic("runtime", Order = 3, Audience = "advanced")]
    private sealed class AdvancedLesson : LessonBase
    {
        public static string Normalize([NotBlank] string value) => value.Trim().ToUpperInvariant();
    }
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class LearningTopicAttribute(string topic) : Attribute
{
    public string Topic { get; } = topic;

    public int Order { get; set; }

    public string Audience { get; set; } = "all";
}

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class NotBlankAttribute : Attribute;
