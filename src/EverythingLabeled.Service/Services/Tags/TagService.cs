using EverythingLabeled.Service.Data;
using EverythingLabeled.Service.Data.Entities;
using EverythingLabeled.Service.Services.Abstractions;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace EverythingLabeled.Service.Services.Tags;

public sealed class TagService(ElDbContext db, IUnitOfWork uow, INameInitialsGenerator initials)
{
    public async Task<ServiceResult<Paged<TagDto>>> ListAsync(
        string? ns,
        string? q,
        Paging paging,
        CancellationToken ct)
    {
        if (q is not null && string.IsNullOrWhiteSpace(q))
        {
            return ServiceResult<Paged<TagDto>>.Fail(
                ErrorCodes.BadRequest,
                "Query must not be blank.");
        }

        var query = db.Tags.AsNoTracking().Include(t => t.Aliases).AsQueryable();
        if (ns is not null)
        {
            var nsFilter = ns.Length == 0 ? null : ns;
            query = query.Where(t => t.Namespace == nsFilter);
        }

        if (q is not null)
        {
            var like = LikePattern.Prefix(AliasRules.Normalize(q));
            query = query.Where(t =>
                t.Aliases.Any(a => EF.Functions.Like(a.AliasNormalized, like, LikePattern.EscapeCharacter))
                || EF.Functions.Like(t.NameInitials, like, LikePattern.EscapeCharacter));
        }

        var matched = TagListOrder.Apply(await query.ToListAsync(ct)).ToList();
        var page = matched.Skip(paging.Offset).Take(paging.Limit).ToList();
        return ServiceResult<Paged<TagDto>>.Ok(new Paged<TagDto>
        {
            Items = page.Select(ToDto).ToList(),
            Offset = paging.Offset,
            Limit = paging.Limit,
            Total = matched.Count,
        });
    }

