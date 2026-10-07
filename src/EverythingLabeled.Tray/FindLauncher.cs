using System.Diagnostics;
using EverythingLabeled.ExplorerShared;

namespace EverythingLabeled.Tray;

public static class FindLauncher
{
    public const string HostExeName = "EverythingLabeled.ExplorerHost.exe";
    public const int DebounceMs = 300;

    public static string ResolveHostPath(string trayDirectory) =>
        Path.Combine(trayDirectory, HostExeName);

    /// <summary>先 TrySendActivate；失败则 Start Host --mode find。缺 exe → false。</summary>
    public static bool TryActivateOrStart(string trayDirectory, string pipeName, TimeSpan activateTimeout)
    {
        if (FindHostIpc.TrySendActivate(pipeName, activateTimeout))
            return true;

        var hostPath = ResolveHostPath(trayDirectory);
        if (!File.Exists(hostPath))
            return false;

        Process.Start(new ProcessStartInfo
        {
            FileName = hostPath,
            Arguments = "--mode find",
            UseShellExecute = false,
            WorkingDirectory = trayDirectory,
        });
        return true;
    }
}
