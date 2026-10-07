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
    private readonly System.Windows.Forms.Timer _debounceTimer;
    private HotkeyRegistration? _hotkey;
    private bool _exiting;

    public TrayApplicationContext()
    {
        _trayDirectory = AppContext.BaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        _trayExePath = Environment.ProcessPath
            ?? Path.Combine(_trayDirectory, "EverythingLabeled.Tray.exe");
        _configPath = TrayConfigStore.DefaultConfigPath();
        _pipeName = FindHostIpc.ResolvePipeName();

        _debounceTimer = new System.Windows.Forms.Timer { Interval = FindLauncher.DebounceMs };
        _debounceTimer.Tick += (_, _) =>
        {
            _debounceTimer.Stop();
            ActivateFind();
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add(Copy.MenuOpenFind, null, (_, _) => RequestActivateFind());
        _startupItem = new ToolStripMenuItem(Copy.MenuRunAtStartup)
        {
            CheckOnClick = false,
        };
        _startupItem.Click += (_, _) => ToggleStartup();
        menu.Items.Add(_startupItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Copy.MenuExit, null, (_, _) => ExitTray());

        _notifyIcon = new NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = Copy.BalloonTitle,
            Visible = true,
            ContextMenuStrip = menu,
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
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void ActivateFind()
    {
        if (_exiting)
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

        _debounceTimer.Stop();
        _debounceTimer.Dispose();
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
            _debounceTimer.Dispose();
            _hotkey?.Dispose();
            _notifyIcon.Dispose();
        }

        base.Dispose(disposing);
    }
}
