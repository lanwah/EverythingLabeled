using EverythingLabeled.Service.Data;
using EverythingLabeled.Service.Data.Entities;
using EverythingLabeled.Service.Services.Abstractions;
using EverythingLabeled.Service.Services.Tags;
using Microsoft.EntityFrameworkCore;

namespace EverythingLabeled.Service.Services.Resources;

public sealed class ResourceTagService(ElDbContext db, IUnitOfWork uow, TagService tags)
{
    private const int ApplyBatchLimit = 100;

    public async Task<ServiceResult<TagApplyResultDto>> ApplyAsync(TagApplyRequest body, CancellationToken ct)
    {
        var add = body.Add ?? [];
        var remove = body.Remove ?? [];
        if (add.Count + remove.Count > ApplyBatchLimit)
        {
            return ServiceResult<TagApplyResultDto>.Fail(
                ErrorCodes.BatchLimitExceeded,
                "add and remove together may not exceed 100 items.");
        }

        if (body.ResourceId is null && string.IsNullOrWhiteSpace(body.Uri))
        {
            return ServiceResult<TagApplyResultDto>.Fail(
                ErrorCodes.ValidationFailed,
                "resourceId or uri is required.");
        }

        var resolvedResource = await ResolveResourceAsync(body, implicitCreate: true, ct);
        if (!resolvedResource.IsSuccess)
            return ResourceService.Fail<TagApplyResultDto, Resource>(resolvedResource);

        var resource = resolvedResource.Data!;
        var createdResource = db.Entry(resource).State == EntityState.Added;

        var createdTags = new List<TagDto>();
        var addTags = new List<Tag>();
        foreach (var item in add)
        {
            var resolved = await ResolveApplyItemAsync(item, body.CreateMissing, createdTags, ct);
            if (!resolved.IsSuccess)
                return ResourceService.Fail<TagApplyResultDto, Tag>(resolved);
            addTags.Add(resolved.Data!);
        }

        var removeTags = new List<Tag>();
        foreach (var item in remove)
        {
            var resolved = await ResolveApplyItemAsync(item, createMissing: false, createdTags, ct);
            if (!resolved.IsSuccess)
                return ResourceService.Fail<TagApplyResultDto, Tag>(resolved);
            removeTags.Add(resolved.Data!);
        }

        var existing = await db.ResourceTags.Where(rt => rt.ResourceId == resource.Id).ToListAsync(ct);
        var bound = existing.Select(rt => rt.TagId).ToHashSet();
        var added = new List<Guid>();
        var removed = new List<Guid>();
        var now = DateTimeOffset.UtcNow;

        foreach (var tag in addTags)
        {
            if (!bound.Add(tag.Id))
                continue;

            db.ResourceTags.Add(new ResourceTag
            {
                ResourceId = resource.Id,
                TagId = tag.Id,
                CreatedAt = now,
            });
            added.Add(tag.Id);
        }

        foreach (var tag in removeTags)
        {
            if (!bound.Remove(tag.Id))
                continue;

            var row = existing.FirstOrDefault(rt => rt.TagId == tag.Id)
                ?? await db.ResourceTags.FirstOrDefaultAsync(
                    rt => rt.ResourceId == resource.Id && rt.TagId == tag.Id,
                    ct);
            if (row is null)
                continue;

            db.ResourceTags.Remove(row);
            removed.Add(tag.Id);
        }

        var dirty = createdResource || createdTags.Count > 0 || added.Count > 0 || removed.Count > 0;
        if (dirty)
            uow.MarkDirty();

        var version = await uow.CommitAsync(ct);
        var fresh = await LoadWithTagsAsync(resource.Id, ct);
        return ServiceResult<TagApplyResultDto>.Ok(new TagApplyResultDto(
            ResourceService.ToDetail(fresh!),
            createdTags,
            added,
            removed,
            version));
    }

