using EverythingLabeled.Service.Services.Abstractions;

namespace EverythingLabeled.Service.Services.Query;

public sealed class ParsedQuery
{
    public required string Normalized { get; init; }
    public required IReadOnlyList<QueryClause> Clauses { get; init; }
}

public sealed class QueryClause
{
    public required IReadOnlyList<string> PositiveAliasNormalized { get; init; }
    public required IReadOnlyList<string> NegativeAliasNormalized { get; init; }
}

public readonly record struct ResolvedTerm(bool IsPositive, string AliasNormalized, int ClauseIndex, Guid TagId);

public sealed class ExpandedQuery
{
    public required string Normalized { get; init; }
    public required IReadOnlyList<ExpandedClause> Clauses { get; init; }
    public required IReadOnlyCollection<Guid> AllExpandedTagIds { get; init; }
    public bool Truncated { get; init; }
}

public sealed class ExpandedClause
{
    public required IReadOnlyList<IReadOnlySet<Guid>> Positives { get; init; }
    public required IReadOnlySet<Guid> Negatives { get; init; }
}

public sealed record QueryHitDto(Guid Id, string Uri, string Kind, string? DisplayName);

public sealed record QueryResultDto(IReadOnlyList<QueryHitDto> Items, int Offset, int Limit, int Total);

public sealed record PreviewResultDto(IReadOnlyList<QueryHitDto> Items, int Offset, int Limit);

public sealed record QueryRequest(
    string? Q,
    string? Expand,
    string? MaxDepthRaw,
    string? ExpandedTagLimitRaw,
    Paging Paging)
{
    public static QueryRequest Parse(
        string q,
        string? expand = null,
        string? maxDepth = null,
        string? expandedTagLimit = null,
        int offset = 0,
        int limit = Paging.DefaultLimit) =>
        new(q, expand, maxDepth, expandedTagLimit, new Paging(offset, limit));
}
