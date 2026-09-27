namespace EverythingLabeled.Service.Hosting;

public sealed class AppPaths
{
    public const string DefaultMutexName = @"Local\EverythingLabeled.Service";
    public const string ConfigFileName = "config.json";
    public const string DatabaseFileName = "el.db";
    public const string DataDirectoryName = "data";
    public const string LogsDirectoryName = "logs";

    public required string Root { get; init; }
    public string ConfigPath => Path.Combine(Root, ConfigFileName);
    public string DataDirectory => Path.Combine(Root, DataDirectoryName);
    public string LogsDirectory => Path.Combine(Root, LogsDirectoryName);
    public string DatabasePath => Path.Combine(DataDirectory, DatabaseFileName);

    public static AppPaths Resolve()
    {
        var overrideRoot = Environment.GetEnvironmentVariable("EL_APPDATA");
        var root = string.IsNullOrWhiteSpace(overrideRoot)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "EverythingLabeled")
            : Path.GetFullPath(overrideRoot);

        return new AppPaths { Root = root };
    }

    public static string ResolveMutexName()
    {
        var overrideName = Environment.GetEnvironmentVariable("EL_MUTEX_NAME");
        return string.IsNullOrWhiteSpace(overrideName) ? DefaultMutexName : overrideName;
    }
}
