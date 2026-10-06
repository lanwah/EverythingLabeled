using System.Globalization;
using EverythingLabeled.PathUri;

namespace EverythingLabeled.Client;

public readonly record struct ElQueryExpand(string? Expand, int? MaxDepth, int? ExpandedTagLimit);

public static class ElRequestRules
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

    public static ElResult<IReadOnlyList<string>> ParseAliases(IReadOnlyList<string>? aliases)
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

    public static ElResult<string> ResolveKindForTag(CanonicalTarget target)
    {
        if (target.Scheme is "http" or "https")
            return ElResult<string>.Ok("url");
        return FileTarget.ResolveKindForTag(target.Uri);
    }

    public static ElResult<string> ParseRequiredText(string? raw, string emptyMessage)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return ElResult<string>.Fail(new ElError(ElErrorCodes.BadRequest, emptyMessage));

        return ElResult<string>.Ok(raw.Trim());
    }

    public static ElResult<int> ParseLimit(int? value, int defaultValue)
    {
        if (value is null)
            return ElResult<int>.Ok(defaultValue);
        return ValidateLimit(value.Value);
    }

    public static ElResult<int> ParseLimit(string? raw, int defaultValue)
    {
        if (raw is null)
            return ElResult<int>.Ok(defaultValue);
        if (!TryParseNonNegative(raw, out var value))
            return LimitFail();
        return ValidateLimit(value);
    }

    public static ElResult<int> ParseOffset(int? value)
    {
        if (value is null)
            return ElResult<int>.Ok(0);
        return ValidateOffset(value.Value);
    }

    public static ElResult<int> ParseOffset(string? raw)
    {
        if (raw is null)
            return ElResult<int>.Ok(0);
        if (!TryParseNonNegative(raw, out var value))
            return OffsetFail();
        return ValidateOffset(value);
    }

    public static ElResult<ElQueryExpand> ParseExpand(string? expand, int? maxDepth, int? expandedTagLimit)
    {
        if (maxDepth is < 0 || expandedTagLimit is < 0)
            return ExpandGateParseFail();

        return FinishExpand(expand, maxDepth is not null || expandedTagLimit is not null, maxDepth, expandedTagLimit);
    }

    public static ElResult<ElQueryExpand> ParseExpand(string? expand, string? maxDepthRaw, string? expandedTagLimitRaw)
    {
        var hasGate = maxDepthRaw is not null || expandedTagLimitRaw is not null;
        if (string.IsNullOrEmpty(expand))
            return FinishExpand(expand, hasGate, null, null);

        if (expand != "child")
        {
            return ElResult<ElQueryExpand>.Fail(
                new ElError(ElErrorCodes.BadRequest, "expand only allows child."));
        }

        if (!TryParseOptionalNonNegative(maxDepthRaw, out var maxDepth) ||
            !TryParseOptionalNonNegative(expandedTagLimitRaw, out var expandedTagLimit))
        {
            return ExpandGateParseFail();
        }

        return ElResult<ElQueryExpand>.Ok(new ElQueryExpand("child", maxDepth, expandedTagLimit));
    }

    private static ElResult<ElQueryExpand> FinishExpand(
        string? expand,
        bool hasGate,
        int? maxDepth,
        int? expandedTagLimit)
    {
        if (string.IsNullOrEmpty(expand))
        {
            if (hasGate)
                return ExpandRequiresExpandFail();
            return ElResult<ElQueryExpand>.Ok(new ElQueryExpand(null, null, null));
        }

        if (expand != "child")
        {
            return ElResult<ElQueryExpand>.Fail(
                new ElError(ElErrorCodes.BadRequest, "expand only allows child."));
        }

        return ElResult<ElQueryExpand>.Ok(new ElQueryExpand("child", maxDepth, expandedTagLimit));
    }

    private static ElResult<int> ValidateLimit(int value)
    {
        if (value is < 1 or > 1024)
            return LimitFail();
        return ElResult<int>.Ok(value);
    }

    private static ElResult<int> ValidateOffset(int value)
    {
        if (value < 0)
            return OffsetFail();
        return ElResult<int>.Ok(value);
    }

    private static ElResult<int> LimitFail() =>
        ElResult<int>.Fail(new ElError(ElErrorCodes.BadRequest, "limit must be an integer from 1 to 1024."));

    private static ElResult<int> OffsetFail() =>
        ElResult<int>.Fail(new ElError(ElErrorCodes.BadRequest, "offset must be a non-negative integer."));

    private static ElResult<ElQueryExpand> ExpandRequiresExpandFail() =>
        ElResult<ElQueryExpand>.Fail(
            new ElError(ElErrorCodes.BadRequest, "max_depth and expanded_tag_limit require expand."));

    private static ElResult<ElQueryExpand> ExpandGateParseFail() =>
        ElResult<ElQueryExpand>.Fail(
            new ElError(ElErrorCodes.BadRequest, "max_depth and expanded_tag_limit must be non-negative integers."));

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
