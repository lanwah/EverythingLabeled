using EverythingLabeled.Service.Services.Abstractions;

namespace EverythingLabeled.Service.Http;

public static class EndpointExtensions
{
    public static Task WriteAsync<T>(this HttpContext context, ServiceResult<T> result)
    {
        if (result.IsSuccess)
            return ApiEnvelope.WriteOkAsync(context, result.Data, result.Message);

        return ApiEnvelope.WriteErrorAsync(
            context,
            HttpErrorMap.StatusFor(result.Code ?? ErrorCodes.InternalError),
            result.Message ?? "Request failed.",
            result.Code ?? ErrorCodes.InternalError,
            result.Details);
    }

    public static ServiceResult<Paging> TryParsePaging(this HttpContext context) =>
        Paging.TryParse(OptionalQuery(context, "offset"), OptionalQuery(context, "limit"));

    public static string? OptionalQuery(this HttpContext context, string key) =>
        context.Request.Query.ContainsKey(key) ? context.Request.Query[key].ToString() : null;
}
