namespace EverythingLabeled.ExplorerHost;

/// <summary>Pure helpers for find-box last-token suggest / replacement.</summary>
public static class FindQueryEdit
{
    /// <summary>
    /// Incomplete last token for suggest. Null when blank or the text ends with whitespace
    /// (user finished a token).
    /// </summary>
    public static string? LastTokenForSuggest(string? raw)
    {
        if (raw is null)
            return null;

        if (raw.Length == 0 || char.IsWhiteSpace(raw[^1]))
            return null;

        var span = raw.AsSpan().TrimEnd();
        if (span.Length == 0)
            return null;

        var start = span.Length;
        while (start > 0 && !char.IsWhiteSpace(span[start - 1]))
            start--;

        var token = span[start..].ToString();
        return SuggestQuery.Normalize(token);
    }

    /// <summary>Replace the incomplete last token with <paramref name="alias"/> and a trailing space.</summary>
    public static string ReplaceLastToken(string? raw, string alias)
    {
        ArgumentException.ThrowIfNullOrEmpty(alias);

        if (string.IsNullOrWhiteSpace(raw))
            return alias + " ";

        if (char.IsWhiteSpace(raw[^1]))
            return raw.TrimEnd() + " " + alias + " ";

        var end = raw.Length;
        var start = end;
        while (start > 0 && !char.IsWhiteSpace(raw[start - 1]))
            start--;

        return raw[..start] + alias + " ";
    }
}
