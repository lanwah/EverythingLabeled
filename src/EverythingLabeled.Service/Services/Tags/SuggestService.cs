using EverythingLabeled.Service.Data;
using EverythingLabeled.Service.Services.Abstractions;
using EverythingLabeled.Service.Services.Caching;
using Microsoft.EntityFrameworkCore;

namespace EverythingLabeled.Service.Services.Tags;

public sealed class SuggestService(ElDbContext db, ITagQueryCache cache)
{
    public async Task<ServiceResult<Paged<SuggestItemDto>>> SuggestAsync(
        string q,
        Paging paging,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return ServiceResult<Paged<SuggestItemDto>>.Fail(
                ErrorCodes.BadRequest,
                "Query is required.");
        }

        if (paging.Offset > 0)
        {
            return ServiceResult<Paged<SuggestItemDto>>.Fail(
                ErrorCodes.BadRequest,
                "Suggest does not support offset greater than zero.");
        }

        var normalizedPrefix = AliasRules.Normalize(q);
        var versionAtKey = await DatabaseInitializer.GetDataVersionAsync(db, ct);
        var key = QueryCacheKeyFactory.ForSuggest(versionAtKey, normalizedPrefix, paging.Limit);
        var page = await QueryCachePipeline.GetOrLoadAsync(
            cache,
            key,
            versionAtKey,
            token => DatabaseInitializer.GetDataVersionAsync(db, token),
            token => LoadAsync(normalizedPrefix, paging.Limit, token),
            ct);
        return ServiceResult<Paged<SuggestItemDto>>.Ok(page);
    }

    private async Task<Paged<SuggestItemDto>> LoadAsync(string normalizedPrefix, int limit, CancellationToken ct)
    {
        var like = LikePattern.Prefix(normalizedPrefix);
        var tags = TagListOrder.Apply(await db.Tags.AsNoTracking()
            .Include(t => t.Aliases)
            .Where(t =>
                t.Aliases.Any(a => EF.Functions.Like(a.AliasNormalized, like, LikePattern.EscapeCharacter))
                || EF.Functions.Like(t.NameInitials, like, LikePattern.EscapeCharacter))
            .ToListAsync(ct));

        var items = new List<SuggestItemDto>();
        foreach (var tag in tags)
        {
            var matchingAliases = tag.Aliases
                .Where(a => a.AliasNormalized.StartsWith(normalizedPrefix, StringComparison.Ordinal))
                .ToList();
            if (matchingAliases.Count > 0)
            {
                foreach (var alias in matchingAliases)
                {
                    items.Add(new SuggestItemDto(
                        tag.Id, alias.Alias, tag.Name, tag.Namespace, tag.NameInitials));
                }
            }
            else
            {
                var display = tag.Aliases.FirstOrDefault(a => a.Alias == tag.Name)?.Alias
                    ?? tag.Aliases.Select(a => a.Alias).FirstOrDefault()
                    ?? tag.Name;
                items.Add(new SuggestItemDto(tag.Id, display, tag.Name, tag.Namespace, tag.NameInitials));
            }
        }

        return new Paged<SuggestItemDto>
        {
            Items = items.Take(limit).ToList(),
            Offset = 0,
            Limit = limit,
            Total = items.Count,
        };
    }
}
