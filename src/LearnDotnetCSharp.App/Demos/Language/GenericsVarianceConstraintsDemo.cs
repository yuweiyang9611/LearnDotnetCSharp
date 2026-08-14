using LearnDotnetCSharp.Infrastructure;

namespace LearnDotnetCSharp.Demos.Language;

public sealed class GenericsVarianceConstraintsDemo : IDemo
{
    public DemoMetadata Metadata { get; } = new(
        "language.generics-variance-constraints",
        "language",
        "泛型约束、型变与静态抽象成员",
        "演示 out/in 型变、notnull/unmanaged/new() 约束、静态抽象接口、类型推断，以及 allows ref struct 反约束。",
        [3, 4, 24],
        ["generic constraints", "covariance", "contravariance", "static abstract", "allows ref struct"]);

    public ValueTask RunAsync(DemoContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IProducer<Dog> dogProducer = new Factory<Dog>(() => new Dog("Mochi"));
        IProducer<Animal> animalProducer = dogProducer;
        var produced = animalProducer.Produce();

        var recorder = new AnimalRecorder();
        IConsumer<Animal> animalConsumer = recorder;
        IConsumer<Dog> dogConsumer = animalConsumer;
        dogConsumer.Consume(new Dog("Sora"));
        context.WriteProperty("covariant producer", produced.Name);
        context.WriteProperty("contravariant consumer", recorder.LastName);

        var index = BuildIndex(["alpha", "beta", "alpha"]);
        var unmanagedSize = SizeOf<Coordinate>();
        var initialized = CreateInitialized<MutableToken>();
        context.WriteProperty("notnull dictionary", string.Join(", ", index.OrderBy(pair => pair.Value).Select(pair => pair.Key)));
        context.WriteProperty("unmanaged sizeof", unmanagedSize);
        context.WriteProperty("new() constraint", initialized.IsInitialized);

        var combined = Fold([new CounterValue(3), new CounterValue(4), new CounterValue(5)]);
        var inferredLength = Map("variance", static text => text.Length);
        context.WriteProperty("static abstract fold", combined.Value);
        context.WriteProperty("inferred type arguments", inferredLength);

        Span<int> values = stackalloc[] { 2, 3, 5 };
        var slot = new RefSlot<Span<int>>(values);
        var storedSpan = slot.Value;
        storedSpan[1] = 30;
        context.WriteProperty("allows ref struct", string.Join(", ", values.ToArray()));

        DemoAssert.True(produced is Dog { Name: "Mochi" }, "协变接口应允许 Dog producer 作为 Animal producer");
        DemoAssert.Equal("Sora", recorder.LastName, "逆变接口应允许 Animal consumer 消费 Dog");
        DemoAssert.True(index.Count == 2 && index["alpha"] == 0, "notnull key 应构建稳定索引");
        DemoAssert.True(unmanagedSize == sizeof(int) * 2 && initialized.IsInitialized, "unmanaged 与 new() 约束应生效");
        DemoAssert.True(combined.Value == 12 && inferredLength == 8, "静态抽象成员和类型推断应得到预期结果");
        DemoAssert.SequenceEqual([2, 30, 5], values.ToArray(), "allows ref struct 应允许泛型 ref struct 保存 Span");

        return ValueTask.CompletedTask;
    }

    private static Dictionary<T, int> BuildIndex<T>(IEnumerable<T> values)
        where T : notnull
    {
        var result = new Dictionary<T, int>();
        foreach (var value in values)
        {
            result.TryAdd(value, result.Count);
        }

        return result;
    }

    private static unsafe int SizeOf<T>()
        where T : unmanaged => sizeof(T);

    private static T CreateInitialized<T>()
        where T : IInitializable, new()
    {
        var value = new T();
        value.Initialize();
        return value;
    }

    private static T Fold<T>(ReadOnlySpan<T> values)
        where T : ICombinable<T>
    {
        if (values.IsEmpty)
        {
            throw new ArgumentException("At least one value is required.", nameof(values));
        }

        var result = values[0];
        foreach (var value in values[1..])
        {
            result = T.Combine(result, value);
        }

        return result;
    }

    private static TResult Map<TSource, TResult>(TSource source, Func<TSource, TResult> selector) => selector(source);

    public interface IProducer<out T>
    {
        T Produce();
    }

    public interface IConsumer<in T>
    {
        void Consume(T value);
    }

    public interface IInitializable
    {
        void Initialize();
    }

    public interface ICombinable<TSelf>
        where TSelf : ICombinable<TSelf>
    {
        static abstract TSelf Combine(TSelf left, TSelf right);
    }

    public abstract record Animal(string Name);

    public sealed record Dog(string Name) : Animal(Name);

    public sealed class Factory<T>(Func<T> factory) : IProducer<T>
    {
        public T Produce() => factory();
    }

    public sealed class AnimalRecorder : IConsumer<Animal>
    {
        public string? LastName { get; private set; }

        public void Consume(Animal value)
        {
            LastName = value.Name;
        }
    }

    public sealed class MutableToken : IInitializable
    {
        public bool IsInitialized { get; private set; }

        public void Initialize()
        {
            IsInitialized = true;
        }
    }

    public readonly record struct Coordinate(int X, int Y);

    public readonly record struct CounterValue(int Value) : ICombinable<CounterValue>
    {
        public static CounterValue Combine(CounterValue left, CounterValue right) => new(left.Value + right.Value);
    }

    public ref struct RefSlot<T>(T value)
        where T : allows ref struct
    {
        public T Value { get; set; } = value;
    }
}
