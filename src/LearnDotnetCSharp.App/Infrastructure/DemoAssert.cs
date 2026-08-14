namespace LearnDotnetCSharp.Infrastructure;

public static class DemoAssert
{
    public static void True(bool condition, string invariant)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"示例不变量失败：{invariant}");
        }
    }

    public static void Equal<T>(T expected, T actual, string invariant)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"示例不变量失败：{invariant}；期望 '{expected}'，实际 '{actual}'。");
        }
    }

    public static void SequenceEqual<T>(
        IEnumerable<T> expected,
        IEnumerable<T> actual,
        string invariant)
    {
        if (!expected.SequenceEqual(actual))
        {
            throw new InvalidOperationException($"示例不变量失败：{invariant}");
        }
    }
}
