using System.Runtime.InteropServices;

namespace EverythingLabeled.Tray;

public sealed class HotkeyRegistration : IDisposable
{
    private readonly HotkeyNativeWindow _window;
    private bool _disposed;

    private HotkeyRegistration(HotkeyNativeWindow window)
    {
        _window = window;
    }

    public static bool TryRegister(HotkeySpec spec, Action onHotkey, out HotkeyRegistration? reg, out string? error)
    {
        ArgumentNullException.ThrowIfNull(onHotkey);
        reg = null;
        error = null;

        HotkeyNativeWindow? window = null;
        try
        {
            window = new HotkeyNativeWindow(onHotkey);
            if (!NativeMethods.RegisterHotKey(
                    window.Handle,
                    NativeMethods.HotkeyId,
                    spec.Modifiers,
                    spec.VirtualKey))
            {
                var code = Marshal.GetLastWin32Error();
                window.DestroyHandle();
                error = $"RegisterHotKey failed (Win32 {code}).";
                return false;
            }

            reg = new HotkeyRegistration(window);
            return true;
        }
        catch (Exception ex)
        {
            window?.DestroyHandle();
            error = ex.Message;
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        try
        {
            NativeMethods.UnregisterHotKey(_window.Handle, NativeMethods.HotkeyId);
        }
        catch
        {
            // ignore
        }

        _window.DestroyHandle();
    }

    private sealed class HotkeyNativeWindow : NativeWindow
    {
        private readonly Action _onHotkey;

        public HotkeyNativeWindow(Action onHotkey)
        {
            _onHotkey = onHotkey;
            CreateHandle(new CreateParams
            {
                Caption = "EverythingLabeled.Tray.Hotkey",
            });
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WmHotkey && m.WParam.ToInt32() == NativeMethods.HotkeyId)
            {
                try
                {
                    _onHotkey();
                }
                catch
                {
                    // ignore callback failures
                }
            }

            base.WndProc(ref m);
        }
    }
}
