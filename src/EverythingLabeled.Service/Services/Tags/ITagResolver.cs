using EverythingLabeled.Service.Data.Entities;
using EverythingLabeled.Service.Services.Abstractions;

namespace EverythingLabeled.Service.Services.Tags;

public interface ITagResolver
{
    Task<ServiceResult<Tag>> ResolveByAliasOrId(string? alias, Guid? id, CancellationToken ct);
}
