namespace EverythingLabeled.ExplorerHost;

/// <summary>Which Find panel list is visible and how suggest is sized.</summary>
public enum FindSuggestPlacement
{
    Hidden,
    /// <summary>Above preview hits when chips exist; capped height.</summary>
    Compact,
    /// <summary>No chips yet — suggest fills the main list area.</summary>
    Main,
}

public static class FindListLayout
{
    public const double CompactSuggestMaxHeight = 140;

    public static (FindSuggestPlacement Suggest, bool ShowPreview) Resolve(
        bool hasDraftToken,
        int suggestCount,
        int chipCount,
        bool hasPreviewQuery)
    {
        var suggestOn = hasDraftToken && suggestCount > 0;

        if (chipCount == 0 && suggestOn)
            return (FindSuggestPlacement.Main, false);

        if (suggestOn && chipCount > 0)
            return (FindSuggestPlacement.Compact, hasPreviewQuery);

        if (hasPreviewQuery)
            return (FindSuggestPlacement.Hidden, true);

        return (FindSuggestPlacement.Hidden, false);
    }
}
