using EverythingLabeled.Client;

namespace EverythingLabeled.ExplorerHost;

/// <summary>Pure helpers for suggest input (debounce delay is applied by UI).</summary>
public static class SuggestQuery
{
    public const int DebounceMs = 250;
    /// <summary>缺省拼首条数；实际请求以 <c>config.json</c> 的 <c>suggestLimit</c> 为准。</summary>
    public const int DefaultLimit = ElConfigLoader.DefaultSuggestLimit;

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
