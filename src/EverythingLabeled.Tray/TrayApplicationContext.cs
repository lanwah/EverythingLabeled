using EverythingLabeled.ExplorerShared;

namespace EverythingLabeled.Tray;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _startupItem;
    private readonly string _trayDirectory;
    private readonly string _trayExePath;
    private readonly string _configPath;
    private readonly string _pipeName;
    private readonly ContextMenuStrip _menu;
    private HotkeyRegistration? _hotkey;
    private bool _exiting;
    private int _restarting;
    private long _lastActivateTicks;

    public TrayApplicationContext()
    {
        _trayDirectory = AppContext.BaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        _trayExePath = Environment.ProcessPath
            ?? Path.Combine(_trayDirectory, "EverythingLabeled.Tray.exe");
        _configPath = TrayConfigStore.DefaultConfigPath();
        _pipeName = FindHostIpc.ResolvePipeName();

        _menu = new ContextMenuStrip();
        _menu.Items.Add(Copy.MenuOpenFind, null, (_, _) => RequestActivateFind());
        _menu.Items.Add(Copy.MenuRestartFind, null, (_, _) => RequestRestartFind());
        _startupItem = new ToolStripMenuItem(Copy.MenuRunAtStartup)
        {
            CheckOnClick = false,
        };
        _startupItem.Click += (_, _) => ToggleStartup();
        _menu.Items.Add(_startupItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(Copy.MenuExit, null, (_, _) => ExitTray());

        _notifyIcon = new NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = Copy.BalloonTitle,
            Visible = true,
            ContextMenuStrip = _menu,
        };
        _notifyIcon.MouseClick += OnNotifyMouseClick;

        var config = TrayConfigStore.Load(_configPath);
        _startupItem.Checked = config.RunAtStartup;
        if (config.RunAtStartup)
        {
            try
            {
                StartupRun.Apply(true, _trayExePath);
            }
            catch
            {
                // Keep tray running; menu still reflects config intent.
            }
        }

        RegisterHotkeyFromConfig(config.FindHotkey);
    }

    private void OnNotifyMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
            RequestActivateFind();
    }

    private void RequestActivateFind()
    {
        // Coalesce rapid repeats without delaying the first press.
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastActivateTicks);
        if (last != 0 && now - last < FindLauncher.DebounceMs)
            return;
        Interlocked.Exchange(ref _lastActivateTicks, now);
        ActivateFind();
    }

    private void RequestRestartFind()
    {
        if (_exiting)
            return;
        if (Interlocked.CompareExchange(ref _restarting, 1, 0) != 0)
            return;

        Task.Run(() =>
        {
            FindRestartResult result;
            try
            {
                result = FindLauncher.TryRestart(_trayDirectory, TimeSpan.FromSeconds(5));
            }
            catch
            {
                result = FindRestartResult.StartFailed;
            }

            PostToUi(() =>
            {
                try
                {
                    if (_exiting || result == FindRestartResult.Started)
                        return;

                    var message = result == FindRestartResult.HostMissing
                        ? Copy.HostMissing
                        : Copy.RestartFailed;
                    _notifyIcon.ShowBalloonTip(4000, Copy.BalloonTitle, message, ToolTipIcon.Error);
                }
                finally
                {
                    Interlocked.Exchange(ref _restarting, 0);
                }
            });
        });
    }

    private void PostToUi(Action action)
    {
        if (_menu.IsHandleCreated)
            _menu.BeginInvoke(action);
        else
            action();
    }

    private void ActivateFind()
    {
        if (_exiting || Volatile.Read(ref _restarting) != 0)
            return;

        if (FindLauncher.TryActivateOrStart(_trayDirectory, _pipeName, TimeSpan.FromSeconds(1)))
            return;

        _notifyIcon.ShowBalloonTip(
            4000,
            Copy.BalloonTitle,
            Copy.HostMissing,
            ToolTipIcon.Error);
    }

    private void RegisterHotkeyFromConfig(string findHotkey)
    {
        var kind = HotkeyParser.Parse(findHotkey, out var spec);
        if (kind == HotkeyParseKind.Disabled)
            return;

        if (kind == HotkeyParseKind.FallbackDefault)
            spec = ParseDefaultSpec();

        if (HotkeyRegistration.TryRegister(spec, RequestActivateFind, out var reg, out _))
        {
            _hotkey = reg;
            return;
        }

        _notifyIcon.ShowBalloonTip(
            5000,
            Copy.BalloonTitle,
            Copy.HotkeyRegisterFailed,
            ToolTipIcon.Warning);
    }

    private static HotkeySpec ParseDefaultSpec()
    {
        HotkeyParser.Parse(HotkeyParser.DefaultChord, out var spec);
        return spec;
    }

    private void ToggleStartup()
    {
        var desired = !_startupItem.Checked;
        if (!TrayConfigStore.TrySetRunAtStartup(_configPath, desired))
        {
            _notifyIcon.ShowBalloonTip(
                4000,
                Copy.BalloonTitle,
                Copy.StartupRunFailed,
                ToolTipIcon.Warning);
            return;
        }

        try
        {
            StartupRun.Apply(desired, _trayExePath);
            _startupItem.Checked = desired;
        }
        catch
        {
            TrayConfigStore.TrySetRunAtStartup(_configPath, !desired);
            _notifyIcon.ShowBalloonTip(
                4000,
                Copy.BalloonTitle,
                Copy.StartupRunFailed,
                ToolTipIcon.Warning);
        }
    }

    private void ExitTray()
    {
        if (_exiting)
            return;
        _exiting = true;

        _hotkey?.Dispose();
        _hotkey = null;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        ExitThread();
    }

    private static Icon LoadTrayIcon()
    {
        var icoPath = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "tray.ico");
        if (File.Exists(icoPath))
            return new Icon(icoPath);

        return SystemIcons.Application;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_exiting)
        {
            _hotkey?.Dispose();
            _notifyIcon.Dispose();
        }

        base.Dispose(disposing);
    }
}
