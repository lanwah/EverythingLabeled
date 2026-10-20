namespace EverythingLabeled.ExplorerHost;

/// <summary>Find footer status when no chips are committed yet.</summary>
public static class FindStatusText
{
    /// <summary>
    /// Idle 「预览 0 条」, or 「建议 N 条」 while the draft drives suggest (incl. 拼首).
    /// </summary>
    public static string IdleOrSuggest(string? draftToken, int suggestCount) =>
        draftToken is null ? Copy.PreviewCount(0) : Copy.SuggestCount(suggestCount);
}
