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

        if (!TryParse(uri.Trim(), out var parsed) || parsed is null)
        {
            return ServiceResult<(string Uri, string Kind)>.Fail(
                ErrorCodes.ValidationFailed,
                "URI is invalid.");
        }

        var scheme = parsed.Scheme.ToLowerInvariant();
        string normalized;
        string inferredKind;

        if (scheme is "http" or "https")
        {
            normalized = NormalizeHttp(parsed, scheme);
            inferredKind = "url";
        }
        else if (scheme is "file")
        {
            normalized = NormalizeFile(parsed);
            inferredKind = "file";
        }
        else
        {
            return ServiceResult<(string Uri, string Kind)>.Fail(
                ErrorCodes.ValidationFailed,
                "URI scheme is not supported.");
        }

        var kind = string.IsNullOrWhiteSpace(kindHint)
            ? inferredKind
            : kindHint.Trim().ToLowerInvariant();

        if (!IsCompatible(scheme, kind))
        {
            return ServiceResult<(string Uri, string Kind)>.Fail(
                ErrorCodes.ValidationFailed,
                "URI scheme is incompatible with kind.",
                new { reason = "kind_uri_mismatch" });
        }

        return ServiceResult<(string Uri, string Kind)>.Ok((normalized, kind));
    }

    private static bool TryParse(string raw, out Uri? parsed)
    {
        if (Uri.TryCreate(raw, UriKind.Absolute, out parsed) && parsed.Scheme is not "")
            return true;

        try
        {
            parsed = new Uri(Path.GetFullPath(raw));
            return parsed.IsFile;
        }
        catch (Exception ex) when (ex is ArgumentException or UriFormatException or NotSupportedException)
        {
            parsed = null;
            return false;
        }
    }

    private static string NormalizeHttp(Uri parsed, string scheme)
    {
        var builder = new UriBuilder(parsed)
        {
            Scheme = scheme,
            Host = parsed.Host.ToLowerInvariant(),
            Fragment = string.Empty,
        };

        if ((scheme == "http" && builder.Port == 80) || (scheme == "https" && builder.Port == 443))
            builder.Port = -1;

        return builder.Uri.GetComponents(UriComponents.HttpRequestUrl, UriFormat.UriEscaped);
    }

    private static string NormalizeFile(Uri parsed)
    {
        if (parsed.Host is { Length: > 0 } host && !IsWindowsDriveHost(host))
        {
            var uncPath = parsed.AbsolutePath.Replace('\\', '/');
            if (uncPath.Length > 1 && uncPath.EndsWith('/'))
                uncPath = uncPath.TrimEnd('/');
            return $"file://{host.ToLowerInvariant()}{uncPath}";
        }

        var local = parsed.LocalPath.Replace('\\', '/');
        if (local.Length >= 2 && char.IsLetter(local[0]) && local[1] == ':')
            local = char.ToUpperInvariant(local[0]) + local[1..];

        if (IsDriveRoot(local))
        {
            if (!local.EndsWith('/'))
                local += "/";
        }
        else if (local.EndsWith('/') && local.Length > 1)
        {
            local = local.TrimEnd('/');
        }

        return "file://" + (local.StartsWith('/') ? local : "/" + local);
    }

    private static bool IsWindowsDriveHost(string host) =>
        host.Length == 1 && char.IsLetter(host[0]);

    private static bool IsDriveRoot(string local) =>
        local.Length is 2 or 3 && char.IsLetter(local[0]) && local[1] == ':';

    private static bool IsCompatible(string scheme, string kind) => (scheme, kind) switch
    {
        ("http", "url") => true,
        ("https", "url") => true,
        ("file", "file") => true,
        ("file", "folder") => true,
        _ => false,
    };
}
