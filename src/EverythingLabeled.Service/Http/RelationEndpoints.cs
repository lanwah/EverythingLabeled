using EverythingLabeled.Service.Services.Abstractions;
using EverythingLabeled.Service.Services.Relations;

namespace EverythingLabeled.Service.Http;

public static class RelationEndpoints
{
    public static void MapRelationEndpoints(this WebApplication app)
    {
        app.MapGet("/v1/relations", async (
            HttpContext context,
            RelationService relations,
            CancellationToken cancellationToken) =>
        {
            var paging = context.TryParsePaging();
            if (!paging.IsSuccess)
            {
                await context.WriteAsync(paging);
                return;
            }

            if (!TryParseGuidQuery(context, "fromTagId", out var from, out ServiceResult<Paged<RelationDto>> fromError))
            {
                await context.WriteAsync(fromError);
                return;
            }

            if (!TryParseGuidQuery(context, "toTagId", out var to, out ServiceResult<Paged<RelationDto>> toError))
            {
                await context.WriteAsync(toError);
                return;
            }

            await context.WriteAsync(await relations.ListAsync(
                from,
                to,
                context.OptionalQuery("kind"),
                paging.Data,
                cancellationToken));
        });

        app.MapPost("/v1/relations/batch", async (
            CreateRelationBatchRequest body,
            RelationService relations,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var items = body.Items ?? [];
            await context.WriteAsync(await relations.CreateBatchAsync(items, cancellationToken));
        });

        app.MapPost("/v1/relations/reorder", async (
            ReorderRelationsRequest body,
            RelationService relations,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (body.Kind is null || body.OrderedToTagIds is null)
            {
                await context.WriteAsync(ServiceResult<RelationReorderResultDto>.Fail(
                    ErrorCodes.ValidationFailed,
                    "kind and orderedToTagIds are required."));
                return;
            }

            await context.WriteAsync(await relations.ReorderAsync(
                body.FromTagId,
                body.Kind,
                body.OrderedToTagIds,
                cancellationToken));
        });

        app.MapPost("/v1/relations", async (
            CreateRelationRequest body,
            RelationService relations,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            await context.WriteAsync(await relations.CreateAsync(body, cancellationToken));
        });

        app.MapGet("/v1/relations/{id:guid}", async (
            Guid id,
            RelationService relations,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            await context.WriteAsync(await relations.GetAsync(id, cancellationToken));
        });

        app.MapPatch("/v1/relations/{id:guid}", async (
            Guid id,
            PatchRelationRequest body,
            RelationService relations,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            await context.WriteAsync(await relations.PatchSeqAsync(id, body.Seq, cancellationToken));
        });

        app.MapDelete("/v1/relations/{id:guid}", async (
            Guid id,
            RelationService relations,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            await context.WriteAsync(await relations.DeleteAsync(id, cancellationToken));
        });
    }

    private static bool TryParseGuidQuery<T>(
        HttpContext context,
        string key,
        out Guid? value,
        out ServiceResult<T> error)
    {
        value = null;
        error = null!;
        if (!context.Request.Query.ContainsKey(key))
            return true;

        var raw = context.Request.Query[key].ToString();
        if (!Guid.TryParse(raw, out var parsed))
        {
            error = ServiceResult<T>.Fail(
                ErrorCodes.BadRequest,
                $"Invalid {key}.",
                new { reason = "param_invalid" });
            return false;
        }

        value = parsed;
        return true;
    }
}