    public async Task<ServiceResult<TagDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var tag = await LoadTagAsync(id, ct);
        return tag is null
            ? NotFound<TagDto>()
            : ServiceResult<TagDto>.Ok(ToDto(tag));
    }

    public async Task<ServiceResult<TagDto>> GetByAliasAsync(string alias, CancellationToken ct)
    {
        var normalized = AliasRules.Normalize(alias);
        var tagId = await db.TagAliases.AsNoTracking()
            .Where(a => a.AliasNormalized == normalized)
            .Select(a => a.TagId)
            .FirstOrDefaultAsync(ct);
        if (tagId == Guid.Empty)
        {
            return ServiceResult<TagDto>.Fail(
                ErrorCodes.TagNotFound,
                $"Alias '{alias}' not found.",
                new { alias });
        }

        return await GetAsync(tagId, ct);
    }

    public async Task<ServiceResult<TagWriteDto>> CreateAsync(CreateTagRequest body, CancellationToken ct)
    {
        var aliases = body.Aliases is { Count: > 0 } ? body.Aliases : [body.Name];
        var rejected = RejectAliases<TagWriteDto>(aliases);
        if (rejected is not null)
            return rejected;

        if (await NameTakenAsync(body.Namespace, body.Name, excludeId: null, ct))
            return NameConflict<TagWriteDto>(body.Name);

        foreach (var alias in aliases)
        {
            var conflict = await AliasTakenAsync(alias, ct);
            if (conflict is not null)
                return conflict;
        }

        var now = DateTimeOffset.UtcNow;
        var tag = new Tag
        {
            Id = Guid.NewGuid(),
            Name = body.Name,
            NameNormalized = NormalizeName(body.Name),
            NameInitials = initials.FromName(body.Name),
            Namespace = body.Namespace,
            Seq = body.Seq,
            CreatedAt = now,
            UpdatedAt = now,
            Revision = 0,
        };

        foreach (var alias in aliases)
        {
            tag.Aliases.Add(new TagAlias
            {
                TagId = tag.Id,
                Alias = alias,
                AliasNormalized = AliasRules.Normalize(alias),
            });
        }

        db.Tags.Add(tag);
        uow.MarkDirty();
        var version = await uow.CommitAsync(ct);
        return ServiceResult<TagWriteDto>.Ok(ToWrite(tag, version));
    }

    public async Task<ServiceResult<TagWriteDto>> PatchAsync(Guid id, PatchTagRequest body, CancellationToken ct)
    {
        var tag = await LoadTagAsync(id, ct);
        if (tag is null)
            return NotFound<TagWriteDto>();

        var dirty = false;
        if (body.Name is not null && body.Name != tag.Name)
        {
            if (await NameTakenAsync(body.Namespace ?? tag.Namespace, body.Name, tag.Id, ct))
                return NameConflict<TagWriteDto>(body.Name);

            tag.Name = body.Name;
            tag.NameNormalized = NormalizeName(body.Name);
            tag.NameInitials = initials.FromName(body.Name);
            dirty = true;
        }

        if (body.Namespace is not null && body.Namespace != tag.Namespace)
        {
            if (await NameTakenAsync(body.Namespace, tag.Name, tag.Id, ct))
                return NameConflict<TagWriteDto>(tag.Name);

            tag.Namespace = body.Namespace;
            dirty = true;
        }

        if (body.Seq is { } seq && tag.Seq != seq)
        {
            tag.Seq = seq;
            dirty = true;
        }

        if (!dirty)
        {
            var current = await uow.CommitAsync(ct);
            return ServiceResult<TagWriteDto>.Ok(ToWrite(tag, current));
        }

        tag.Revision++;
        tag.UpdatedAt = DateTimeOffset.UtcNow;
        uow.MarkDirty();
        var version = await uow.CommitAsync(ct);
        return ServiceResult<TagWriteDto>.Ok(ToWrite(tag, version));
    }

    public async Task<ServiceResult<DataVersionDto>> DeleteAsync(Guid id, CancellationToken ct)
    {
        var tag = await db.Tags.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tag is null)
            return NotFound<DataVersionDto>();

        db.Tags.Remove(tag);
        uow.MarkDirty();
        var version = await uow.CommitAsync(ct);
        return ServiceResult<DataVersionDto>.Ok(new DataVersionDto(version));
    }

    public async Task<ServiceResult<TagWriteDto>> AddAliasAsync(Guid id, string alias, CancellationToken ct)
    {
        var rejected = RejectAliases<TagWriteDto>([alias]);
        if (rejected is not null)
            return rejected;

        var tag = await LoadTagAsync(id, ct);
        if (tag is null)
            return NotFound<TagWriteDto>();

        var conflict = await AliasTakenAsync(alias, ct);
        if (conflict is not null)
            return conflict;

        tag.Aliases.Add(new TagAlias
        {
            TagId = tag.Id,
            Alias = alias,
            AliasNormalized = AliasRules.Normalize(alias),
        });
        uow.MarkDirty();
        var version = await uow.CommitAsync(ct);
        return ServiceResult<TagWriteDto>.Ok(ToWrite(tag, version));
    }

    public async Task<ServiceResult<TagWriteDto>> DeleteAliasAsync(Guid id, string alias, CancellationToken ct)
    {
        var tag = await LoadTagAsync(id, ct);
        if (tag is null)
            return NotFound<TagWriteDto>();

        var normalized = AliasRules.Normalize(alias);
        var row = tag.Aliases.FirstOrDefault(a => a.AliasNormalized == normalized);
        if (row is null)
        {
            return ServiceResult<TagWriteDto>.Fail(
                ErrorCodes.NotFound,
                $"Alias '{alias}' not found.");
        }

        if (tag.Aliases.Count <= 1)
        {
            return ServiceResult<TagWriteDto>.Fail(
                ErrorCodes.ValidationFailed,
                "Cannot delete the last alias.",
                new { reason = "last_alias" });
        }

        db.TagAliases.Remove(row);
        uow.MarkDirty();
        var version = await uow.CommitAsync(ct);
        return ServiceResult<TagWriteDto>.Ok(ToWrite(tag, version));
    }

    private async Task<Tag?> LoadTagAsync(Guid id, CancellationToken ct) =>
        await db.Tags.Include(t => t.Aliases).FirstOrDefaultAsync(t => t.Id == id, ct);

    private Task<bool> NameTakenAsync(string? ns, string name, Guid? excludeId, CancellationToken ct)
    {
        var query = db.Tags.Where(t => t.Namespace == ns && t.Name == name);
        if (excludeId is { } id)
            query = query.Where(t => t.Id != id);
        return query.AnyAsync(ct);
    }

    private async Task<ServiceResult<TagWriteDto>?> AliasTakenAsync(string alias, CancellationToken ct)
    {
        var normalized = AliasRules.Normalize(alias);
        var exists = await db.TagAliases.AnyAsync(a => a.AliasNormalized == normalized, ct);
        if (!exists)
            return null;

        return ServiceResult<TagWriteDto>.Fail(
            ErrorCodes.AliasConflict,
            $"Alias '{alias}' already exists.");
    }

    private static ServiceResult<T>? RejectAliases<T>(IReadOnlyList<string> aliases)
    {
        foreach (var alias in aliases)
        {
            if (AliasRules.TryValidate(alias, out var reason))
                continue;

            Log.Warning("{Reason}", reason);
            return ServiceResult<T>.Fail(
                ErrorCodes.ValidationFailed,
                "Alias rejected.",
                new { reason });
        }

        return null;
    }

    private static ServiceResult<T> NotFound<T>() =>
        ServiceResult<T>.Fail(ErrorCodes.NotFound, "Tag not found.");

    private static ServiceResult<T> NameConflict<T>(string name) =>
        ServiceResult<T>.Fail(ErrorCodes.NameConflict, $"Name '{name}' already exists in this namespace.");

    private static string NormalizeName(string name) => name.ToLowerInvariant();

    private static TagDto ToDto(Tag tag) =>
        new(
            tag.Id,
            tag.Name,
            tag.Namespace,
            tag.Seq,
            tag.NameInitials,
            AliasNames(tag),
            tag.CreatedAt,
            tag.UpdatedAt,
            tag.Revision);

    private static TagWriteDto ToWrite(Tag tag, int version) =>
        new(
            tag.Id,
            tag.Name,
            tag.Namespace,
            tag.Seq,
            tag.NameInitials,
            AliasNames(tag),
            tag.CreatedAt,
            tag.UpdatedAt,
            tag.Revision,
            version);

    private static IReadOnlyList<string> AliasNames(Tag tag) =>
        tag.Aliases.Select(a => a.Alias).ToList();
}
