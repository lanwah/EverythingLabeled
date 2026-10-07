using System.Runtime.InteropServices;

namespace EverythingLabeled.Tray;

internal static class NativeMethods
{
    public const int WmHotkey = 0x0312;
    public const int HotkeyId = 1;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
