namespace EverythingLabeled.Service.Http;

public static class EnvelopeStatusCodePages
{
    public static void UseV1EnvelopeStatusPages(this WebApplication app)
    {
        app.UseStatusCodePages(async statusCodeContext =>
        {
            var context = statusCodeContext.HttpContext;
            if (!context.Request.Path.StartsWithSegments("/v1"))
                return;

            if (context.Response.StatusCode == StatusCodes.Status404NotFound)
            {
                await ApiEnvelope.WriteErrorAsync(
                    context,
                    StatusCodes.Status404NotFound,
                    "Not found.",
                    "not_found");
            }
        });
    }
}
