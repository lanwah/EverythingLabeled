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
}
