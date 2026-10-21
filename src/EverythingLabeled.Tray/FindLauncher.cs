using System.ComponentModel;
using System.Diagnostics;
using EverythingLabeled.ExplorerShared;

namespace EverythingLabeled.Tray;

public enum FindRestartResult
{
    Started,
    HostMissing,
    StopFailed,
    StartFailed,
}

public static class FindLauncher
{
    public const string HostExeName = "EverythingLabeled.ExplorerHost.exe";
    public static readonly string HostProcessName = Path.GetFileNameWithoutExtension(HostExeName);
    /// <summary>Coalesce window for rapid hotkey/menu repeats (first press is immediate).</summary>
    public const int DebounceMs = 300;
    /// <summary>How long a newly started Find may exit before we treat the mutex as still held.</summary>
    public const int RestartStartSettleMs = 250;
    public const int RestartStartAttempts = 8;

    private const int ColdStartForegroundPollMs = 50;
    private const int ColdStartForegroundAttempts = 40;

    /// <summary>Tests substitute process shutdown so a missing-host case never touches live hosts.</summary>
    internal static Func<TimeSpan, bool>? StopHostProcessesOverride { get; set; }

    public static string ResolveHostPath(string trayDirectory) =>
        Path.Combine(trayDirectory, HostExeName);

    /// <summary>先 TrySendActivate（含抢前台）；失败则 Start Host --mode find 并尽力前置。</summary>
    public static bool TryActivateOrStart(string trayDirectory, string pipeName, TimeSpan activateTimeout)
    {
        // Hotkey/menu click gives Tray brief foreground rights; grant Host before IPC.
        WindowForeground.AllowSetForegroundAny();

        if (FindHostIpc.TrySendActivate(pipeName, activateTimeout))
            return true;

        return TryStartFind(trayDirectory) is not null;
    }

    /// <summary>
    /// 结束全部 ExplorerHost（Find 关窗只 Hide，探测结果会留在进程里），再冷启动 --mode find。
    /// </summary>
    public static FindRestartResult TryRestart(string trayDirectory, TimeSpan exitTimeout)
    {
        if (!File.Exists(ResolveHostPath(trayDirectory)))
            return FindRestartResult.HostMissing;

        if (!StopHostProcesses(exitTimeout))
            return FindRestartResult.StopFailed;

        for (var attempt = 0; attempt < RestartStartAttempts; attempt++)
        {
            using var process = TryStartFind(trayDirectory);
            if (process is null)
                return FindRestartResult.StartFailed;

            try
            {
                if (!process.WaitForExit(RestartStartSettleMs))
                    return FindRestartResult.Started;
            }
            catch (InvalidOperationException)
            {
                return FindRestartResult.Started;
            }
        }

        return FindRestartResult.StartFailed;
    }

    private static bool StopHostProcesses(TimeSpan exitTimeout)
    {
        var over = StopHostProcessesOverride;
        if (over is not null)
            return over(exitTimeout);

        return TryStopHostProcesses(exitTimeout);
    }

    private static bool TryStopHostProcesses(TimeSpan exitTimeout)
    {
        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName(HostProcessName);
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        var waitMs = ToWaitMilliseconds(exitTimeout);
        try
        {
            foreach (var process in processes)
            {
                if (!TryKill(process))
                    return false;
            }

            foreach (var process in processes)
            {
                try
                {
                    if (!process.WaitForExit(waitMs))
                        return false;
                }
                catch (InvalidOperationException)
                {
                    // already exited
                }
                catch (Win32Exception)
                {
                    return false;
                }
            }

            return true;
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
    }

    private static bool TryKill(Process process)
    {
        try
        {
            if (process.HasExited)
                return true;

            process.Kill(entireProcessTree: true);
            return true;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private static int ToWaitMilliseconds(TimeSpan exitTimeout)
    {
        if (exitTimeout < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(exitTimeout));

        var ms = (long)exitTimeout.TotalMilliseconds;
        return ms >= int.MaxValue ? int.MaxValue : (int)ms;
    }

    private static Process? TryStartFind(string trayDirectory)
    {
        var hostPath = ResolveHostPath(trayDirectory);
        if (!File.Exists(hostPath))
            return null;

        WindowForeground.AllowSetForegroundAny();

        Process? process;
        try
        {
            process = Process.Start(new ProcessStartInfo
            {
                FileName = hostPath,
                Arguments = "--mode find",
                UseShellExecute = false,
                WorkingDirectory = trayDirectory,
            });
        }
        catch (Win32Exception)
        {
            return null;
        }

        if (process is not null)
            TryBringColdStartToForeground(process);

        return process;
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
