using EverythingLabeled.Service.Data;
using EverythingLabeled.Service.Data.Entities;
using EverythingLabeled.Service.Services.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace EverythingLabeled.Service.Services.Query;

public sealed class QueryExecutor(ElDbContext db)
{
    public async Task<Paged<QueryHitDto>> ExecuteAsync(
        ExpandedQuery expanded,
        Paging paging,
        bool includeTotal,
        CancellationToken ct)
    {
        // Filter in SQL via ResourceTags Any/Contains (SQLite translates closed-over Guid[]).
        // Clause OR uses Union. Sort/skip/take stay in-memory because SQLite cannot ORDER BY
        // DateTimeOffset (same constraint as ResourceService.ListAsync).
        var filtered = FilterInSql(expanded.Clauses);
        var matched = (await filtered
                .Select(r => new { r.Id, r.Uri, r.Kind, r.DisplayName, r.CreatedAt })
                .ToListAsync(ct))
            .OrderBy(r => r.CreatedAt)
            .ThenBy(r => r.Id)
            .ToList();

        var page = matched
            .Skip(paging.Offset)
            .Take(paging.Limit)
            .Select(r => new QueryHitDto(r.Id, r.Uri, r.Kind, r.DisplayName))
            .ToList();

        return new Paged<QueryHitDto>
        {
            Items = page,
            Offset = paging.Offset,
            Limit = paging.Limit,
            Total = includeTotal ? matched.Count : 0,
        };
    }

    public async Task<IReadOnlyList<QueryHitDto>> AttachResourceTagsAsync(
        IReadOnlyList<QueryHitDto> hits,
        CancellationToken ct)
    {
        if (hits.Count == 0)
            return hits;

        var ids = hits.Select(h => h.Id).ToArray();
        var bindings = await db.ResourceTags.AsNoTracking()
            .Where(rt => ids.Contains(rt.ResourceId))
            .Include(rt => rt.Tag)
            .ThenInclude(t => t.Aliases)
            .ToListAsync(ct);

        bindings = bindings
            .OrderBy(rt => rt.CreatedAt)
            .ThenBy(rt => rt.TagId)
            .ToList();

        var labelsByResource = bindings
            .GroupBy(rt => rt.ResourceId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<string>)g.Select(rt => QueryHitTagLabels.PrimaryAlias(rt.Tag)).ToList());

        return hits
            .Select(h => labelsByResource.TryGetValue(h.Id, out var labels)
                ? h with { Tags = labels }
                : h)
            .ToList();
    }

    private IQueryable<Resource> FilterInSql(IReadOnlyList<ExpandedClause> clauses)
    {
        IQueryable<Resource>? combined = null;
        foreach (var clause in clauses)
        {
            if (clause.Positives.Count == 0)
                continue;

            var clauseQuery = ApplyClause(db.Resources.AsNoTracking(), clause);
            combined = combined is null ? clauseQuery : combined.Union(clauseQuery);
        }

        return combined ?? db.Resources.AsNoTracking().Where(_ => false);
    }

    private static IQueryable<Resource> ApplyClause(IQueryable<Resource> query, ExpandedClause clause)
    {
        foreach (var positive in clause.Positives)
        {
            // Local array so EF Core closes over a constant collection (translates to SQL IN).
            var set = positive.ToArray();
            query = query.Where(r => r.ResourceTags.Any(rt => set.Contains(rt.TagId)));
        }

        if (clause.Negatives.Count > 0)
        {
            var neg = clause.Negatives.ToArray();
            query = query.Where(r => !r.ResourceTags.Any(rt => neg.Contains(rt.TagId)));
        }

        return query;
    }
}
