using System.Net.Http;
using System.Net.Http.Headers;
using System.Windows;
using EverythingLabeled.Client;
using EverythingLabeled.ExplorerShared;

namespace EverythingLabeled.ExplorerHost;

public partial class App : Application
{
    private HttpClient? _http;
    private FindSingleInstance? _findInstance;
    private MainWindow? _mainWindow;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!HostArgsParser.TryParse(e.Args, out var launchArgs, out var error))
        {
            MessageBox.Show(error, "EverythingLabeled", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        if (launchArgs.Mode == HostMode.Find)
        {
            var mutexName = FindHostIpc.ResolveMutexName();
            var pipeName = FindHostIpc.ResolvePipeName();
            if (!FindSingleInstance.TryAcquire(
                    mutexName,
                    pipeName,
                    () => _mainWindow?.ActivateFromIpc(),
                    out var findInstance))
            {
                FindHostIpc.TrySendActivate(pipeName, TimeSpan.FromSeconds(1));
                Shutdown(0);
                return;
            }

            _findInstance = findInstance;
        }

        string? banner = null;
        var businessEnabled = false;
        IElApi? api = null;
        ExplorerRuntimeConfig? config = null;
        SelectionResult? selection = null;

        var configResult = ExplorerConfigLoader.Load();
        if (!configResult.IsSuccess)
        {
            banner = configResult.Error!.Value.Message;
        }
        else
        {
            config = configResult.Data!;
            _http = new HttpClient
            {
                BaseAddress = new Uri(config.BaseUrl),
                Timeout = TimeSpan.FromSeconds(30),
            };
            _http.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
            api = new HttpElApi(_http, config.Token);

            var probe = await Probe.RunAsync(api, CancellationToken.None).ConfigureAwait(true);
            if (!probe.IsSuccess)
                banner = probe.Error!.Value.Message;
            else
                businessEnabled = true;
        }

        if (launchArgs.Mode is not HostMode.Find and not HostMode.Library)
        {
            IReadOnlyList<string> paths = string.IsNullOrWhiteSpace(launchArgs.PathsFilePath)
                ? Array.Empty<string>()
                : PathsFile.ReadAndDelete(launchArgs.PathsFilePath!);

            selection = SelectionNormalizer.Normalize(paths, launchArgs.Mode);
            ApplySelectionGates(selection, launchArgs.Mode, ref banner, ref businessEnabled);
        }
        else if (!string.IsNullOrWhiteSpace(launchArgs.PathsFilePath))
        {
            // library / find 忽略 paths-file；若误传则读完删除，避免残留临时文件。
            _ = PathsFile.ReadAndDelete(launchArgs.PathsFilePath!);
        }

        var ctx = new HostUiContext
        {
            Mode = launchArgs.Mode,
            Api = api,
            Config = config,
            Selection = selection,
            Banner = banner,
            BusinessEnabled = businessEnabled,
            Http = _http,
        };

        if (launchArgs.Mode == HostMode.Library)
        {
            new LibraryWindow(ctx).Show();
            return;
        }

        _mainWindow = new MainWindow(ctx);
        if (_findInstance is not null)
        {
            _mainWindow.Closed += (_, _) =>
            {
                _findInstance?.Dispose();
                _findInstance = null;
            };
        }

        _mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _findInstance?.Dispose();
        _findInstance = null;
        _http?.Dispose();
        base.OnExit(e);
    }

    private static void ApplySelectionGates(
        SelectionResult selection,
        HostMode mode,
        ref string? banner,
        ref bool businessEnabled)
    {
        if (selection.IgnoredUnsupportedCount > 0)
            AppendBanner(ref banner, Copy.IgnoredUnsupported(selection.IgnoredUnsupportedCount));

        if (selection.RejectedOverLimit)
        {
            AppendBanner(ref banner, Copy.MaxItemsExceeded);
            businessEnabled = false;
            return;
        }

        var included = selection.Items.Count(i => i.Included);

        if (mode == HostMode.Tags)
        {
            if (included != 1)
            {
                AppendBanner(ref banner, Copy.TagsNeedSinglePath);
                businessEnabled = false;
            }

            return;
        }

        if (included == 0)
        {
            AppendBanner(ref banner, Copy.NoIncludedPaths);
            businessEnabled = false;
        }
    }

    private static void AppendBanner(ref string? banner, string message)
    {
        banner = string.IsNullOrEmpty(banner) ? message : banner + Environment.NewLine + message;
    }
}
