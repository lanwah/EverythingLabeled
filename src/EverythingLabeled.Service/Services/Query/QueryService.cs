using System.Globalization;
using EverythingLabeled.Service.Data;
using EverythingLabeled.Service.Services.Abstractions;
using EverythingLabeled.Service.Services.Caching;
using EverythingLabeled.Service.Services.Relations;
using EverythingLabeled.Service.Services.Resources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;

namespace EverythingLabeled.Service.Services.Query;

public sealed class QueryService(
    ElDbContext db,
    QueryCachePipeline pipeline,
    IOptions<CacheOptions> cacheOptions,
    QueryParser parser,
    QueryExpander expander,
    QueryExecutor executor,
    IDataVersionReader dataVersion)
{
    public async Task<ServiceResult<QueryResultDto>> QueryAsync(QueryRequest req, CancellationToken ct)
    {
        var r = await RunAsync(req, op: "query", includeTotal: true, ct);
        if (!r.IsSuccess)
            return ServiceResult<QueryResultDto>.Fail(r.Code!, r.Message!, r.Details);
        return ServiceResult<QueryResultDto>.Ok(
            new QueryResultDto(r.Data!.Items, r.Data.Offset, r.Data.Limit, r.Data.Total),
            r.Message);
    }

    public async Task<ServiceResult<PreviewResultDto>> PreviewAsync(QueryRequest req, CancellationToken ct)
    {
        var r = await RunAsync(req, op: "preview", includeTotal: false, ct);
        if (!r.IsSuccess)
            return ServiceResult<PreviewResultDto>.Fail(r.Code!, r.Message!, r.Details);
        var page = r.Data!;
        var items = page.Items.Count == 0
            ? page.Items
            : await executor.AttachResourceTagsAsync(page.Items, ct);
        return ServiceResult<PreviewResultDto>.Ok(
            new PreviewResultDto(items, page.Offset, page.Limit),
            r.Message);
    }

    private async Task<ServiceResult<QueryResultDto>> RunAsync(
        QueryRequest req,
        string op,
        bool includeTotal,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Q))
        {
            Log.Information("Query {Reason}", "param_invalid");
            return BadRequest("Query is required.");
        }

        var gates = ValidateGates(req.Expand, req.MaxDepthRaw, req.ExpandedTagLimitRaw);
        if (!gates.IsSuccess)
            return ServiceResult<QueryResultDto>.Fail(gates.Code!, gates.Message!, gates.Details);

        var kindFilter = ResourceKinds.ParseQueryFilter(req.KindRaw);
        if (!kindFilter.IsSuccess)
        {
            Log.Information("Query {Reason} kind={Kind}", "param_invalid", req.KindRaw);
            return ServiceResult<QueryResultDto>.Fail(
                kindFilter.Code!,
                kindFilter.Message!,
                kindFilter.Details);
        }

        var expandMode = gates.Data!.ExpandMode;
        var maxDepth = gates.Data.MaxDepth;
        var expandedTagLimit = gates.Data.ExpandedTagLimit;
        var kind = kindFilter.Data;

        var parsed = parser.Parse(req.Q!);
        if (!parsed.IsSuccess)
        {
            Log.Information("Query {Reason}", "param_invalid");
            return ServiceResult<QueryResultDto>.Fail(parsed.Code!, parsed.Message!, parsed.Details);
        }

        var resolve = await ResolveTermsAsync(parsed.Data!, ct);
        if (resolve.Message is not null)
        {
            return ServiceResult<QueryResultDto>.Ok(
                new QueryResultDto([], req.Paging.Offset, req.Paging.Limit, 0),
                resolve.Message);
        }

        ExpandedQuery expanded;
        if (expandMode == RelationGraph.Child)
        {
            var expandedResult = await expander.ExpandAsync(
                parsed.Data!,
                resolve.Data!,
                maxDepth,
                expandedTagLimit,
                ct);
            if (!expandedResult.IsSuccess)
                return ServiceResult<QueryResultDto>.Fail(
                    expandedResult.Code!,
                    expandedResult.Message!,
                    expandedResult.Details);
            expanded = expandedResult.Data!;
        }
        else
        {
            expanded = BuildNoExpand(parsed.Data!, resolve.Data!);
        }

        var versionAtKey = await dataVersion.GetAsync(ct);
        var cacheDepth = expandMode == "none" ? 0 : maxDepth;
        var cacheLimit = expandMode == "none" ? 0 : expandedTagLimit;
        var key = QueryCacheKeyFactory.ForQuery(
            versionAtKey,
            op,
            parsed.Data!.Normalized,
            expandMode,
            cacheDepth,
            cacheLimit,
            req.Paging.Limit,
            req.Paging.Offset,
            ResourceKinds.CacheKeyToken(kind));

        var entryOptions = op == "preview"
            ? new CacheEntryOptions(cacheOptions.Value.PreviewTtl)
            : null;
        var page = await pipeline.GetOrLoadAsync(
            key,
            versionAtKey,
            dataVersion.GetAsync,
            token => executor.ExecuteAsync(expanded, req.Paging, includeTotal, token, kind),
            entryOptions,
            ct);

        return ServiceResult<QueryResultDto>.Ok(
            new QueryResultDto(page.Items, page.Offset, page.Limit, includeTotal ? page.Total : 0));
    }

    private async Task<ServiceResult<IReadOnlyList<ResolvedTerm>>> ResolveTermsAsync(
        ParsedQuery parsed,
        CancellationToken ct)
    {
        var aliases = parsed.Clauses
            .SelectMany((c, i) =>
                c.PositiveAliasNormalized.Select(a => (Alias: a, Positive: true, Clause: i))
                    .Concat(c.NegativeAliasNormalized.Select(a => (Alias: a, Positive: false, Clause: i))))
            .ToList();

        var unique = aliases.Select(a => a.Alias).Distinct(StringComparer.Ordinal).ToList();
        var map = await db.TagAliases.AsNoTracking()
            .Where(a => unique.Contains(a.AliasNormalized))
            .Select(a => new { a.AliasNormalized, a.TagId })
            .ToDictionaryAsync(a => a.AliasNormalized, a => a.TagId, StringComparer.Ordinal, ct);

        var resolved = new List<ResolvedTerm>();
        foreach (var (alias, positive, clause) in aliases)
        {
            if (!map.TryGetValue(alias, out var tagId))
            {
                if (positive)
                {
                    Log.Information(
                        "Query {Reason} token={Token} clauseIndex={ClauseIndex} normalized_query={NormalizedQuery}",
                        "unknown_positive_term",
                        alias,
                        clause,
                        parsed.Normalized);
                    return ServiceResult<IReadOnlyList<ResolvedTerm>>.Ok(
                        Array.Empty<ResolvedTerm>(),
                        $"Unknown positive term '{alias}'; returning empty result.");
                }

                Log.Information(
                    "Query {Reason} token={Token} clauseIndex={ClauseIndex} normalized_query={NormalizedQuery}",
                    "unknown_negative_term_ignored",
                    alias,
                    clause,
                    parsed.Normalized);
                continue;
            }

            resolved.Add(new ResolvedTerm(positive, alias, clause, tagId));
        }

        return ServiceResult<IReadOnlyList<ResolvedTerm>>.Ok(resolved);
    }

    private static ExpandedQuery BuildNoExpand(ParsedQuery parsed, IReadOnlyList<ResolvedTerm> resolved)
    {
        // Last-wins: duplicate aliases in one clause (e.g. "工作 工作") must not throw.
        var byKey = new Dictionary<(int Clause, bool Positive, string Alias), HashSet<Guid>>();
        foreach (var t in resolved)
            byKey[(t.ClauseIndex, t.IsPositive, t.AliasNormalized)] = new HashSet<Guid> { t.TagId };

        var all = new HashSet<Guid>();
        var clauses = new List<ExpandedClause>(parsed.Clauses.Count);
        for (var i = 0; i < parsed.Clauses.Count; i++)
        {
            var clause = parsed.Clauses[i];
            var positives = new List<IReadOnlySet<Guid>>();
            var seenPositive = new HashSet<string>(StringComparer.Ordinal);
            foreach (var alias in clause.PositiveAliasNormalized)
            {
                if (!seenPositive.Add(alias))
                    continue;
                if (byKey.TryGetValue((i, true, alias), out var set))
                {
                    positives.Add(set);
                    all.UnionWith(set);
                }
            }

            var negatives = new HashSet<Guid>();
            var seenNegative = new HashSet<string>(StringComparer.Ordinal);
            foreach (var alias in clause.NegativeAliasNormalized)
            {
                if (!seenNegative.Add(alias))
                    continue;
                if (byKey.TryGetValue((i, false, alias), out var set))
                {
                    negatives.UnionWith(set);
                    all.UnionWith(set);
                }
            }

            clauses.Add(new ExpandedClause { Positives = positives, Negatives = negatives });
        }

        return new ExpandedQuery
        {
            Normalized = parsed.Normalized,
            Clauses = clauses,
            AllExpandedTagIds = all,
            Truncated = false,
        };
    }

    private static ServiceResult<GateParams> ValidateGates(
        string? expandRaw,
        string? maxDepthRaw,
        string? expandedTagLimitRaw)
    {
        string expandMode;
        if (expandRaw is null)
        {
            expandMode = "none";
        }
        else if (expandRaw.Equals(RelationGraph.Child, StringComparison.Ordinal))
        {
            expandMode = RelationGraph.Child;
        }
        else
        {
            Log.Information("Query {Reason} expand={Expand}", "param_invalid", expandRaw);
            return ServiceResult<GateParams>.Fail(
                ErrorCodes.BadRequest,
                "Invalid expand value.",
                new { reason = "param_invalid" });
        }

        var maxDepth = QueryExpansionLimits.DefaultMaxDepth;
        if (maxDepthRaw is not null)
        {
            if (!int.TryParse(maxDepthRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var md) || md < 0)
            {
                Log.Information("Query {Reason} maxDepth={MaxDepth}", "param_invalid", maxDepthRaw);
                return ServiceResult<GateParams>.Fail(
                    ErrorCodes.BadRequest,
                    "Invalid maxDepth.",
                    new { reason = "param_invalid" });
            }

            if (md > QueryExpansionLimits.ServerMaxDepth)
            {
                Log.Information("Query {Reason} maxDepth={MaxDepth}", "param_exceeds_server_max", md);
                return ServiceResult<GateParams>.Fail(
                    ErrorCodes.ExpansionLimitExceeded,
                    "maxDepth exceeds server maximum.",
                    new { reason = "param_exceeds_server_max" });
            }

            maxDepth = md;
        }

        var expandedTagLimit = QueryExpansionLimits.DefaultExpandedTagLimit;
        if (expandedTagLimitRaw is not null)
        {
            if (!int.TryParse(expandedTagLimitRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lim)
                || lim <= 0)
            {
                Log.Information("Query {Reason} expandedTagLimit={Limit}", "param_invalid", expandedTagLimitRaw);
                return ServiceResult<GateParams>.Fail(
                    ErrorCodes.BadRequest,
                    "Invalid expandedTagLimit.",
                    new { reason = "param_invalid" });
            }

            if (lim > QueryExpansionLimits.ServerMaxExpandedTagLimit)
            {
                Log.Information("Query {Reason} expandedTagLimit={Limit}", "param_exceeds_server_max", lim);
                return ServiceResult<GateParams>.Fail(
                    ErrorCodes.ExpansionLimitExceeded,
                    "expandedTagLimit exceeds server maximum.",
                    new { reason = "param_exceeds_server_max" });
            }

            expandedTagLimit = lim;
        }

        return ServiceResult<GateParams>.Ok(new GateParams(expandMode, maxDepth, expandedTagLimit));
    }

    private static ServiceResult<QueryResultDto> BadRequest(string message) =>
        ServiceResult<QueryResultDto>.Fail(
            ErrorCodes.BadRequest,
            message,
            new { reason = "param_invalid" });

    private sealed record GateParams(string ExpandMode, int MaxDepth, int ExpandedTagLimit);
}
