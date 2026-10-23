using EverythingLabeled.Service.Services.Abstractions;

namespace EverythingLabeled.Service.Services.Resources;

/// <summary>Resource <c>kind</c> values and query/preview <c>?kind=</c> filter tokens.</summary>
public static class ResourceKinds
{
    public const string File = "file";
    public const string Folder = "folder";
    public const string Url = "url";

    /// <summary>Query filter sentinel: no kind restriction (same as omitting <c>kind</c>).</summary>
    public const string All = "all";

    public static bool IsKnown(string kind) =>
        kind is File or Folder or Url;

    /// <summary>
    /// Parse query/preview <c>kind</c>: omit / blank / <c>all</c> → no filter (<c>null</c>);
    /// <c>file</c>|<c>folder</c>|<c>url</c> → that kind; otherwise validation error.
    /// </summary>
    public static ServiceResult<string?> ParseQueryFilter(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return ServiceResult<string?>.Ok(null);

        var kind = raw.Trim();
        if (kind.Equals(All, StringComparison.Ordinal))
            return ServiceResult<string?>.Ok(null);

        if (!IsKnown(kind))
        {
            return ServiceResult<string?>.Fail(
                ErrorCodes.BadRequest,
                "Invalid kind.",
                new { reason = "param_invalid" });
        }

        return ServiceResult<string?>.Ok(kind);
    }

    /// <summary>Opaque cache-key token; always one of <c>all</c>|<c>file</c>|<c>folder</c>|<c>url</c>.</summary>
    public static string CacheKeyToken(string? kindFilter) => kindFilter ?? All;
}
