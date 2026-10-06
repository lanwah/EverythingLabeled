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

}
