using System.Globalization;
using EverythingLabeled.Client;
using EverythingLabeled.PathUri;

namespace EverythingLabeled.Cli;

internal static class CommandSupport
{
    public static ElResult<CanonicalTarget> ParseTarget(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) ||
            !PathUriCanonicalizer.TryCanonicalize(raw, out var canonical))
        {
            return ElResult<CanonicalTarget>.Fail(
                new ElError(ElErrorCodes.BadRequest, "Target is not a valid path or URI."));
        }

        return ElResult<CanonicalTarget>.Ok(canonical);
    }

    public static ElResult<IReadOnlyList<string>> ParseAliases(string[]? aliases)
    {
        var trimmed = new List<string>();
        if (aliases is not null)
        {
            foreach (var alias in aliases)
                trimmed.Add(alias.Trim());
        }

        if (trimmed.Count is < 1 or > 100)
        {
            return ElResult<IReadOnlyList<string>>.Fail(
                new ElError(ElErrorCodes.BadRequest, "Provide between 1 and 100 aliases."));
        }

        foreach (var alias in trimmed)
        {
            if (alias.Length == 0)
            {
                return ElResult<IReadOnlyList<string>>.Fail(
                    new ElError(ElErrorCodes.BadRequest, "Alias must not be empty."));
            }
        }

        return ElResult<IReadOnlyList<string>>.Ok(trimmed);
    }

    public static ElResult<string> ResolveKind(CanonicalTarget target)
    {
        if (target.Scheme is "http" or "https")
            return ElResult<string>.Ok("url");
        return FileTarget.ResolveKindForTag(target.Uri);
    }

    public static ElResult<string> ParseRequiredText(string? raw, string emptyMessage)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return ElResult<string>.Fail(new ElError(ElErrorCodes.BadRequest, emptyMessage));
        }

        return ElResult<string>.Ok(raw.Trim());
    }

    public static ElResult<int> ParseLimit(string? raw, int defaultValue)
    {
        if (raw is null)
            return ElResult<int>.Ok(defaultValue);
        if (!TryParseNonNegative(raw, out var value) || value is < 1 or > 1024)
        {
            return ElResult<int>.Fail(
                new ElError(ElErrorCodes.BadRequest, "limit must be an integer from 1 to 1024."));
        }

        return ElResult<int>.Ok(value);
    }

    public static ElResult<int> ParseOffset(string? raw)
    {
        if (raw is null)
            return ElResult<int>.Ok(0);
        if (!TryParseNonNegative(raw, out var value))
        {
            return ElResult<int>.Fail(
                new ElError(ElErrorCodes.BadRequest, "offset must be a non-negative integer."));
        }

        return ElResult<int>.Ok(value);
    }

    public static ElResult<QueryExpand> ParseExpand(
        string? expand,
        string? maxDepthRaw,
        string? expandedTagLimitRaw)
    {
        var hasGate = maxDepthRaw is not null || expandedTagLimitRaw is not null;
        if (string.IsNullOrEmpty(expand))
        {
            if (hasGate)
            {
                return ElResult<QueryExpand>.Fail(
                    new ElError(ElErrorCodes.BadRequest, "--max-depth and --expanded-tag-limit require --expand."));
            }

            return ElResult<QueryExpand>.Ok(new QueryExpand(null, null, null));
        }

        if (expand != "child")
        {
            return ElResult<QueryExpand>.Fail(
                new ElError(ElErrorCodes.BadRequest, "--expand only allows child."));
        }

        if (!TryParseOptionalNonNegative(maxDepthRaw, out var maxDepth) ||
            !TryParseOptionalNonNegative(expandedTagLimitRaw, out var expandedTagLimit))
        {
            return ElResult<QueryExpand>.Fail(
                new ElError(ElErrorCodes.BadRequest, "Gate options must be non-negative integers."));
        }

        return ElResult<QueryExpand>.Ok(new QueryExpand("child", maxDepth, expandedTagLimit));
    }

    private static bool TryParseNonNegative(string raw, out int value)
    {
        value = 0;
        return int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= 0;
    }

    private static bool TryParseOptionalNonNegative(string? raw, out int? value)
    {
        value = null;
        if (raw is null)
            return true;
        if (!TryParseNonNegative(raw, out var parsed))
            return false;
        value = parsed;
        return true;
    }
}

internal readonly record struct QueryExpand(string? Expand, int? MaxDepth, int? ExpandedTagLimit);