    public async Task<ServiceResult<Paged<TagDto>>> ListTagsAsync(
        Guid? resourceId,
        string? uri,
        Paging paging,
        CancellationToken ct)
    {
        if (resourceId is null && string.IsNullOrWhiteSpace(uri))
        {
            return ServiceResult<Paged<TagDto>>.Fail(
                ErrorCodes.ValidationFailed,
                "resourceId or uri is required.");
        }

        var resolved = await ResolveResourceAsync(
            new TagApplyRequest(resourceId, uri, null, null, null, null, false),
            implicitCreate: false,
            ct);
        if (!resolved.IsSuccess)
            return ResourceService.Fail<Paged<TagDto>, Resource>(resolved);

        var rows = await db.ResourceTags.AsNoTracking()
            .Where(rt => rt.ResourceId == resolved.Data!.Id)
            .Include(rt => rt.Tag)
            .ThenInclude(t => t.Aliases)
            .ToListAsync(ct);

        var mapped = rows
            .OrderBy(rt => rt.CreatedAt)
            .ThenBy(rt => rt.TagId)
            .Select(rt => TagService.ToDto(rt.Tag))
            .ToList();
        var page = mapped.Skip(paging.Offset).Take(paging.Limit).ToList();
        return ServiceResult<Paged<TagDto>>.Ok(new Paged<TagDto>
        {
            Items = page,
            Offset = paging.Offset,
            Limit = paging.Limit,
            Total = mapped.Count,
        });
    }

    private async Task<ServiceResult<Resource>> ResolveResourceAsync(
        TagApplyRequest body,
        bool implicitCreate,
        CancellationToken ct)
    {
        if (body.ResourceId is { } id)
        {
            var resource = await db.Resources.FirstOrDefaultAsync(r => r.Id == id, ct);
            if (resource is null)
                return ResourceService.NotFound<Resource>();

            if (!string.IsNullOrWhiteSpace(body.Uri))
            {
                var normalized = UriNormalizer.Normalize(body.Uri, body.Kind);
                if (!normalized.IsSuccess)
                    return ResourceService.Fail<Resource, (string Uri, string Kind)>(normalized);

                if (!string.Equals(resource.Uri, normalized.Data.Uri, StringComparison.Ordinal))
                {
                    return ServiceResult<Resource>.Fail(
                        ErrorCodes.ResourceIdUriMismatch,
                        "resourceId does not match uri.");
                }
            }

            return ServiceResult<Resource>.Ok(resource);
        }

        var uriResult = UriNormalizer.Normalize(body.Uri!, body.Kind);
        if (!uriResult.IsSuccess)
            return ResourceService.Fail<Resource, (string Uri, string Kind)>(uriResult);

        var existing = await db.Resources.FirstOrDefaultAsync(r => r.Uri == uriResult.Data.Uri, ct);
        if (existing is not null)
            return ServiceResult<Resource>.Ok(existing);

        if (!implicitCreate)
            return ResourceService.NotFound<Resource>();

        var now = DateTimeOffset.UtcNow;
        var created = new Resource
        {
            Id = Guid.NewGuid(),
            Uri = uriResult.Data.Uri,
            Kind = uriResult.Data.Kind,
            DisplayName = body.DisplayName,
            CreatedAt = now,
            UpdatedAt = now,
            Revision = 0,
        };
        db.Resources.Add(created);
        return ServiceResult<Resource>.Ok(created);
    }

    private async Task<ServiceResult<Tag>> ResolveApplyItemAsync(
        TagApplyItem item,
        bool createMissing,
        List<TagDto> createdTags,
        CancellationToken ct)
    {
        if (item.Id is { } tagId)
            return await tags.ResolveByAliasOrId(null, tagId, ct);

        var resolved = await tags.ResolveByAliasOrId(item.Alias, null, ct);
        if (resolved.IsSuccess || !createMissing)
            return resolved;

        var created = await tags.CreateUncommittedAsync(
            new CreateTagRequest(item.Alias, null, null, null),
            ct);
        if (!created.IsSuccess)
            return created;

        createdTags.Add(TagService.ToDto(created.Data!));
        return created;
    }

    private Task<Resource?> LoadWithTagsAsync(Guid id, CancellationToken ct) =>
        db.Resources
            .Include(r => r.ResourceTags)
            .ThenInclude(rt => rt.Tag)
            .ThenInclude(t => t.Aliases)
            .FirstOrDefaultAsync(r => r.Id == id, ct);
}
