using EverythingLabeled.Service.Data;
using EverythingLabeled.Service.Services.Abstractions;
using EverythingLabeled.Service.Services.Relations;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace EverythingLabeled.Service.Services.Query;

public sealed class QueryExpander(ElDbContext db)
{
    public async Task<ServiceResult<ExpandedQuery>> ExpandAsync(
        ParsedQuery parsed,
        IReadOnlyList<ResolvedTerm> resolved,
        int maxDepth,
        int expandedTagLimit,
        CancellationToken ct)
    {
        var childEdges = await db.TagRelations.AsNoTracking()
            .Where(r => r.Kind == RelationGraph.Child)
            .Select(r => new { r.FromTagId, r.ToTagId })
            .ToListAsync(ct);

        var children = childEdges
            .GroupBy(e => e.FromTagId)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ToTagId).ToList());

        var expansions = new Dictionary<(int Clause, bool Positive, string Alias), HashSet<Guid>>();
        var all = new HashSet<Guid>();
        var truncated = false;

        foreach (var term in resolved)
        {
            var set = ExpandOne(term.TagId, maxDepth, children, ref truncated);
            expansions[(term.ClauseIndex, term.IsPositive, term.AliasNormalized)] = set;
            foreach (var id in set)
                all.Add(id);
        }

        if (all.Count > expandedTagLimit)
        {
            Log.Information(
                "Query {Reason} expandedTagCount={Count} expandedTagLimit={Limit} normalized_query={NormalizedQuery}",
                "expansion_tag_limit_exceeded",
                all.Count,
                expandedTagLimit,
                parsed.Normalized);
            return ServiceResult<ExpandedQuery>.Fail(
                ErrorCodes.ExpansionLimitExceeded,
                "Expanded tag limit exceeded.",
                new { reason = "expansion_tag_limit_exceeded" });
        }

        if (truncated)
        {
            Log.Information(
                "Query {Reason} maxDepth={MaxDepth} normalized_query={NormalizedQuery}",
                "max_depth_truncated",
                maxDepth,
                parsed.Normalized);
        }

        var clauses = new List<ExpandedClause>(parsed.Clauses.Count);
        for (var i = 0; i < parsed.Clauses.Count; i++)
        {
            var clause = parsed.Clauses[i];
            var positives = new List<IReadOnlySet<Guid>>();
            foreach (var alias in clause.PositiveAliasNormalized)
            {
                if (expansions.TryGetValue((i, true, alias), out var set))
                    positives.Add(set);
            }

            var negatives = new HashSet<Guid>();
            foreach (var alias in clause.NegativeAliasNormalized)
            {
                if (expansions.TryGetValue((i, false, alias), out var set))
                    negatives.UnionWith(set);
            }

            clauses.Add(new ExpandedClause
            {
                Positives = positives,
                Negatives = negatives,
            });
        }

        return ServiceResult<ExpandedQuery>.Ok(new ExpandedQuery
        {
            Normalized = parsed.Normalized,
            Clauses = clauses,
            AllExpandedTagIds = all,
            Truncated = truncated,
        });
    }

    private static HashSet<Guid> ExpandOne(
        Guid root,
        int maxDepth,
        Dictionary<Guid, List<Guid>> children,
        ref bool truncated)
    {
        var result = new HashSet<Guid> { root };
        if (maxDepth <= 0)
            return result;

        var queue = new Queue<(Guid Id, int Depth)>();
        queue.Enqueue((root, 0));
        var visited = new HashSet<Guid> { root };

        while (queue.Count > 0)
        {
            var (id, depth) = queue.Dequeue();
            if (!children.TryGetValue(id, out var kids))
                continue;

            foreach (var kid in kids)
            {
                var nextDepth = depth + 1;
                if (nextDepth > maxDepth)
                {
                    truncated = true;
                    continue;
                }

                if (!visited.Add(kid))
                    continue;

                result.Add(kid);
                queue.Enqueue((kid, nextDepth));
            }
        }

        return result;
    }
}
