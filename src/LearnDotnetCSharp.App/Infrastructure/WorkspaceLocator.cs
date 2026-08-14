namespace LearnDotnetCSharp.Infrastructure;

public static class WorkspaceLocator
{
    public static string FindRoot()
    {
        foreach (var startPath in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(Path.GetFullPath(startPath));
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "LearnDotnetCSharp.slnx")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException(
            "无法从当前目录或应用程序目录定位 LearnDotnetCSharp.slnx。请从仓库内运行示例。");
    }
}
