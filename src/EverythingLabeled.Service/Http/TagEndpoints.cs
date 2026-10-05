using EverythingLabeled.Service.Services.Abstractions;
using EverythingLabeled.Service.Services.Tags;

namespace EverythingLabeled.Service.Http;

public static class TagEndpoints
{
    public static void MapTagEndpoints(this WebApplication app)
    {
        app.MapGet("/v1/tags/suggest", async (
            HttpContext context,
            SuggestService suggest,
            CancellationToken cancellationToken) =>
        {
            var paging = TryParsePaging(context);
            if (!paging.IsSuccess)
            {
                await context.WriteAsync(paging);
                return;
            }

            var q = context.Request.Query["q"].ToString();
            await context.WriteAsync(await suggest.SuggestAsync(q, paging.Data, cancellationToken));
        });

        app.MapGet("/v1/tags/by-alias/{alias}", async (
            string alias,
            TagService tags,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            await context.WriteAsync(await tags.GetByAliasAsync(alias, cancellationToken));
        });

        app.MapGet("/v1/tags", async (
            HttpContext context,
            TagService tags,
            CancellationToken cancellationToken) =>
        {
            var paging = TryParsePaging(context);
            if (!paging.IsSuccess)
            {
                await context.WriteAsync(paging);
                return;
            }

            string? q = context.Request.Query.ContainsKey("q")
                ? context.Request.Query["q"].ToString()
                : null;
            string? ns = context.Request.Query.ContainsKey("namespace")
                ? context.Request.Query["namespace"].ToString()
                : null;
            await context.WriteAsync(await tags.ListAsync(ns, q, paging.Data, cancellationToken));
        });

        app.MapPost("/v1/tags", async (
            CreateTagRequest body,
            TagService tags,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            await context.WriteAsync(await tags.CreateAsync(body, cancellationToken));
        });

        app.MapGet("/v1/tags/{id:guid}", async (
            Guid id,
            TagService tags,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            await context.WriteAsync(await tags.GetAsync(id, cancellationToken));
        });

        app.MapPatch("/v1/tags/{id:guid}", async (
            Guid id,
            PatchTagRequest body,
            TagService tags,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            await context.WriteAsync(await tags.PatchAsync(id, body, cancellationToken));
        });

        app.MapDelete("/v1/tags/{id:guid}", async (
            Guid id,
            TagService tags,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            await context.WriteAsync(await tags.DeleteAsync(id, cancellationToken));
        });

        app.MapPost("/v1/tags/{id:guid}/aliases", async (
            Guid id,
            AddAliasRequest body,
            TagService tags,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            await context.WriteAsync(await tags.AddAliasAsync(id, body.Alias, cancellationToken));
        });

        app.MapDelete("/v1/tags/{id:guid}/aliases/{alias}", async (
            Guid id,
            string alias,
            TagService tags,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            await context.WriteAsync(await tags.DeleteAliasAsync(id, alias, cancellationToken));
        });
    }

    private static ServiceResult<Paging> TryParsePaging(HttpContext context) =>
        Paging.TryParse(OptionalQuery(context, "offset"), OptionalQuery(context, "limit"));

    private static string? OptionalQuery(HttpContext context, string key) =>
        context.Request.Query.ContainsKey(key) ? context.Request.Query[key].ToString() : null;
}
