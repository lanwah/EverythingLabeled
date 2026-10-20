using System.Text;
using System.Text.Json;
using EverythingLabeled.Service.Services.Abstractions;
using EverythingLabeled.Service.Services.Query;
using EverythingLabeled.Service.Services.Tags;

namespace EverythingLabeled.Service.Services.Caching;

public static class CacheEntrySizer
{
    private const int EntryOverhead = 192;
    private const int StringOverhead = 24;
    private const int GuidSize = 16;
    private const int PagedOverhead = 48;
    private const int IntFieldSize = 8;

    public static long Estimate<T>(string key, T value) where T : class =>
        EntryOverhead + Utf8ByteCount(key) + EstimateValue(value);

    private static long EstimateValue<T>(T value) where T : class
    {
        return value switch
        {
            string s => StringOverhead + Utf8ByteCount(s),
            QueryHitDto hit => EstimateQueryHit(hit),
            SuggestItemDto suggest => EstimateSuggestItem(suggest),
            Paged<QueryHitDto> pagedHits => EstimatePaged(pagedHits.Items, pagedHits.Offset, pagedHits.Limit, pagedHits.Total),
            Paged<SuggestItemDto> pagedSuggest => EstimatePaged(pagedSuggest.Items, pagedSuggest.Offset, pagedSuggest.Limit, pagedSuggest.Total),
            QueryResultDto result => EstimatePaged(result.Items, result.Offset, result.Limit, result.Total),
            PreviewResultDto preview => EstimatePaged(preview.Items, preview.Offset, preview.Limit, total: 0),
            _ => EstimateFallback(value)
        };
    }

    private static long EstimateQueryHit(QueryHitDto hit)
    {
        long size = GuidSize
            + EstimateString(hit.Uri)
            + EstimateString(hit.Kind)
            + EstimateString(hit.DisplayName);
        if (hit.Tags is not null)
        {
            foreach (var tag in hit.Tags)
                size += EstimateString(tag);
        }

        return size;
    }

    private static long EstimateSuggestItem(SuggestItemDto item) =>
        GuidSize
        + EstimateString(item.Alias)
        + EstimateString(item.Name)
        + EstimateString(item.Namespace)
        + EstimateString(item.NameInitials);

    private static long EstimatePaged<T>(IReadOnlyList<T> items, int offset, int limit, int total) where T : class
    {
        long size = PagedOverhead + IntFieldSize + IntFieldSize + IntFieldSize;
        foreach (var item in items)
            size += EstimateValue(item);
        return size;
    }

    private static long EstimateString(string? value) =>
        StringOverhead + Utf8ByteCount(value ?? string.Empty);

    private static long EstimateFallback<T>(T value) where T : class
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        return Math.Max(256, bytes.Length * 2L);
    }

    private static int Utf8ByteCount(string value) =>
        Encoding.UTF8.GetByteCount(value);
}
