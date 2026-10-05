using EverythingLabeled.Service.Data;
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
        // SQLite cannot translate complex nested Any/All over in-memory sets reliably;
        // load tag bindings then filter/sort in memory (same pattern as ResourceService list).
        var resources = await db.Resources.AsNoTracking()
            .Select(r => new
            {
                r.Id,
                r.Uri,
                r.Kind,
                r.DisplayName,
                r.CreatedAt,
                TagIds = r.ResourceTags.Select(rt => rt.TagId).ToList(),
            })
            .ToListAsync(ct);

        var matched = resources
            .Where(r => MatchesAnyClause(r.TagIds, expanded.Clauses))
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

    private static bool MatchesAnyClause(IReadOnlyList<Guid> tagIds, IReadOnlyList<ExpandedClause> clauses)
    {
        var owned = tagIds as HashSet<Guid> ?? tagIds.ToHashSet();
        foreach (var clause in clauses)
        {
            if (MatchesClause(owned, clause))
                return true;
        }

        return false;
    }

    private static bool MatchesClause(HashSet<Guid> owned, ExpandedClause clause)
    {
        foreach (var positive in clause.Positives)
        {
            if (!positive.Any(owned.Contains))
                return false;
        }

        if (clause.Negatives.Count > 0 && clause.Negatives.Any(owned.Contains))
            return false;

        return clause.Positives.Count > 0;
    }
}
