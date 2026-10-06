namespace EverythingLabeled.Client;

public static class FileTarget
{
    public static ElResult<string> ResolveKindForTag(string canonicalFileUri)
    {
        if (!Uri.TryCreate(canonicalFileUri, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeFile)
        {
            return NotFound();
        }

        var local = uri.LocalPath;
        var linkTarget = ResolveFinalLink(local);
        if (linkTarget is not null)
        {
            if (!linkTarget.Exists)
                return NotFound();
            return ElResult<string>.Ok(linkTarget is DirectoryInfo ? "folder" : "file");
        }

        if (Directory.Exists(local))
            return ElResult<string>.Ok("folder");
        if (File.Exists(local))
            return ElResult<string>.Ok("file");
        return NotFound();
    }

    private static FileSystemInfo? ResolveFinalLink(string local)
    {
        try
        {
            var fileTarget = File.ResolveLinkTarget(local, returnFinalTarget: true);
            if (fileTarget is not null)
                return fileTarget;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
        }

        try
        {
            return Directory.ResolveLinkTarget(local, returnFinalTarget: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static ElResult<string> NotFound() =>
        ElResult<string>.Fail(new ElError(ElErrorCodes.PathNotFound, "Path not found."));
}
