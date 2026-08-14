using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using LearnDotnetCSharp.Infrastructure;

namespace LearnDotnetCSharp.Demos.Reflection;

public sealed class ReflectionAdvancedDemo : IDemo
{
    public DemoMetadata Metadata { get; } = new(
        "reflection.advanced",
        "reflection",
        "特性、泛型反射与动态代码",
        "读取自定义特性，关闭并调用泛型成员，然后比较表达式树与 Reflection.Emit 两种运行时代码生成方式。",
        [18, 19, 20],
        ["custom attributes", "MakeGenericMethod", "Expression<TDelegate>", "Reflection.Emit"]);

    public ValueTask RunAsync(DemoContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var openMethod = typeof(GenericProjector).GetMethod(
            nameof(GenericProjector.Describe),
            BindingFlags.Static | BindingFlags.Public)
            ?? throw new MissingMethodException(typeof(GenericProjector).FullName, nameof(GenericProjector.Describe));

        var operation = openMethod.GetCustomAttribute<DemoOperationAttribute>()
            ?? throw new InvalidOperationException("泛型示例方法缺少 DemoOperationAttribute。");
        var closedMethod = openMethod.MakeGenericMethod(typeof(decimal));
        var reflectedResult = closedMethod.Invoke(null, [12.5m, "price"]);

        context.WriteLine("[元数据与泛型成员]");
        context.WriteProperty("特性别名", operation.Alias);
        context.WriteProperty("特性说明", operation.Description);
        context.WriteProperty("开放泛型方法", openMethod);
        context.WriteProperty("关闭后的类型参数", closedMethod.GetGenericArguments()[0]);
        context.WriteProperty("反射调用结果", reflectedResult);

        cancellationToken.ThrowIfCancellationRequested();
        Expression<Func<int, int>> expression = value => checked((value * value) + 1);
        var expressionDelegate = expression.Compile();
        var expressionResult = expressionDelegate(6);

        context.WriteLine("\n[表达式树]");
        context.WriteProperty("表达式", expression);
        context.WriteProperty("根节点", expression.Body.NodeType);
        context.WriteProperty("参数节点", expression.Parameters[0].NodeType);
        context.WriteProperty("Compile 后 f(6)", expressionResult);

        context.WriteLine("\n[Reflection.Emit]");
        context.WriteProperty("支持动态代码", RuntimeFeature.IsDynamicCodeSupported);
        context.WriteProperty("动态代码会被编译", RuntimeFeature.IsDynamicCodeCompiled);

        if (RuntimeFeature.IsDynamicCodeSupported)
        {
            var multiplyAdd = CreateMultiplyAddDelegate();
            var emittedResult = multiplyAdd(6, 7, 1);
            context.WriteProperty("生成公式", "left * right + addend");
            context.WriteProperty("生成方法 (6, 7, 1)", emittedResult);
            DemoAssert.Equal(43, emittedResult, "Reflection.Emit 方法应执行 checked 乘加");
        }
        else
        {
            context.WriteLine("  当前运行环境（例如某些 NativeAOT 部署）不允许 Reflection.Emit，已安全跳过。");
        }

        DemoAssert.Equal("describe", operation.Alias, "自定义特性应可被反射读取");
        DemoAssert.Equal("price<Decimal>=12.5", reflectedResult as string, "关闭泛型方法后反射调用应成功");
        DemoAssert.Equal(37, expressionResult, "表达式树编译结果应正确");

        return ValueTask.CompletedTask;
    }

    private static Func<int, int, int, int> CreateMultiplyAddDelegate()
    {
        var dynamicMethod = new DynamicMethod(
            "MultiplyAdd",
            typeof(int),
            [typeof(int), typeof(int), typeof(int)],
            typeof(ReflectionAdvancedDemo).Module,
            skipVisibility: false);

        var generator = dynamicMethod.GetILGenerator();
        generator.Emit(OpCodes.Ldarg_0);
        generator.Emit(OpCodes.Ldarg_1);
        generator.Emit(OpCodes.Mul_Ovf);
        generator.Emit(OpCodes.Ldarg_2);
        generator.Emit(OpCodes.Add_Ovf);
        generator.Emit(OpCodes.Ret);

        return dynamicMethod.CreateDelegate<Func<int, int, int, int>>();
    }

    [AttributeUsage(AttributeTargets.Method)]
    private sealed class DemoOperationAttribute(string alias, string description) : Attribute
    {
        public string Alias { get; } = alias;

        public string Description { get; } = description;
    }

    private sealed class GenericProjector
    {
        [DemoOperation("describe", "把任意 T 投影为带类型信息的稳定文本")]
        public static string Describe<T>(T value, string label) =>
            $"{label}<{typeof(T).Name}>={Convert.ToString(value, CultureInfo.InvariantCulture)}";
    }
}
