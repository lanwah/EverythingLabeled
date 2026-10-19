using System.Diagnostics;
using EverythingLabeled.ExplorerShared;

namespace EverythingLabeled.Tray;

public static class FindLauncher
{
    public const string HostExeName = "EverythingLabeled.ExplorerHost.exe";
    /// <summary>Coalesce window for rapid hotkey/menu repeats (first press is immediate).</summary>
    public const int DebounceMs = 300;

    private const int ColdStartForegroundPollMs = 50;
    private const int ColdStartForegroundAttempts = 40;

    public static string ResolveHostPath(string trayDirectory) =>
        Path.Combine(trayDirectory, HostExeName);

    /// <summary>先 TrySendActivate（含抢前台）；失败则 Start Host --mode find 并尽力前置。</summary>
    public static bool TryActivateOrStart(string trayDirectory, string pipeName, TimeSpan activateTimeout)
    {
        // Hotkey/menu click gives Tray brief foreground rights; grant Host before IPC.
        WindowForeground.AllowSetForegroundAny();

        if (FindHostIpc.TrySendActivate(pipeName, activateTimeout))
            return true;

        var hostPath = ResolveHostPath(trayDirectory);
        if (!File.Exists(hostPath))
            return false;

        var process = Process.Start(new ProcessStartInfo
        {
            FileName = hostPath,
            Arguments = "--mode find",
            UseShellExecute = false,
            WorkingDirectory = trayDirectory,
        });

        if (process is not null)
            TryBringColdStartToForeground(process);

        return true;
    }

    private static void TryBringColdStartToForeground(Process process)
    {
        WindowForeground.AllowSetForegroundAny();

        for (var i = 0; i < ColdStartForegroundAttempts; i++)
        {
            try
            {
                process.Refresh();
                if (process.HasExited)
                    return;

                var hwnd = process.MainWindowHandle;
                if (hwnd != IntPtr.Zero)
                {
                    WindowForeground.TryBringToForeground(hwnd);
                    return;
                }
            }
            catch
            {
                return;
            }

            Thread.Sleep(ColdStartForegroundPollMs);
        }
    }
}
