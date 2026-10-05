using EverythingLabeled.Service.Data;
using EverythingLabeled.Service.Data.Entities;
using EverythingLabeled.Service.Services.Abstractions;
using EverythingLabeled.Service.Services.Tags;
using Microsoft.EntityFrameworkCore;

namespace EverythingLabeled.Service.Services.Resources;

public sealed class ResourceService(ElDbContext db, IUnitOfWork uow)
{
    public async Task<ServiceResult<Paged<ResourceListDto>>> ListAsync(
        string? kind,
        string? uriExact,
        Paging paging,
        CancellationToken ct)
    {
        var query = db.Resources.AsNoTracking().AsQueryable();
        if (kind is not null)
            query = query.Where(r => r.Kind == kind);

        if (uriExact is not null)
        {
            var normalized = UriNormalizer.Normalize(uriExact, null);
            if (!normalized.IsSuccess)
                return Fail<Paged<ResourceListDto>, (string Uri, string Kind)>(normalized);

            query = query.Where(r => r.Uri == normalized.Data.Uri);
        }

        // SQLite cannot translate DateTimeOffset in ORDER BY; sort after materialize.
        var matched = (await query.ToListAsync(ct))
            .OrderBy(r => r.CreatedAt)
            .ThenBy(r => r.Id)
            .ToList();
        var page = matched.Skip(paging.Offset).Take(paging.Limit).ToList();
        return ServiceResult<Paged<ResourceListDto>>.Ok(new Paged<ResourceListDto>
        {
            Items = page.Select(ToList).ToList(),
            Offset = paging.Offset,
            Limit = paging.Limit,
            Total = matched.Count,
        });
    }

    public async Task<ServiceResult<ResourceDetailDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var resource = await LoadWithTagsAsync(id, ct);
        return resource is null
            ? NotFound<ResourceDetailDto>()
            : ServiceResult<ResourceDetailDto>.Ok(ToDetail(resource));
    }

    public async Task<ServiceResult<ResourceDetailDto>> GetByUriAsync(string uri, CancellationToken ct)
    {
        var normalized = UriNormalizer.Normalize(uri, null);
        if (!normalized.IsSuccess)
            return Fail<ResourceDetailDto, (string Uri, string Kind)>(normalized);

        var resource = await LoadWithTagsByUriAsync(normalized.Data.Uri, ct);
        return resource is null
            ? NotFound<ResourceDetailDto>()
            : ServiceResult<ResourceDetailDto>.Ok(ToDetail(resource));
    }

    public async Task<ServiceResult<ResourceWriteDto>> CreateAsync(CreateResourceRequest body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Uri))
        {
            return ServiceResult<ResourceWriteDto>.Fail(
                ErrorCodes.ValidationFailed,
                "URI is required.");
        }

        var normalized = UriNormalizer.Normalize(body.Uri, body.Kind);
        if (!normalized.IsSuccess)
            return Fail<ResourceWriteDto, (string Uri, string Kind)>(normalized);

        if (await db.Resources.AnyAsync(r => r.Uri == normalized.Data.Uri, ct))
        {
            return ServiceResult<ResourceWriteDto>.Fail(
                ErrorCodes.UriConflict,
                "A resource with this URI already exists.");
        }

        var now = DateTimeOffset.UtcNow;
        var resource = new Resource
        {
            Id = Guid.NewGuid(),
            Uri = normalized.Data.Uri,
            Kind = normalized.Data.Kind,
            DisplayName = body.DisplayName,
            CreatedAt = now,
            UpdatedAt = now,
            Revision = 0,
        };
        db.Resources.Add(resource);
        uow.MarkDirty();
        var version = await uow.CommitAsync(ct);
        return ServiceResult<ResourceWriteDto>.Ok(ToWrite(resource, version));
    }

    public async Task<ServiceResult<ResourceWriteDto>> PatchAsync(Guid id, PatchResourceRequest body, CancellationToken ct)
    {
        var resource = await LoadWithTagsAsync(id, ct);
        if (resource is null)
            return NotFound<ResourceWriteDto>();

        var dirty = false;
        if (body.Kind is not null)
        {
            var normalized = UriNormalizer.Normalize(resource.Uri, body.Kind);
            if (!normalized.IsSuccess)
                return Fail<ResourceWriteDto, (string Uri, string Kind)>(normalized);

            if (normalized.Data.Kind != resource.Kind)
            {
                resource.Kind = normalized.Data.Kind;
                dirty = true;
            }
        }

        if (body.DisplayName is not null && body.DisplayName != resource.DisplayName)
        {
            resource.DisplayName = body.DisplayName;
            dirty = true;
        }

        if (!dirty)
        {
            var current = await uow.CommitAsync(ct);
            return ServiceResult<ResourceWriteDto>.Ok(ToWrite(resource, current));
        }

        resource.Revision++;
        resource.UpdatedAt = DateTimeOffset.UtcNow;
        uow.MarkDirty();
        var version = await uow.CommitAsync(ct);
        return ServiceResult<ResourceWriteDto>.Ok(ToWrite(resource, version));
    }

    public async Task<ServiceResult<DataVersionDto>> DeleteAsync(Guid id, CancellationToken ct)
    {
        var resource = await db.Resources.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (resource is null)
            return NotFound<DataVersionDto>();

        db.Resources.Remove(resource);
        uow.MarkDirty();
        var version = await uow.CommitAsync(ct);
        return ServiceResult<DataVersionDto>.Ok(new DataVersionDto(version));
    }

    internal static ResourceListDto ToList(Resource resource) =>
        new(
            resource.Id,
            resource.Uri,
            resource.Kind,
            resource.DisplayName,
            resource.CreatedAt,
            resource.UpdatedAt,
            resource.Revision);

    internal static ResourceDetailDto ToDetail(Resource resource) =>
        new(
            resource.Id,
            resource.Uri,
            resource.Kind,
            resource.DisplayName,
            resource.CreatedAt,
            resource.UpdatedAt,
            resource.Revision,
            MapTags(resource));

    internal static ResourceWriteDto ToWrite(Resource resource, int version) =>
        new(
            resource.Id,
            resource.Uri,
            resource.Kind,
            resource.DisplayName,
            resource.CreatedAt,
            resource.UpdatedAt,
            resource.Revision,
            MapTags(resource),
            version);

    internal static IReadOnlyList<TagDto> MapTags(Resource resource) =>
        resource.ResourceTags
            .OrderBy(rt => rt.CreatedAt)
            .ThenBy(rt => rt.TagId)
            .Select(rt => TagService.ToDto(rt.Tag))
            .ToList();

    internal static ServiceResult<T> Fail<T, TIn>(ServiceResult<TIn> source) =>
        ServiceResult<T>.Fail(source.Code!, source.Message!, source.Details);

    internal static ServiceResult<T> NotFound<T>() =>
        ServiceResult<T>.Fail(ErrorCodes.ResourceNotFound, "Resource not found.");

    private Task<Resource?> LoadWithTagsAsync(Guid id, CancellationToken ct) =>
        WithTags().FirstOrDefaultAsync(r => r.Id == id, ct);

    private Task<Resource?> LoadWithTagsByUriAsync(string uri, CancellationToken ct) =>
        WithTags().FirstOrDefaultAsync(r => r.Uri == uri, ct);

    private IQueryable<Resource> WithTags() =>
        db.Resources
            .Include(r => r.ResourceTags)
            .ThenInclude(rt => rt.Tag)
            .ThenInclude(t => t.Aliases);
}
