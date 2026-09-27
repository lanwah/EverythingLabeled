using Serilog;
using Serilog.Events;

namespace EverythingLabeled.Service.Logging;

public static class SerilogSetup
{
    public static void Configure(string logsDirectory, string? minimumLevel = null)
    {
        Directory.CreateDirectory(logsDirectory);

        var level = ParseLevel(minimumLevel);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(level)
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.File(
                path: Path.Combine(logsDirectory, "service-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true)
            .CreateLogger();
    }

    private static LogEventLevel ParseLevel(string? minimumLevel) =>
        Enum.TryParse<LogEventLevel>(minimumLevel, ignoreCase: true, out var level)
            ? level
            : LogEventLevel.Information;
}
