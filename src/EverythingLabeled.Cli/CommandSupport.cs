using EverythingLabeled.Client;
using EverythingLabeled.PathUri;

namespace EverythingLabeled.Cli;

internal static class CommandSupport
{
    public static ElResult<CanonicalTarget> ParseTarget(string? raw) =>
        ElRequestRules.ParseTarget(raw);

    public static ElResult<IReadOnlyList<string>> ParseAliases(string[]? aliases) =>
        ElRequestRules.ParseAliases(aliases);

    public static ElResult<string> ResolveKind(CanonicalTarget target) =>
        ElRequestRules.ResolveKindForTag(target);

    public static ElResult<string> ParseRequiredText(string? raw, string emptyMessage) =>
        ElRequestRules.ParseRequiredText(raw, emptyMessage);

    public static ElResult<int> ParseLimit(string? raw, int defaultValue) =>
        ElRequestRules.ParseLimit(raw, defaultValue);

    public static ElResult<int> ParseOffset(string? raw) =>
        ElRequestRules.ParseOffset(raw);

    public static ElResult<ElQueryExpand> ParseExpand(
        string? expand,
        string? maxDepthRaw,
        string? expandedTagLimitRaw) =>
        ElRequestRules.ParseExpand(expand, maxDepthRaw, expandedTagLimitRaw);
}
