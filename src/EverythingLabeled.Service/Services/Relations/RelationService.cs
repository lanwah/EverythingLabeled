using EverythingLabeled.Service.Data;
using EverythingLabeled.Service.Data.Entities;
using EverythingLabeled.Service.Services.Abstractions;
using EverythingLabeled.Service.Services.Tags;
using Microsoft.EntityFrameworkCore;

namespace EverythingLabeled.Service.Services.Relations;

public sealed class RelationService(ElDbContext db, IUnitOfWork uow, TagService tags)
{
    public async Task<ServiceResult<Paged<RelationDto>>> ListAsync(
        Guid? from,
        Guid? to,
        string? kind,
        Paging paging,
        CancellationToken ct)
    {
        if (from is null && to is null)
        {
            return ServiceResult<Paged<RelationDto>>.Fail(
                ErrorCodes.RelationsFilterRequired,
                "fromTagId or toTagId is required.");
        }

        var kindFilter = NormalizeKindFilter(kind);
        if (!kindFilter.IsSuccess)
            return Fail<Paged<RelationDto>, IReadOnlyList<string>>(kindFilter);

        var query = db.TagRelations.AsNoTracking().AsQueryable();
        if (from is { } fromId)
            query = query.Where(r => r.FromTagId == fromId);
        if (to is { } toId)
            query = query.Where(r => r.ToTagId == toId);
        if (kindFilter.Data is { Count: 1 } kinds)
            query = query.Where(r => r.Kind == kinds[0]);

        var rows = await query.ToListAsync(ct);
        var ordered = from is not null
            ? OrderByEdgeSeq(rows)
            : OrderByCreated(rows);
        var matched = ordered.ToList();
        var page = matched.Skip(paging.Offset).Take(paging.Limit).Select(ToDto).ToList();
        return ServiceResult<Paged<RelationDto>>.Ok(new Paged<RelationDto>
        {
            Items = page,
            Offset = paging.Offset,
            Limit = paging.Limit,
            Total = matched.Count,
        });
    }

