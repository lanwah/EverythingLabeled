namespace EverythingLabeled.Client;

public sealed record TagInfo(
    Guid Id,
    string Name,
    string? Namespace,
    int? Seq,
    string NameInitials,
    IReadOnlyList<string> Aliases,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long Revision);

public sealed record ResourceDetail(
    Guid Id,
    string Uri,
    string Kind,
    string? DisplayName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long Revision,
    IReadOnlyList<TagInfo> Tags);

public sealed record TagApplyData(
    ResourceDetail Resource,
    IReadOnlyList<TagInfo> CreatedTags,
    IReadOnlyList<Guid> Added,
    IReadOnlyList<Guid> Removed,
    int DataVersion);

public sealed record TagsPage(IReadOnlyList<TagInfo> Items, int Offset, int Limit, int Total);

public sealed record QueryHit(Guid Id, string Uri, string Kind, string? DisplayName);

public sealed record QueryPage(IReadOnlyList<QueryHit> Items, int Offset, int Limit, int Total);

public sealed record PreviewPage(IReadOnlyList<QueryHit> Items, int Offset, int Limit);

public sealed record SuggestItem(Guid TagId, string Alias, string Name, string? Namespace, string NameInitials);

public sealed record SuggestPage(IReadOnlyList<SuggestItem> Items, int Offset, int Limit, int Total);

public sealed record HealthData(string Health, int DataVersion);

public sealed record MetaData(int DataVersion);
