using System.Diagnostics;
using System.Runtime.InteropServices;

namespace EverythingLabeled.ExplorerCommand;

internal static class HostLauncher
{
    private const string HostExeName = "EverythingLabeled.ExplorerHost.exe";
    private static int _missingHostPrompted;

    public static void StartFireAndForget(string mode, string? pathsFilePath)
    {
        var directory = ResolveCommandDirectory();
        var hostPath = Path.Combine(directory, HostExeName);
        if (!File.Exists(hostPath))
        {
            if (Interlocked.Exchange(ref _missingHostPrompted, 1) == 0)
            {
                NativeMethods.MessageBox(
                    IntPtr.Zero,
                    $"未找到 {HostExeName}（应与菜单扩展同目录）。\n目录：{directory}",
                    "EverythingLabeled",
                    0x00000010); // MB_ICONERROR
            }

            return;
        }

        var arguments = $"--mode {mode}";
        if (!string.IsNullOrEmpty(pathsFilePath))
        {
            arguments += $" --paths-file \"{pathsFilePath}\"";
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = hostPath,
            Arguments = arguments,
            UseShellExecute = false,
            WorkingDirectory = directory,
        });
    }

    private static string ResolveCommandDirectory()
    {
        var location = typeof(HostLauncher).Assembly.Location;
        if (!string.IsNullOrEmpty(location))
        {
            var dir = Path.GetDirectoryName(location);
            if (!string.IsNullOrEmpty(dir))
            {
                return dir;
            }
        }

        return AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        public static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

        public static int MessageBox(IntPtr hWnd, string text, string caption, uint type) =>
            MessageBoxW(hWnd, text, caption, type);
    }
}
