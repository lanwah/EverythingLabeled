using EverythingLabeled.PathUri;
using EverythingLabeled.Service.Services.Abstractions;

namespace EverythingLabeled.Service.Services.Resources;

public static class UriNormalizer
{
    public static ServiceResult<(string Uri, string Kind)> Normalize(string uri, string? kindHint)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            return ServiceResult<(string Uri, string Kind)>.Fail(
                ErrorCodes.ValidationFailed,
                "URI is required.");
        }

        var trimmed = uri.Trim();
        if (!PathUriCanonicalizer.TryCanonicalize(trimmed, out var canonical))
        {
            if (Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed) && parsed.Scheme is not "")
            {
                return ServiceResult<(string Uri, string Kind)>.Fail(
                    ErrorCodes.ValidationFailed,
                    "URI scheme is not supported.");
            }

            return ServiceResult<(string Uri, string Kind)>.Fail(
                ErrorCodes.ValidationFailed,
                "URI is invalid.");
        }

        var inferredKind = canonical.Scheme is "http" or "https" ? "url" : "file";
        var kind = string.IsNullOrWhiteSpace(kindHint)
            ? inferredKind
            : kindHint.Trim().ToLowerInvariant();

        if (!PathUriCanonicalizer.IsKindCompatible(canonical.Scheme, kind))
        {
            return ServiceResult<(string Uri, string Kind)>.Fail(
                ErrorCodes.ValidationFailed,
                "URI scheme is incompatible with kind.",
                new { reason = "kind_uri_mismatch" });
        }

        return ServiceResult<(string Uri, string Kind)>.Ok((canonical.Uri, kind));
    }
}
