using EverythingLabeled.Service.Services.Abstractions;
using Serilog;

namespace EverythingLabeled.Service.Http;

public static class HttpErrorMap
{
    public static int StatusFor(string code)
    {
        switch (code)
        {
            case ErrorCodes.BadRequest:
            case ErrorCodes.RelationsFilterRequired:
                return StatusCodes.Status400BadRequest;
            case ErrorCodes.Unauthorized:
                return StatusCodes.Status401Unauthorized;
            case ErrorCodes.NotFound:
            case ErrorCodes.TagNotFound:
            case ErrorCodes.ResourceNotFound:
                return StatusCodes.Status404NotFound;
            case ErrorCodes.AliasConflict:
            case ErrorCodes.NameConflict:
            case ErrorCodes.UriConflict:
            case ErrorCodes.RelationConflict:
            case ErrorCodes.RelationCycle:
            case ErrorCodes.ResourceIdUriMismatch:
                return StatusCodes.Status409Conflict;
            case ErrorCodes.ValidationFailed:
            case ErrorCodes.BatchLimitExceeded:
            case ErrorCodes.ExpansionLimitExceeded:
                return StatusCodes.Status422UnprocessableEntity;
            case ErrorCodes.InternalError:
                return StatusCodes.Status500InternalServerError;
            default:
                Log.Error("Unmapped error code {ErrorCode}", code);
                return StatusCodes.Status500InternalServerError;
        }
    }
}
