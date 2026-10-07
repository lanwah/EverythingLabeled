using EverythingLabeled.Client;

namespace EverythingLabeled.ExplorerHost;

public static class ResourceTags
{
    public static async Task<ElResult<TagsPage>> GetTagsOrEmptyAsync(
        IElApi api,
        string uri,
        CancellationToken ct = default)
    {
        var result = await api.GetTagsAsync(uri, ct).ConfigureAwait(false);
        if (!result.IsSuccess
            && result.Error is { Code: "resource_not_found" })
        {
            return ElResult<TagsPage>.Ok(new TagsPage([], 0, 0, 0));
        }

        return result;
    }
}
