namespace EverythingLabeled.ExplorerHost;

/// <summary>Pure helpers for Find result row title / subtitle / glyph.</summary>
public static class FindResultPresentation
{
    // Segoe MDL2 Assets. E8B7 is named Folder but draws as a vertical page,
    // so rows use FolderHorizontal (F12B) to separate folders from documents.
    public const string GlyphFolder = "\uF12B";
    public const string GlyphFile = "\uE8A5";
    public const string GlyphUrl = "\uE774";

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

    public static string Glyph(string kind) =>
        kind switch
        {
            "folder" => GlyphFolder,
            "url" => GlyphUrl,
            _ => GlyphFile,
        };
}
