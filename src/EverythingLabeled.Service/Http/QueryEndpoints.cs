using EverythingLabeled.Service.Services.Abstractions;
using EverythingLabeled.Service.Services.Query;

namespace EverythingLabeled.Service.Http;

public static class QueryEndpoints
{
    public static void MapQueryEndpoints(this WebApplication app)
    {
        app.MapGet("/v1/query", async (
            HttpContext context,
            QueryService queries,
            CancellationToken cancellationToken) =>
        {
            var paging = context.TryParsePaging();
            if (!paging.IsSuccess)
            {
                await context.WriteAsync(paging);
                return;
            }

            var request = new QueryRequest(
                context.OptionalQuery("q"),
                context.OptionalQuery("expand"),
                context.OptionalQuery("maxDepth"),
                context.OptionalQuery("expandedTagLimit"),
                paging.Data,
                context.OptionalQuery("kind"));
            await context.WriteAsync(await queries.QueryAsync(request, cancellationToken));
        });

        app.MapGet("/v1/query/preview", async (
            HttpContext context,
            QueryService queries,
            CancellationToken cancellationToken) =>
        {
            var paging = context.TryParsePaging();
            if (!paging.IsSuccess)
            {
                await context.WriteAsync(paging);
                return;
            }

            var request = new QueryRequest(
                context.OptionalQuery("q"),
                context.OptionalQuery("expand"),
                context.OptionalQuery("maxDepth"),
                context.OptionalQuery("expandedTagLimit"),
                paging.Data,
                context.OptionalQuery("kind"));
            await context.WriteAsync(await queries.PreviewAsync(request, cancellationToken));
        });
    }
}
