using EverythingLabeled.Client;
using EverythingLabeled.PathUri;

namespace EverythingLabeled.ExplorerHost;

/// <summary>Copy the source resource's tags onto a new address, then delete the source.</summary>
public static class ResourceReplace
{
    public const int AliasBatch = 100;

    public static async Task<ElResult<MetaData>> ExecuteAsync(
        IElApi api,
        Guid sourceId,
        IReadOnlyList<string> aliases,
        string newAddress,
        string kind,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(newAddress))
            return Fail(Copy.ReplaceAddressRequired);

        if (!PathUriCanonicalizer.TryCanonicalize(newAddress.Trim(), out var canonical)
            || !PathUriCanonicalizer.IsKindCompatible(canonical.Scheme, kind))
            return Fail(Copy.ReplaceKindMismatch);

        Guid? createdId = null;
        if (aliases.Count == 0)
        {
            var created = await api.TagAsync(canonical.Uri, kind, Array.Empty<string>(), false, ct)
                .ConfigureAwait(false);
            if (!created.IsSuccess)
                return ElResult<MetaData>.Fail(created.Error ?? new ElError(ElErrorCodes.BadRequest, Copy.ReplaceKindMismatch));
            createdId = created.Data!.Resource.Id;
        }
        else
        {
            for (var offset = 0; offset < aliases.Count; offset += AliasBatch)
            {
                var count = Math.Min(AliasBatch, aliases.Count - offset);
                var chunk = new string[count];
                for (var i = 0; i < count; i++)
                    chunk[i] = aliases[offset + i];

                var tagged = await api.TagAsync(canonical.Uri, kind, chunk, false, ct)
                    .ConfigureAwait(false);
                if (!tagged.IsSuccess)
                    return ElResult<MetaData>.Fail(tagged.Error ?? new ElError(ElErrorCodes.BadRequest, Copy.ReplaceKindMismatch));
                createdId = tagged.Data!.Resource.Id;
            }
        }

        if (createdId == sourceId)
            return Fail(Copy.ReplaceSameResource);

        var deleted = await api.DeleteResourceAsync(sourceId, ct).ConfigureAwait(false);
        if (!deleted.IsSuccess)
        {
            var detail = deleted.Error?.Message;
            return Fail(string.IsNullOrWhiteSpace(detail)
                ? Copy.ReplaceDeleteFailed
                : Copy.ReplaceDeleteFailed + detail);
        }

        return deleted;
    }

    private static ElResult<MetaData> Fail(string message) =>
        ElResult<MetaData>.Fail(new ElError(ElErrorCodes.BadRequest, message));
}
