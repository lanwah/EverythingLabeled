namespace EverythingLabeled.ExplorerHost;

public enum HostMode
{
    Tag,
    Untag,
    Tags,
    Find,
    Library,
    Resources,
}

public sealed record HostLaunchArgs(HostMode Mode, string? PathsFilePath);

public static class HostArgsParser
{
    public static bool TryParse(IReadOnlyList<string> argv, out HostLaunchArgs args, out string error)
    {
        args = null!;
        error = string.Empty;

        HostMode? mode = null;
        string? pathsFile = null;

        for (var i = 0; i < argv.Count; i++)
        {
            var token = argv[i];
            if (string.Equals(token, "--mode", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= argv.Count)
                {
                    error = "Missing value for --mode.";
                    return false;
                }

                i++;
                if (!TryParseMode(argv[i], out var parsed))
                {
                    error = $"Illegal mode: {argv[i]}";
                    return false;
                }

                mode = parsed;
                continue;
            }

            if (string.Equals(token, "--paths-file", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= argv.Count)
                {
                    error = "Missing value for --paths-file.";
                    return false;
                }

                i++;
                pathsFile = argv[i];
                continue;
            }

            error = $"Unknown argument: {token}";
            return false;
        }

        if (mode is null)
        {
            error = "Missing required --mode.";
            return false;
        }

        args = new HostLaunchArgs(mode.Value, pathsFile);
        return true;
    }

    private static bool TryParseMode(string value, out HostMode mode)
    {
        if (string.Equals(value, "tag", StringComparison.OrdinalIgnoreCase))
        {
            mode = HostMode.Tag;
            return true;
        }

        if (string.Equals(value, "untag", StringComparison.OrdinalIgnoreCase))
        {
            mode = HostMode.Untag;
            return true;
        }

        if (string.Equals(value, "tags", StringComparison.OrdinalIgnoreCase))
        {
            mode = HostMode.Tags;
            return true;
        }

        if (string.Equals(value, "find", StringComparison.OrdinalIgnoreCase))
        {
            mode = HostMode.Find;
            return true;
        }

        if (string.Equals(value, "library", StringComparison.OrdinalIgnoreCase))
        {
            mode = HostMode.Library;
            return true;
        }

        if (string.Equals(value, "resources", StringComparison.OrdinalIgnoreCase))
        {
            mode = HostMode.Resources;
            return true;
        }

        mode = default;
        return false;
    }
}
