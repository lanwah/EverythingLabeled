namespace EverythingLabeled.ExplorerHost;

/// <summary>Pure helpers for Find result row title / subtitle / kind icon key.</summary>
public static class FindResultPresentation
{
    // DrawingImage keys in Themes/KindIcons.xaml (mirrors design/explorer-ui/icons).
    public const string IconKeyFile = "KindIconFile";
    public const string IconKeyFolder = "KindIconFolder";
    public const string IconKeyUrl = "KindIconUrl";

    public static string Title(string uri, string? displayName)
    {
        if (!string.IsNullOrWhiteSpace(displayName))
            return displayName.Trim();

        if (Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.IsFile)
        {
            var local = parsed.LocalPath.TrimEnd(
                System.IO.Path.DirectorySeparatorChar,
                System.IO.Path.AltDirectorySeparatorChar);
            var name = System.IO.Path.GetFileName(local);
            if (!string.IsNullOrEmpty(name))
                return name;
            return local.Length > 0 ? local : uri;
        }

        return uri;
    }

    public static string KindLabel(string kind) =>
        kind switch
        {
            "folder" => "文件夹",
            "file" => "文件",
            "url" => "链接",
            _ => kind,
        };

    public static string Subtitle(string uri, string kind, bool canOpen)
    {
        var line = KindLabel(kind) + "  ·  " + uri;
        return canOpen ? line : line + "  [" + Copy.CannotOpen + "]";
    }

    public static string IconKey(string kind) =>
        kind switch
        {
            "folder" => IconKeyFolder,
            "url" => IconKeyUrl,
            _ => IconKeyFile,
        };

    public static IReadOnlyList<string> TagLabels(IReadOnlyList<string>? tags) =>
        tags is { Count: > 0 } ? tags : Array.Empty<string>();
}
