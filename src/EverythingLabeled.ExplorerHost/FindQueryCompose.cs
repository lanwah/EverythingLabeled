namespace EverythingLabeled.ExplorerHost;

/// <summary>Pure helpers: chip tags → query string (AND spaces / OR keyword).</summary>
public static class FindQueryCompose
{
    public static string? Compose(IEnumerable<string> tags, bool useOr)
    {
        var parts = tags
            .Select(t => t?.Trim())
            .Where(t => !string.IsNullOrEmpty(t))
            .Cast<string>()
            .ToList();

        if (parts.Count == 0)
            return null;

        return useOr ? string.Join(" OR ", parts) : string.Join(" ", parts);
    }

    public static bool TryAdd(IList<string> tags, string? raw)
    {
        var alias = SuggestQuery.Normalize(raw);
        if (alias is null)
            return false;

        if (tags.Any(t => string.Equals(t, alias, StringComparison.OrdinalIgnoreCase)))
            return false;

        tags.Add(alias);
        return true;
    }
}
