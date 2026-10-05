using EverythingLabeled.Service.Services.Abstractions;

namespace EverythingLabeled.Service.Services.Tags;

public sealed record TagDto(
    Guid Id,
    string Name,
    string? Namespace,
    int? Seq,
    string NameInitials,
    IReadOnlyList<string> Aliases,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long Revision);

public sealed record TagWriteDto(
    Guid Id,
    string Name,
    string? Namespace,
    int? Seq,
    string NameInitials,
    IReadOnlyList<string> Aliases,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long Revision,
    int DataVersion);

public sealed record DataVersionDto(int DataVersion);

public sealed record SuggestItemDto(
    Guid TagId,
    string Alias,
    string Name,
    string? Namespace,
    string NameInitials);

public sealed record CreateTagRequest(
    string? Name,
    string? Namespace,
    int? Seq,
    IReadOnlyList<string>? Aliases);

public sealed record PatchTagRequest(string? Name, string? Namespace, int? Seq);

public sealed record AddAliasRequest(string? Alias);
