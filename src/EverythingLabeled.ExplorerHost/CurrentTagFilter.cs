namespace EverythingLabeled.ExplorerHost;

public readonly record struct CurrentTagFilterView(
    string Status,
    bool ShowCount,
    string CountText,
    bool ShowMiss,
    bool ShowEmpty);

public static class CurrentTagFilter
{
    public static string? ActiveQuery(string? query)
    {
        if (query is null)
            return null;

        var trimmed = query.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    public static bool IsFiltering(string? query) => ActiveQuery(query) is not null;

    public static bool Matches(string label, string alias, string nameInitials, string? query)
    {
        var active = ActiveQuery(query);
        if (active is null)
            return true;

        if (label.Contains(active, StringComparison.OrdinalIgnoreCase))
            return true;

        var prefix = active.ToLowerInvariant();
        if (alias.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return true;

        return nameInitials.StartsWith(prefix, StringComparison.Ordinal);
    }

    public static string CountText(int shown, int total) => $"{shown} / {total}";

    public static CurrentTagFilterView Describe(int total, int shown, string? query)
    {
        var filtering = IsFiltering(query);
        if (total == 0)
        {
            return new CurrentTagFilterView(
                Copy.UntagNoCurrentTags,
                ShowCount: false,
                CountText: CountText(shown, total),
                ShowMiss: false,
                ShowEmpty: true);
        }

        if (filtering && shown == 0)
        {
            return new CurrentTagFilterView(
                Copy.UntagFilterMiss,
                ShowCount: true,
                CountText: CountText(shown, total),
                ShowMiss: true,
                ShowEmpty: false);
        }

        if (filtering)
        {
            return new CurrentTagFilterView(
                Copy.UntagFilterMatch(shown, total),
                ShowCount: true,
                CountText: CountText(shown, total),
                ShowMiss: false,
                ShowEmpty: false);
        }

        return new CurrentTagFilterView(
            Copy.UntagChipHint,
            ShowCount: false,
            CountText: CountText(shown, total),
            ShowMiss: false,
            ShowEmpty: false);
    }
}
