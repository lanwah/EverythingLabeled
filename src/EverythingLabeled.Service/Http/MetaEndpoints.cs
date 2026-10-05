using EverythingLabeled.Service.Data;
using EverythingLabeled.Service.Services.Tags;

namespace EverythingLabeled.Service.Http;

public static class MetaEndpoints
{
    public static void MapMetaEndpoints(this WebApplication app)
    {
        app.MapGet("/v1/meta", async (
            ElDbContext db,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var dataVersion = await DatabaseInitializer.GetDataVersionAsync(db, cancellationToken);
            await ApiEnvelope.WriteOkAsync(context, new DataVersionDto(dataVersion));
        });
    }
}
