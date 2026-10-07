using System.Diagnostics;
using EverythingLabeled.PathUri;

namespace EverythingLabeled.ExplorerHost;

public enum OpenKind
{
    SelectInExplorer,
    OpenFolder,
    OpenUrl,
    Unsupported,
}

public sealed record OpenAction(OpenKind Kind, string Argument);

public static class FindOpen
{
    public static OpenAction Decide(string uri, string kind)
    {
        if (!PathUriCanonicalizer.TryCanonicalize(uri, out var target)
            || !PathUriCanonicalizer.IsKindCompatible(target.Scheme, kind))
        {
            return new OpenAction(OpenKind.Unsupported, string.Empty);
        }

        return (target.Scheme, kind) switch
        {
            ("file", "file") when TryLocalPath(target.Uri, out var filePath) =>
                new OpenAction(OpenKind.SelectInExplorer, filePath),
            ("file", "folder") when TryLocalPath(target.Uri, out var folderPath) =>
                new OpenAction(OpenKind.OpenFolder, folderPath),
            ("http", "url") or ("https", "url") =>
                new OpenAction(OpenKind.OpenUrl, target.Uri),
            _ => new OpenAction(OpenKind.Unsupported, string.Empty),
        };
    }

    public static void Execute(OpenAction action)
    {
        switch (action.Kind)
        {
            case OpenKind.SelectInExplorer:
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "/select,\"" + action.Argument + "\"",
                    UseShellExecute = true,
                });
                break;
            case OpenKind.OpenFolder:
            case OpenKind.OpenUrl:
                Process.Start(new ProcessStartInfo
                {
                    FileName = action.Argument,
                    UseShellExecute = true,
                });
                break;
            case OpenKind.Unsupported:
                break;
        }
    }

    private static bool TryLocalPath(string fileUri, out string localPath)
    {
        localPath = string.Empty;
        if (!Uri.TryCreate(fileUri, UriKind.Absolute, out var parsed) || !parsed.IsFile)
            return false;
        localPath = parsed.LocalPath;
        return localPath.Length > 0;
    }
}