    public async Task<ServiceResult<RelationDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var row = await db.TagRelations.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        return row is null
            ? NotFound<RelationDto>()
            : ServiceResult<RelationDto>.Ok(ToDto(row));
    }

    public async Task<ServiceResult<RelationWriteDto>> CreateAsync(CreateRelationRequest body, CancellationToken ct)
    {
        var existing = await LoadEdgesAsync(ct);
        var proposed = new[] { ToEdge(body) };
        var graph = RelationGraph.Validate(existing, proposed);
        if (graph is not null)
            return GraphFail<RelationWriteDto>(graph, includeIndex: false);

        var missing = await MissingTagAsync<RelationWriteDto>(body, index: null, ct);
        if (missing is not null)
            return missing;

        var entity = NewRelation(body);
        db.TagRelations.Add(entity);
        uow.MarkDirty();
        var version = await uow.CommitAsync(ct);
        return ServiceResult<RelationWriteDto>.Ok(ToWrite(entity, version));
    }

    public async Task<ServiceResult<RelationBatchResultDto>> CreateBatchAsync(
        IReadOnlyList<CreateRelationRequest> items,
        CancellationToken ct)
    {
        if (items.Count is < RelationGraph.BatchMin or > RelationGraph.BatchMax)
        {
            return ServiceResult<RelationBatchResultDto>.Fail(
                ErrorCodes.BatchLimitExceeded,
                "items length must be between 1 and 100.");
        }

        var existing = await LoadEdgesAsync(ct);
        var proposed = items.Select(ToEdge).ToList();
        var graph = RelationGraph.Validate(existing, proposed);
        if (graph is not null)
            return GraphFail<RelationBatchResultDto>(graph, includeIndex: true);

        for (var i = 0; i < items.Count; i++)
        {
            var missing = await MissingTagAsync<RelationBatchResultDto>(items[i], i, ct);
            if (missing is not null)
                return missing;
        }

        var now = DateTimeOffset.UtcNow;
        var created = new List<TagRelation>(items.Count);
        foreach (var item in items)
        {
            var entity = NewRelation(item, now);
            db.TagRelations.Add(entity);
            created.Add(entity);
        }

        uow.MarkDirty();
        var version = await uow.CommitAsync(ct);
        return ServiceResult<RelationBatchResultDto>.Ok(
            new RelationBatchResultDto(created.Select(ToDto).ToList(), version));
    }

    public async Task<ServiceResult<RelationWriteDto>> PatchSeqAsync(Guid id, int? seq, CancellationToken ct)
    {
        var row = await db.TagRelations.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null)
            return NotFound<RelationWriteDto>();

        if (row.Seq == seq)
        {
            var unchanged = await uow.CommitAsync(ct);
            return ServiceResult<RelationWriteDto>.Ok(ToWrite(row, unchanged));
        }

        row.Seq = seq;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        row.Revision++;
        uow.MarkDirty();
        var version = await uow.CommitAsync(ct);
        return ServiceResult<RelationWriteDto>.Ok(ToWrite(row, version));
    }

    public async Task<ServiceResult<DataVersionDto>> DeleteAsync(Guid id, CancellationToken ct)
    {
        var row = await db.TagRelations.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null)
            return NotFound<DataVersionDto>();

        db.TagRelations.Remove(row);
        uow.MarkDirty();
        var version = await uow.CommitAsync(ct);
        return ServiceResult<DataVersionDto>.Ok(new DataVersionDto(version));
    }

    public async Task<ServiceResult<RelationReorderResultDto>> ReorderAsync(
        Guid fromTagId,
        string kind,
        IReadOnlyList<Guid> orderedToTagIds,
        CancellationToken ct)
    {
        if (!RelationGraph.IsKnownKind(kind))
        {
            return ServiceResult<RelationReorderResultDto>.Fail(
                ErrorCodes.ValidationFailed,
                "Invalid relation kind.");
        }

        var fromTag = await tags.GetAsync(fromTagId, ct);
        if (!fromTag.IsSuccess)
            return Fail<RelationReorderResultDto, TagDto>(fromTag);

        var rows = await db.TagRelations
            .Where(r => r.FromTagId == fromTagId && r.Kind == kind)
            .ToListAsync(ct);

        if (orderedToTagIds.Count != orderedToTagIds.Distinct().Count()
            || !rows.Select(r => r.ToTagId).ToHashSet().SetEquals(orderedToTagIds))
        {
            return ServiceResult<RelationReorderResultDto>.Fail(
                ErrorCodes.ValidationFailed,
                "orderedToTagIds must match the existing toTagId set.");
        }

        var byTo = rows.ToDictionary(r => r.ToTagId);
        var aligned = true;
        for (var i = 0; i < orderedToTagIds.Count; i++)
        {
            if (byTo[orderedToTagIds[i]].Seq != i)
            {
                aligned = false;
                break;
            }
        }

        if (!aligned)
        {
            var now = DateTimeOffset.UtcNow;
            for (var i = 0; i < orderedToTagIds.Count; i++)
            {
                var row = byTo[orderedToTagIds[i]];
                row.Seq = i;
                row.UpdatedAt = now;
                row.Revision++;
            }

            uow.MarkDirty();
        }

        var version = await uow.CommitAsync(ct);
        var items = orderedToTagIds.Select(id => ToDto(byTo[id])).ToList();
        return ServiceResult<RelationReorderResultDto>.Ok(new RelationReorderResultDto(items, version));
    }

    public async Task<ServiceResult<Paged<NeighborDto>>> NeighborsAsync(
        Guid tagId,
        string direction,
        string? kindsCsv,
        Paging paging,
        CancellationToken ct)
    {
        var tag = await tags.GetAsync(tagId, ct);
        if (!tag.IsSuccess)
            return Fail<Paged<NeighborDto>, TagDto>(tag);

        if (direction is not ("out" or "in" or "both"))
        {
            return ServiceResult<Paged<NeighborDto>>.Fail(
                ErrorCodes.ValidationFailed,
                "direction must be out, in, or both.");
        }

        var kinds = NormalizeKindFilter(kindsCsv);
        if (!kinds.IsSuccess)
            return Fail<Paged<NeighborDto>, IReadOnlyList<string>>(kinds);

        var kindSet = kinds.Data!.ToHashSet(StringComparer.Ordinal);
        var query = db.TagRelations.AsNoTracking()
            .Include(r => r.FromTag)
            .Include(r => r.ToTag)
            .Where(r => kindSet.Contains(r.Kind));

        query = direction switch
        {
            "out" => query.Where(r => r.FromTagId == tagId),
            "in" => query.Where(r => r.ToTagId == tagId),
            _ => query.Where(r => r.FromTagId == tagId || r.ToTagId == tagId),
        };

        var rows = await query.ToListAsync(ct);
        var comparable = direction == "out" && kindSet.Count == 1;
        var ordered = comparable ? OrderByEdgeSeq(rows) : OrderByCreated(rows);
        var matched = ordered.ToList();
        var page = matched
            .Skip(paging.Offset)
            .Take(paging.Limit)
            .Select(r => ToNeighbor(r, tagId))
            .ToList();
        return ServiceResult<Paged<NeighborDto>>.Ok(new Paged<NeighborDto>
        {
            Items = page,
            Offset = paging.Offset,
            Limit = paging.Limit,
            Total = matched.Count,
        });
    }

    private async Task<IReadOnlyList<RelationEdge>> LoadEdgesAsync(CancellationToken ct) =>
        await db.TagRelations
            .Select(r => new RelationEdge(r.FromTagId, r.ToTagId, r.Kind))
            .ToListAsync(ct);

    private async Task<ServiceResult<T>?> MissingTagAsync<T>(
        CreateRelationRequest body,
        int? index,
        CancellationToken ct)
    {
        var fromExists = await db.Tags.AnyAsync(t => t.Id == body.FromTagId, ct);
        var toExists = await db.Tags.AnyAsync(t => t.Id == body.ToTagId, ct);
        if (fromExists && toExists)
            return null;

        return ServiceResult<T>.Fail(
            ErrorCodes.TagNotFound,
            "Tag not found.",
            index is { } i
                ? Details(i, body)
                : new { fromTagId = body.FromTagId, toTagId = body.ToTagId, kind = body.Kind });
    }

    private static TagRelation NewRelation(CreateRelationRequest body, DateTimeOffset? now = null)
    {
        var stamp = now ?? DateTimeOffset.UtcNow;
        return new TagRelation
        {
            Id = Guid.NewGuid(),
            FromTagId = body.FromTagId,
            ToTagId = body.ToTagId,
            Kind = body.Kind!,
            Seq = body.Seq,
            CreatedAt = stamp,
            UpdatedAt = stamp,
            Revision = 0,
        };
    }

    private static RelationEdge ToEdge(CreateRelationRequest body) =>
        new(body.FromTagId, body.ToTagId, body.Kind ?? "");

    private static ServiceResult<IReadOnlyList<string>> NormalizeKindFilter(string? kindsCsv)
    {
        if (string.IsNullOrWhiteSpace(kindsCsv))
            return ServiceResult<IReadOnlyList<string>>.Ok([RelationGraph.Child, RelationGraph.Related]);

        var kinds = kindsCsv
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (kinds.Count == 0 || kinds.Any(k => !RelationGraph.IsKnownKind(k)))
        {
            return ServiceResult<IReadOnlyList<string>>.Fail(
                ErrorCodes.ValidationFailed,
                "Invalid relation kind.");
        }

        return ServiceResult<IReadOnlyList<string>>.Ok(kinds);
    }

    private static IOrderedEnumerable<TagRelation> OrderByEdgeSeq(IEnumerable<TagRelation> rows) =>
        rows
            .OrderBy(r => r.Seq is null)
            .ThenBy(r => r.Seq)
            .ThenBy(r => r.CreatedAt)
            .ThenBy(r => r.Id);

    private static IOrderedEnumerable<TagRelation> OrderByCreated(IEnumerable<TagRelation> rows) =>
        rows.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id);

    private static RelationDto ToDto(TagRelation row) =>
        new(row.Id, row.FromTagId, row.ToTagId, row.Kind, row.Seq, row.CreatedAt, row.UpdatedAt, row.Revision);

    private static RelationWriteDto ToWrite(TagRelation row, int version) =>
        new(row.Id, row.FromTagId, row.ToTagId, row.Kind, row.Seq, row.CreatedAt, row.UpdatedAt, row.Revision, version);

    private static NeighborDto ToNeighbor(TagRelation row, Guid tagId)
    {
        var outgoing = row.FromTagId == tagId;
        var peer = outgoing ? row.ToTag : row.FromTag;
        return new NeighborDto(
            row.Id,
            row.FromTagId,
            row.ToTagId,
            row.Kind,
            row.Seq,
            outgoing ? "out" : "in",
            row.CreatedAt,
            row.UpdatedAt,
            row.Revision,
            new NeighborTagDto(peer.Id, peer.Name, peer.Namespace, peer.NameInitials));
    }

    private static ServiceResult<T> GraphFail<T>(RelationGraphFailure failure, bool includeIndex) =>
        ServiceResult<T>.Fail(
            failure.Code,
            failure.Message,
            includeIndex ? Details(failure.Index, failure) : new
            {
                fromTagId = failure.FromTagId,
                toTagId = failure.ToTagId,
                kind = failure.Kind,
            });

    private static object Details(int index, RelationGraphFailure failure) =>
        new
        {
            index,
            fromTagId = failure.FromTagId,
            toTagId = failure.ToTagId,
            kind = failure.Kind,
        };

    private static object Details(int index, CreateRelationRequest body) =>
        new { index, fromTagId = body.FromTagId, toTagId = body.ToTagId, kind = body.Kind };

    private static ServiceResult<T> NotFound<T>() =>
        ServiceResult<T>.Fail(ErrorCodes.NotFound, "Relation not found.");

    private static ServiceResult<TOut> Fail<TOut, TIn>(ServiceResult<TIn> source) =>
        ServiceResult<TOut>.Fail(source.Code!, source.Message!, source.Details);
}
