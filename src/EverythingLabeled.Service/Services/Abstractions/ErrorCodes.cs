namespace EverythingLabeled.Service.Services.Abstractions;

public static class ErrorCodes
{
    public const string BadRequest = "bad_request";
    public const string RelationsFilterRequired = "relations_filter_required";
    public const string NotFound = "not_found";
    public const string TagNotFound = "tag_not_found";
    public const string ResourceNotFound = "resource_not_found";
    public const string AliasConflict = "alias_conflict";
    public const string NameConflict = "name_conflict";
    public const string UriConflict = "uri_conflict";
    public const string RelationConflict = "relation_conflict";
    public const string RelationCycle = "relation_cycle";
    public const string ResourceIdUriMismatch = "resource_id_uri_mismatch";
    public const string ValidationFailed = "validation_failed";
    public const string BatchLimitExceeded = "batch_limit_exceeded";
    public const string ExpansionLimitExceeded = "expansion_limit_exceeded";
    public const string Unauthorized = "unauthorized";
    public const string InternalError = "internal_error";
}
