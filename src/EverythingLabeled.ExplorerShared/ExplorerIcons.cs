namespace EverythingLabeled.ExplorerShared;

/// <summary>
/// 右键菜单各动词的图标。图标是稀疏包输出目录下 <c>Assets\El*.ico</c>，
/// 由 <c>scripts/make-explorer-icons.ps1</c> 生成；菜单 DLL 的 <c>GetIcon</c> 用它返回绝对路径。
/// </summary>
public static class ExplorerIcons
{
    /// <summary>图标所在子目录（与 AppxManifest 里清单一同位于输出目录根）。</summary>
    public const string AssetsFolderName = "Assets";

    /// <summary>mode → 图标文件名。未知 mode 返回 <c>null</c>（不回落默认图，免得张冠李戴）。</summary>
    public static string? FileNameForMode(string mode) => mode switch
    {
        "tag" => "ElTag.ico",
        "untag" => "ElUntag.ico",
        "tags" => "ElTags.ico",
        "find" => "ElFind.ico",
        "library" => "ElLibrary.ico",
        _ => null,
    };

    /// <summary>解析出 <paramref name="directory"/> 下真实存在的图标绝对路径；缺失则 <c>null</c>。</summary>
    public static string? Resolve(string? directory, string mode)
    {
        var fileName = FileNameForMode(mode);
        if (fileName is null || string.IsNullOrEmpty(directory))
        {
            return null;
        }

        var path = Path.Combine(directory, AssetsFolderName, fileName);
        return File.Exists(path) ? path : null;
    }
}
