namespace EverythingLabeled.ExplorerHost;

/// <summary>
/// Default kind for a replacement address. http(s) is a page; a path with an
/// extension is a file; anything else is a folder. The user can still override.
/// </summary>
public static class ReplaceKindGuess
{
    public const string File = "file";
    public const string Folder = "folder";
    public const string Url = "url";

    public static string? Kind(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var text = raw.Trim();
        if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return Url;

        return HasExtension(LastSegment(text)) ? File : Folder;
    }

    public static string Hint(string? raw, string? selected)
    {
        var guessed = Kind(raw);
        if (guessed is null)
            return "输入地址后会预选类型，仍可修改。";

        var reason = guessed switch
        {
            Url => "以 http(s) 开头，已预选网页。",
            File => "有扩展名，已预选文件。",
            _ => "无扩展名，已预选文件夹。",
        };

        if (selected is not null && !string.Equals(selected, guessed, StringComparison.Ordinal))
            return reason + "当前选择是" + Label(selected) + "。";

        return reason + "识别不对可以改。";
    }

    public static string Label(string kind) => kind switch
    {
        Folder => "文件夹",
        Url => "网页",
        _ => "文件",
    };

    private static string LastSegment(string text)
    {
        var end = text.Length;
        while (end > 0 && text[end - 1] is '/' or '\\')
            end--;

        var start = end;
        while (start > 0 && text[start - 1] is not '/' and not '\\')
            start--;

        return text[start..end];
    }

    private static bool HasExtension(string name)
    {
        var dot = name.LastIndexOf('.');
        return dot > 0 && dot < name.Length - 1;
    }
}
