using EverythingLabeled.Service.Data;

namespace EverythingLabeled.Service.Http;

public static class HealthEndpoints
{
    private const string RootHtml =
        """
        <!DOCTYPE html>
        <html lang="zh-CN">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>EverythingLabeled</title>
        </head>
        <body>
        <h1>EverythingLabeled</h1>
        <p>本地标签服务正在运行。</p>
        <p>业务 API 在 <code>/v1/…</code>；探活：<a href="/v1/health"><code>/v1/health</code></a>（无需鉴权）。</p>
        </body>
        </html>
        """;

    public static void MapHealthEndpoints(this WebApplication app)
    {
        app.MapGet("/", () => Results.Content(RootHtml, "text/html; charset=utf-8"));

        app.MapGet("/v1/health", async (ElDbContext db, CancellationToken cancellationToken) =>
        {
            var dataVersion = await DatabaseInitializer.GetDataVersionAsync(db, cancellationToken);
            return Results.Json(
                new
                {
                    status = "ok",
                    message = (string?)null,
                    data = new
                    {
                        health = "ok",
                        dataVersion,
                    },
                },
                ApiEnvelope.SerializerOptions);
        });
    }
}
