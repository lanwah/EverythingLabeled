using Microsoft.Win32;

namespace EverythingLabeled.Tray;

public static class StartupRun
{
    public const string ValueName = "EverythingLabeled.Tray";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static void Apply(bool enabled, string trayExeAbsolutePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Cannot open HKCU Run key.");

        if (enabled)
        {
            key.SetValue(ValueName, trayExeAbsolutePath);
            return;
        }

        key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        var value = key?.GetValue(ValueName) as string;
        return !string.IsNullOrWhiteSpace(value);
    }
}
