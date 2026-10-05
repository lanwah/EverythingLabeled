using EverythingLabeled.Service.Services.Abstractions;
using EverythingLabeled.Service.Services.Resources;
using EverythingLabeled.Service.Services.Tags;

namespace EverythingLabeled.Service.Http;

public static class ResourceEndpoints
{
    public static void MapResourceEndpoints(this WebApplication app)
    {
        app.MapPost("/v1/resources/tags", async (
            TagApplyRequest body,
            ResourceTagService resourceTags,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            await context.WriteAsync(await resourceTags.ApplyAsync(body, cancellationToken));
        });

        app.MapGet("/v1/resources/tags", async (
            HttpContext context,
            ResourceTagService resourceTags,
            CancellationToken cancellationToken) =>
        {
            var paging = context.TryParsePaging();
            if (!paging.IsSuccess)
            {
                await context.WriteAsync(paging);
                return;
            }

            Guid? resourceId = null;
            if (context.Request.Query.ContainsKey("resourceId"))
            {
                var raw = context.Request.Query["resourceId"].ToString();
                if (!Guid.TryParse(raw, out var parsed))
                {
                    await context.WriteAsync(ServiceResult<Paged<TagDto>>.Fail(
                        ErrorCodes.BadRequest,
                        "Invalid resourceId.",
                        new { reason = "param_invalid" }));
                    return;
                }

                resourceId = parsed;
            }

            var uri = context.OptionalQuery("uri");
            await context.WriteAsync(await resourceTags.ListTagsAsync(resourceId, uri, paging.Data, cancellationToken));
        });

        app.MapGet("/v1/resources/by-uri", async (
            HttpContext context,
            ResourceService resources,
            CancellationToken cancellationToken) =>
        {
            var uri = context.OptionalQuery("uri");
            if (string.IsNullOrWhiteSpace(uri))
            {
                await context.WriteAsync(ServiceResult<ResourceDetailDto>.Fail(
                    ErrorCodes.ValidationFailed,
                    "uri is required."));
                return;
            }

            await context.WriteAsync(await resources.GetByUriAsync(uri, cancellationToken));
        });

        app.MapGet("/v1/resources", async (
            HttpContext context,
            ResourceService resources,
            CancellationToken cancellationToken) =>
        {
            var paging = context.TryParsePaging();
            if (!paging.IsSuccess)
            {
                await context.WriteAsync(paging);
                return;
            }

            await context.WriteAsync(await resources.ListAsync(
                context.OptionalQuery("kind"),
                context.OptionalQuery("uri"),
                paging.Data,
                cancellationToken));
        });

        app.MapPost("/v1/resources", async (
            CreateResourceRequest body,
            ResourceService resources,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            await context.WriteAsync(await resources.CreateAsync(body, cancellationToken));
        });

        app.MapGet("/v1/resources/{id:guid}", async (
            Guid id,
            ResourceService resources,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            await context.WriteAsync(await resources.GetAsync(id, cancellationToken));
        });

        app.MapPatch("/v1/resources/{id:guid}", async (
            Guid id,
            PatchResourceRequest body,
            ResourceService resources,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            await context.WriteAsync(await resources.PatchAsync(id, body, cancellationToken));
        });

        app.MapDelete("/v1/resources/{id:guid}", async (
            Guid id,
            ResourceService resources,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            await context.WriteAsync(await resources.DeleteAsync(id, cancellationToken));
        });
    }
}
