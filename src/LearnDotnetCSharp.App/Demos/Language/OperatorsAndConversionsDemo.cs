using System.Globalization;
using LearnDotnetCSharp.Infrastructure;

namespace LearnDotnetCSharp.Demos.Language;

public sealed class OperatorsAndConversionsDemo : IDemo
{
    public DemoMetadata Metadata { get; } = new(
        "language.operators-conversions",
        "language",
        "运算符重载与用户定义转换",
        "通过定点数值类型演示算术/比较运算符、checked 上下文、隐式/显式转换、可空提升，以及 is/as/Convert 不调用转换运算符的边界。",
        [3, 4, 6],
        ["operator overloads", "implicit conversion", "explicit conversion", "checked operators", "lifted operators"]);

    public ValueTask RunAsync(DemoContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        FixedPoint whole = 12;
        decimal exactDecimal = whole;
        var rounded = (FixedPoint)12.346m;
        context.WriteProperty("implicit int -> FixedPoint", whole);
        context.WriteProperty("implicit FixedPoint -> decimal", exactDecimal);
        context.WriteProperty("explicit decimal -> FixedPoint", rounded);

        var price = (FixedPoint)19.95m;
        var fee = (FixedPoint)2.05m;
        var total = price + fee;
        var scaled = total * 3 / 2;
        context.WriteProperty("overloaded arithmetic", $"{price} + {fee} = {total}; scaled = {scaled}");
        context.WriteProperty("comparison operators", price < total && total >= fee);

        FixedPoint? optionalPrice = price;
        FixedPoint? optionalFee = fee;
        FixedPoint? missing = null;
        var liftedTotal = optionalPrice + optionalFee;
        var liftedMissing = missing + optionalFee;
        context.WriteProperty("lifted nullable operator", $"value={liftedTotal}, null={liftedMissing is null}");

        var nearMaximum = FixedPoint.FromRaw(long.MaxValue);
        var oneRawUnit = FixedPoint.FromRaw(1);
        var uncheckedWrapped = unchecked(nearMaximum + oneRawUnit);
        var checkedAdditionThrew = ThrowsOverflow(() => _ = checked(nearMaximum + oneRawUnit));

        var beyondInt32 = FixedPoint.FromRaw(checked(((long)int.MaxValue + 1) * FixedPoint.Scale));
        var uncheckedInt32 = unchecked((int)beyondInt32);
        var checkedConversionThrew = ThrowsOverflow(() => _ = checked((int)beyondInt32));
        context.WriteProperty("unchecked operator wraps", uncheckedWrapped.RawValue == long.MinValue);
        context.WriteProperty("checked operator throws", checkedAdditionThrew);
        context.WriteProperty("unchecked explicit conversion", uncheckedInt32);
        context.WriteProperty("checked explicit conversion throws", checkedConversionThrew);

        object boxedInteger = 7;
        var isFixedPoint = boxedInteger is FixedPoint;
        var asFixedPoint = boxedInteger as FixedPoint?;
        var explicitlyConverted = (FixedPoint)(int)boxedInteger;
        var convertChangeTypeRejected = ThrowsInvalidCast(
            () => _ = Convert.ChangeType(whole, typeof(decimal), CultureInfo.InvariantCulture));
        context.WriteProperty("is/as invoke conversion", $"is={isFixedPoint}, as={asFixedPoint is not null}");
        context.WriteProperty("unbox then convert", explicitlyConverted);
        context.WriteProperty("Convert.ChangeType rejected", convertChangeTypeRejected);

        DemoAssert.Equal(12m, exactDecimal, "无损用户定义转换应允许隐式使用");
        DemoAssert.Equal(12.35m, rounded.ToDecimal(), "有损 decimal 转换应显式执行并采用指定舍入规则");
        DemoAssert.Equal(22m, total.ToDecimal(), "重载加法应按原始定点值相加");
        DemoAssert.Equal(33m, scaled.ToDecimal(), "乘法和除法重载应组合工作");
        DemoAssert.True(price < total && total > fee, "比较运算符应与原始值顺序一致");
        DemoAssert.True(liftedTotal == total && liftedMissing is null, "值类型运算符应自动提升到 nullable");
        DemoAssert.True(
            uncheckedWrapped.RawValue == long.MinValue && checkedAdditionThrew,
            "普通与 checked 加法应具有不同的溢出语义");
        DemoAssert.True(
            uncheckedInt32 == int.MinValue && checkedConversionThrew,
            "普通与 checked 显式转换应具有不同的溢出语义");
        DemoAssert.True(
            !isFixedPoint && asFixedPoint is null && explicitlyConverted == (FixedPoint)7,
            "is/as 不应调用用户定义转换；显式转换表达式可以调用");
        DemoAssert.True(convertChangeTypeRejected, "Convert.ChangeType 不应发现用户定义转换运算符");

        return ValueTask.CompletedTask;
    }

