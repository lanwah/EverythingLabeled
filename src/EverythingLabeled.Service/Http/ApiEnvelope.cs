using System.Text.Json;
using System.Text.Json.Serialization;

namespace EverythingLabeled.Service.Http;

public static class ApiEnvelope
{
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static async Task WriteAsync(HttpContext context, int statusCode, object envelope)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(context.Response.Body, envelope, SerializerOptions, context.RequestAborted);
    }

    public static Task WriteOkAsync(HttpContext context, object? data, string? message = null) =>
        WriteAsync(context, StatusCodes.Status200OK, new
        {
            status = "ok",
            message,
            data,
        });

    public static Task WriteErrorAsync(
        HttpContext context,
        int statusCode,
        string message,
        string code,
        object? details = null)
    {
        object data = details is null
            ? new { code }
            : new { code, details };
        return WriteAsync(context, statusCode, new
        {
            status = "error",
            message,
            data,
        });
    }
}
