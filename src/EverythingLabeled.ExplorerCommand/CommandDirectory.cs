namespace EverythingLabeled.ExplorerCommand;

/// <summary>菜单扩展 DLL 自己所在的目录 —— Host exe 与 Assets 图标都按它定位。</summary>
internal static class CommandDirectory
{
    public static string Resolve()
    {
        // 稀疏包用 ExternalLocation，COM 服务器跑在壳层 surrogate 里：
        // 工作目录与 AppContext.BaseDirectory 都不可依赖，只能问自己的程序集路径。
        var location = typeof(CommandDirectory).Assembly.Location;
        if (!string.IsNullOrEmpty(location))
        {
            var dir = Path.GetDirectoryName(location);
            if (!string.IsNullOrEmpty(dir))
            {
                return dir;
            }
        }

        return AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
