namespace EverythingLabeled.Service.Services.Relations;

public sealed record RelationDto(
    Guid Id,
    Guid FromTagId,
    Guid ToTagId,
    string Kind,
    int? Seq,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long Revision);

public sealed record RelationWriteDto(
    Guid Id,
    Guid FromTagId,
    Guid ToTagId,
    string Kind,
    int? Seq,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long Revision,
    int DataVersion);

public sealed record RelationBatchResultDto(IReadOnlyList<RelationDto> Items, int DataVersion);

public sealed record RelationReorderResultDto(IReadOnlyList<RelationDto> Items, int DataVersion);

public sealed record NeighborTagDto(Guid Id, string Name, string? Namespace, string NameInitials);

public sealed record NeighborDto(
    Guid Id,
    Guid FromTagId,
    Guid ToTagId,
    string Kind,
    int? Seq,
    string Direction,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long Revision,
    NeighborTagDto Tag);

public sealed record CreateRelationRequest(Guid FromTagId, Guid ToTagId, string? Kind, int? Seq);

public sealed record CreateRelationBatchRequest(IReadOnlyList<CreateRelationRequest>? Items);

public sealed record PatchRelationRequest(int? Seq);

public sealed record ReorderRelationsRequest(Guid FromTagId, string? Kind, IReadOnlyList<Guid>? OrderedToTagIds);
