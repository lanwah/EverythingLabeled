using EverythingLabeled.Service.Hosting;
using EverythingLabeled.Service.Http;

namespace EverythingLabeled.Service.Auth;

public sealed class BearerAuthMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, AppConfig config)
    {
        if (IsHealthGet(context))
        {
            await next(context);
            return;
        }

        if (!context.Request.Path.StartsWithSegments("/v1"))
        {
            await next(context);
            return;
        }

        if (!TryGetBearerToken(context.Request, out var token) ||
            !TokenComparer.FixedTimeEquals(config.Token, token))
        {
            await ApiEnvelope.WriteErrorAsync(
                context,
                StatusCodes.Status401Unauthorized,
                "Unauthorized.",
                "unauthorized");
            return;
        }

        await next(context);
    }

    private static bool IsHealthGet(HttpContext context) =>
        HttpMethods.IsGet(context.Request.Method) &&
        context.Request.Path.Equals("/v1/health", StringComparison.OrdinalIgnoreCase);

    private static bool TryGetBearerToken(HttpRequest request, out string token)
    {
        token = string.Empty;
        var header = request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header))
            return false;

        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        token = header[prefix.Length..].Trim();
        return token.Length > 0;
    }
}
