namespace EverythingLabeled.Tray;

internal static class Program
{
    private const string DefaultMutexName = @"Local\EverythingLabeled.Tray";
    private const string MutexEnvKey = "EL_TRAY_MUTEX_NAME";

    // Keep owned for process lifetime so GC does not release the named mutex.
    private static Mutex? _trayMutex;

    [STAThread]
    private static void Main()
    {
        var mutexName = ResolveMutexName();
        _trayMutex = new Mutex(initiallyOwned: true, mutexName, out var createdNew);
        if (!createdNew)
        {
            _trayMutex.Dispose();
            _trayMutex = null;
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
    }

    private static string ResolveMutexName()
    {
        var overrideName = Environment.GetEnvironmentVariable(MutexEnvKey);
        return string.IsNullOrWhiteSpace(overrideName) ? DefaultMutexName : overrideName;
    }
}
