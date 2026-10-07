using EverythingLabeled.Client;
using EverythingLabeled.PathUri;

namespace EverythingLabeled.ExplorerHost;

public sealed record SelectionItem(
    string RawPath,
    string? CanonicalUri,
    string? Kind,
    bool Included,
    string? ExcludeReason);

public sealed record SelectionResult(
    IReadOnlyList<SelectionItem> Items,
    int IgnoredUnsupportedCount,
    int DedupedCount,
    bool RejectedOverLimit);

public static class SelectionNormalizer
{
    public const int MaxItems = 100;

    public static SelectionResult Normalize(IEnumerable<string> rawPaths, HostMode mode)
    {
        var items = new List<SelectionItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var ignored = 0;

        foreach (var raw in rawPaths)
        {
            if (!TryToFileUri(raw, out var canonicalUri))
            {
                ignored++;
                continue;
            }

            if (!seen.Add(canonicalUri))
                continue;

            if (mode == HostMode.Tag)
            {
                var kindResult = FileTarget.ResolveKindForTag(canonicalUri);
                if (!kindResult.IsSuccess)
                {
                    items.Add(new SelectionItem(
                        raw,
                        canonicalUri,
                        Kind: null,
                        Included: false,
                        ExcludeReason: Copy.PathNotFoundOrBroken));
                    continue;
                }

                items.Add(new SelectionItem(
                    raw,
                    canonicalUri,
                    Kind: kindResult.Data,
                    Included: true,
                    ExcludeReason: null));
                continue;
            }

            items.Add(new SelectionItem(
                raw,
                canonicalUri,
                Kind: null,
                Included: true,
                ExcludeReason: null));
        }

        var deduped = items.Count;
        return new SelectionResult(
            items,
            ignored,
            deduped,
            RejectedOverLimit: deduped > MaxItems);
    }

    private static bool TryToFileUri(string raw, out string canonicalUri)
    {
        canonicalUri = null!;
        if (!PathUriCanonicalizer.TryCanonicalize(raw, out var canonical))
            return false;

        if (!string.Equals(canonical.Scheme, "file", StringComparison.Ordinal))
            return false;

        canonicalUri = canonical.Uri;
        return true;
    }
}
