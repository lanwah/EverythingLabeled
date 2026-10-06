namespace EverythingLabeled.Client;

public static class ElErrorCodes
{
    public const string ConfigMissing = "config_missing";
    public const string PathNotFound = "path_not_found";
    public const string BadRequest = "bad_request";
    public const string ConnectionFailed = "connection_failed";
}

public readonly record struct ElError(string Code, string Message, int? HttpStatus = null);