    private static bool ThrowsOverflow(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (OverflowException)
        {
            return true;
        }
    }

    private static bool ThrowsInvalidCast(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (InvalidCastException)
        {
            return true;
        }
    }
}

public readonly struct FixedPoint : IEquatable<FixedPoint>, IComparable<FixedPoint>
{
    public const long Scale = 100;

    private FixedPoint(long rawValue)
    {
        RawValue = rawValue;
    }

    public long RawValue { get; }

    public static FixedPoint FromRaw(long rawValue) => new(rawValue);

    public static FixedPoint FromDecimal(decimal value) =>
        new(checked((long)decimal.Round(value * Scale, 0, MidpointRounding.ToEven)));

    public decimal ToDecimal() => (decimal)RawValue / Scale;

    public int CompareTo(FixedPoint other) => RawValue.CompareTo(other.RawValue);

    public bool Equals(FixedPoint other) => RawValue == other.RawValue;

    public override bool Equals(object? obj) => obj is FixedPoint other && Equals(other);

    public override int GetHashCode() => RawValue.GetHashCode();

    public override string ToString() => ToDecimal().ToString("0.00", CultureInfo.InvariantCulture);

    public static FixedPoint operator +(FixedPoint left, FixedPoint right) =>
        new(unchecked(left.RawValue + right.RawValue));

    public static FixedPoint operator checked +(FixedPoint left, FixedPoint right) =>
        new(checked(left.RawValue + right.RawValue));

    public static FixedPoint operator -(FixedPoint left, FixedPoint right) =>
        new(unchecked(left.RawValue - right.RawValue));

    public static FixedPoint operator -(FixedPoint value) => new(unchecked(-value.RawValue));

    public static FixedPoint operator *(FixedPoint value, int multiplier) =>
        new(unchecked(value.RawValue * multiplier));

    public static FixedPoint operator /(FixedPoint value, int divisor) => new(value.RawValue / divisor);

    public static bool operator ==(FixedPoint left, FixedPoint right) => left.Equals(right);

    public static bool operator !=(FixedPoint left, FixedPoint right) => !left.Equals(right);

    public static bool operator <(FixedPoint left, FixedPoint right) => left.RawValue < right.RawValue;

    public static bool operator >(FixedPoint left, FixedPoint right) => left.RawValue > right.RawValue;

    public static bool operator <=(FixedPoint left, FixedPoint right) => left.RawValue <= right.RawValue;

    public static bool operator >=(FixedPoint left, FixedPoint right) => left.RawValue >= right.RawValue;

    public static implicit operator FixedPoint(int value) => new((long)value * Scale);

    public static implicit operator decimal(FixedPoint value) => value.ToDecimal();

    public static explicit operator FixedPoint(decimal value) => FromDecimal(value);

    public static explicit operator int(FixedPoint value) => unchecked((int)(value.RawValue / Scale));

    public static explicit operator checked int(FixedPoint value) => checked((int)(value.RawValue / Scale));
}
