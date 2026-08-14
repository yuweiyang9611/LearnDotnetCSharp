using System.Globalization;
using System.Numerics;
using LearnDotnetCSharp.Infrastructure;

namespace LearnDotnetCSharp.Demos.Framework;

public sealed class TimeNumericsAndIdentityDemo : IDemo
{
    public DemoMetadata Metadata { get; } = new(
        "framework.time-numerics-identity",
        "framework",
        "时间、数值、枚举与标识",
        "以 DateTimeOffset/TimeZoneInfo、DateOnly/TimeOnly、BigInteger、位运算、舍入、Flags 和 UUID v7 覆盖框架基础常用值类型。",
        [6],
        ["DateTimeOffset", "TimeZoneInfo", "DateOnly", "BigInteger", "BitOperations", "Guid v7"]);

    public ValueTask RunAsync(DemoContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var utcInstant = new DateTimeOffset(2026, 7, 13, 3, 30, 0, TimeSpan.Zero);
        var studyZone = TimeZoneInfo.CreateCustomTimeZone(
            "Study/JST",
            TimeSpan.FromHours(9),
            "Study/JST",
            "Study/JST");
        var localInstant = TimeZoneInfo.ConvertTime(utcInstant, studyZone);
        var date = DateOnly.ParseExact("2026-07-13", "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var time = TimeOnly.ParseExact("12:30:45", "HH:mm:ss", CultureInfo.InvariantCulture);

        context.WriteLine("[时间点、时区与日历值]");
        context.WriteProperty("UTC instant", utcInstant.ToString("O", CultureInfo.InvariantCulture));
        context.WriteProperty("JST instant", localInstant.ToString("O", CultureInfo.InvariantCulture));
        context.WriteProperty("DateOnly + TimeOnly", date.ToDateTime(time));

        var largePower = BigInteger.Pow(2, 128);
        var bitCount = BitOperations.PopCount(0b1011_0101u);
        var bankersRounding = decimal.Round(2.5m, 0, MidpointRounding.ToEven);
        var awayRounding = decimal.Round(2.5m, 0, MidpointRounding.AwayFromZero);
        var features = RuntimeFeatures.DynamicCode | RuntimeFeatures.HardwareIntrinsics;
        var version7 = Guid.CreateVersion7(utcInstant);

        context.WriteLine("\n[数值、位与标识]");
        context.WriteProperty("2^128 digits", largePower.ToString(CultureInfo.InvariantCulture).Length);
        context.WriteProperty("PopCount", bitCount);
        context.WriteProperty("round ToEven / Away", $"{bankersRounding} / {awayRounding}");
        context.WriteProperty("Flags", features);
        context.WriteProperty("UUID version", version7.Version);

        DemoAssert.True(
            localInstant.Offset == TimeSpan.FromHours(9) && localInstant.Hour == 12,
            "同一时间点转换到 +09:00 后应保持 instant 并改变本地显示");
        DemoAssert.True(date.DayOfWeek == DayOfWeek.Monday && time.Second == 45, "日期和时间应按固定格式解析");
        DemoAssert.True(largePower == (BigInteger.One << 128) && bitCount == 5, "大整数和位计数结果应正确");
        DemoAssert.True(bankersRounding == 2m && awayRounding == 3m, "舍入策略必须显式选择");
        DemoAssert.True(
            features.HasFlag(RuntimeFeatures.DynamicCode) && version7.Version == 7,
            "Flags 与 UUID v7 应暴露其结构语义");

        return ValueTask.CompletedTask;
    }

    [Flags]
    private enum RuntimeFeatures
    {
        None = 0,
        DynamicCode = 1 << 0,
        HardwareIntrinsics = 1 << 1,
        NativeAot = 1 << 2,
    }
}
