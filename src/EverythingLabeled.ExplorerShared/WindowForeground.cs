using System.Runtime.InteropServices;

namespace EverythingLabeled.ExplorerShared;

/// <summary>
/// Best-effort foreground activation across processes (Tray hotkey → Find Host).
/// </summary>
public static class WindowForeground
{
    /// <summary>Allow any process to call SetForegroundWindow until the next input event.</summary>
    public static void AllowSetForegroundAny()
    {
        try
        {
            _ = NativeMethods.AllowSetForegroundWindow(NativeMethods.AsfwAny);
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>
    /// Bring <paramref name="hWnd"/> to the foreground. Tries SetForegroundWindow, then
    /// AttachThreadInput with the current foreground thread.
    /// </summary>
    public static bool TryBringToForeground(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
            return false;

        try
        {
            if (NativeMethods.IsIconic(hWnd))
                _ = NativeMethods.ShowWindow(hWnd, NativeMethods.SwRestore);

            if (NativeMethods.GetForegroundWindow() == hWnd)
                return true;

            if (NativeMethods.SetForegroundWindow(hWnd))
                return true;

            var fore = NativeMethods.GetForegroundWindow();
            var foreThread = NativeMethods.GetWindowThreadProcessId(fore, out _);
            var targetThread = NativeMethods.GetWindowThreadProcessId(hWnd, out _);
            var currentThread = NativeMethods.GetCurrentThreadId();

            var attachedFore = false;
            var attachedTarget = false;
            try
            {
                if (foreThread != 0 && foreThread != currentThread)
                    attachedFore = NativeMethods.AttachThreadInput(currentThread, foreThread, true);
                if (targetThread != 0 && targetThread != currentThread && targetThread != foreThread)
                    attachedTarget = NativeMethods.AttachThreadInput(currentThread, targetThread, true);

                _ = NativeMethods.BringWindowToTop(hWnd);
                _ = NativeMethods.ShowWindow(hWnd, NativeMethods.SwShow);
                return NativeMethods.SetForegroundWindow(hWnd);
            }
            finally
            {
                if (attachedTarget)
                    _ = NativeMethods.AttachThreadInput(currentThread, targetThread, false);
                if (attachedFore)
                    _ = NativeMethods.AttachThreadInput(currentThread, foreThread, false);
            }
        }
        catch
        {
            return false;
        }
    }

    private static class NativeMethods
    {
        public const int AsfwAny = -1;
        public const int SwRestore = 9;
        public const int SwShow = 5;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AllowSetForegroundWindow(int dwProcessId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    }
}
