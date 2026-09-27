using EverythingLabeled.Service.Data;

namespace EverythingLabeled.Service.Http;

public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this WebApplication app)
    {
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
