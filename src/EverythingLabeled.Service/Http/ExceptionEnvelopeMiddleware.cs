using Serilog;

namespace EverythingLabeled.Service.Http;

public sealed class ExceptionEnvelopeMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            if (context.Response.HasStarted)
                throw;

            Log.Error(ex, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);

            if (context.Request.Path.StartsWithSegments("/v1"))
            {
                await ApiEnvelope.WriteErrorAsync(
                    context,
                    StatusCodes.Status500InternalServerError,
                    "An internal error occurred.",
                    "internal_error");
                return;
            }

            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        }
    }
}
