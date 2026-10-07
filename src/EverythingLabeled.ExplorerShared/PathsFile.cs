using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace EverythingLabeled.ExplorerShared;

public static class PathsFile
{
    public static string TmpDirectory { get; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EverythingLabeled",
            "tmp");

    public const int StaleMinutes = 60;

    public static void EnsureTmpDirectory()
    {
        if (Directory.Exists(TmpDirectory))
        {
            return;
        }

        Directory.CreateDirectory(TmpDirectory);
        TryRestrictDirectoryAcl(TmpDirectory);
    }

    public static void CleanupStale(DateTimeOffset utcNow, TimeSpan maxAge)
    {
        if (!Directory.Exists(TmpDirectory))
        {
            return;
        }

        var cutoff = utcNow.UtcDateTime - maxAge;
        foreach (var file in Directory.EnumerateFiles(TmpDirectory))
        {
            try
            {
                var lastWrite = File.GetLastWriteTimeUtc(file);
                if (lastWrite <= cutoff)
                {
                    File.Delete(file);
                }
            }
            catch (IOException)
            {
                // best-effort cleanup
            }
            catch (UnauthorizedAccessException)
            {
                // best-effort cleanup
            }
        }
    }

    public static string Write(IEnumerable<string> absolutePaths)
    {
        EnsureTmpDirectory();
        CleanupStale(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(StaleMinutes));

        var path = Path.Combine(TmpDirectory, Guid.NewGuid().ToString("N") + ".txt");
        var content = string.Join('\n', absolutePaths) + '\n';
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        TryRestrictFileAcl(path);
        return path;
    }

    public static IReadOnlyList<string> ReadAndDelete(string path)
    {
        if (!File.Exists(path))
        {
            return Array.Empty<string>();
        }

        var lines = File.ReadAllLines(path, Encoding.UTF8);
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // still return content even if delete races
        }
        catch (UnauthorizedAccessException)
        {
            // still return content even if delete races
        }

        return lines;
    }

    private static void TryRestrictDirectoryAcl(string directoryPath)
    {
        try
        {
            var identity = WindowsIdentity.GetCurrent().User;
            if (identity is null)
            {
                return;
            }

            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(
                identity,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
            new DirectoryInfo(directoryPath).SetAccessControl(security);
        }
        catch (Exception)
        {
            // ACL is best-effort on Windows
        }
    }

    private static void TryRestrictFileAcl(string filePath)
    {
        try
        {
            var identity = WindowsIdentity.GetCurrent().User;
            if (identity is null)
            {
                return;
            }

            var security = new FileSecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(
                identity,
                FileSystemRights.FullControl,
                AccessControlType.Allow));
            new FileInfo(filePath).SetAccessControl(security);
        }
        catch (Exception)
        {
            // ACL is best-effort on Windows
        }
    }
}
