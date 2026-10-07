namespace EverythingLabeled.ExplorerHost;

/// <summary>Pure helpers for suggest input (debounce delay is applied by UI).</summary>
public static class SuggestQuery
{
    public const int DebounceMs = 250;
    public const int Limit = 10;

    /// <summary>
    /// Trim and require length ≥ 1. Returns null when suggest should not run.
    /// </summary>
    public static string? Normalize(string? raw)
    {
        if (raw is null)
            return null;

        var trimmed = raw.Trim();
        return trimmed.Length >= 1 ? trimmed : null;
    }
}
