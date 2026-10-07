using System.Windows;

namespace EverythingLabeled.ExplorerHost;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        if (!HostArgsParser.TryParse(e.Args, out var launchArgs, out var error))
        {
            MessageBox.Show(error, "EverythingLabeled", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        // Launch args retained for later tasks (config/HTTP/UI).
        _ = launchArgs;

        base.OnStartup(e);
    }
}
