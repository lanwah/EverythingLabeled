using System.Net;
using System.Net.Sockets;
using EverythingLabeled.Service.Auth;
using EverythingLabeled.Service.Data;
using EverythingLabeled.Service.Hosting;
using EverythingLabeled.Service.Http;
using EverythingLabeled.Service.Logging;
using Microsoft.EntityFrameworkCore;
using Serilog;

// Avoid running the host when design-time tools (e.g. dotnet ef) resolve the entry assembly.
if (IsEfDesignTime())
    return 0;

return await ServiceRuntime.RunAsync(args);

static bool IsEfDesignTime() =>
    AppDomain.CurrentDomain.GetAssemblies()
        .Any(a => a.GetName().Name is "Microsoft.EntityFrameworkCore.Design");

internal static class ServiceRuntime
{
    public static async Task<int> RunAsync(string[] args)
    {
        var paths = AppPaths.Resolve();
        using var mutex = SingleInstanceMutex.TryAcquire(AppPaths.ResolveMutexName());
        if (mutex is null)
            return 0;

        AppConfig config;
        try
        {
            config = ConfigBootstrap.LoadOrBootstrap(paths);
        }
        catch (InvalidConfigException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Config bootstrap failed: {ex.Message}");
            return 1;
        }

        Directory.CreateDirectory(paths.DataDirectory);
        Directory.CreateDirectory(paths.LogsDirectory);

        var minimumLevel = ReadSerilogMinimumLevel(args);
        SerilogSetup.Configure(paths.LogsDirectory, minimumLevel);

        try
        {
            Log.Information("Starting EverythingLabeled.Service on {BaseUrl}", config.BaseUrl);

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                Args = args,
                ContentRootPath = AppContext.BaseDirectory,
            });

            builder.Host.UseSerilog();
            // Bind only loopback via Listen; clear inherited ASPNETCORE_URLS / launchSettings URLs.
            builder.WebHost.UseSetting(WebHostDefaults.ServerUrlsKey, string.Empty);
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Listen(IPAddress.Loopback, config.Port);
            });

            builder.Services.AddSingleton(config);
            builder.Services.AddSingleton(paths);
            builder.Services.ConfigureHttpJsonOptions(options =>
            {
                options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
            });

            var connectionString = DatabaseInitializer.BuildConnectionString(paths.DatabasePath);
            builder.Services.AddDbContext<ElDbContext>(options => options.UseSqlite(connectionString));

            await using var app = builder.Build();

            try
            {
                await using (var scope = app.Services.CreateAsyncScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<ElDbContext>();
                    await DatabaseInitializer.InitializeAsync(db);
                }
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Database migration failed; leaving database file in place at {DatabasePath}", paths.DatabasePath);
                return 1;
            }

            app.UseMiddleware<ExceptionEnvelopeMiddleware>();
            app.UseSerilogRequestLogging(options =>
            {
                options.MessageTemplate =
                    "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
                options.GetLevel = (_, _, _) => Serilog.Events.LogEventLevel.Information;
            });
            app.UseMiddleware<BearerAuthMiddleware>();
            app.UseV1EnvelopeStatusPages();

            app.MapHealthEndpoints();

            await app.RunAsync();
            return 0;
        }
        catch (IOException ex) when (IsAddressInUse(ex))
        {
            Log.Fatal(ex, "Port {Port} is already in use; config was not modified", config.Port);
            return 1;
        }
        catch (SocketException ex) when (ex.SocketErrorCode is SocketError.AddressAlreadyInUse or SocketError.AccessDenied)
        {
            Log.Fatal(ex, "Port {Port} is already in use; config was not modified", config.Port);
            return 1;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Service failed to start");
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    private static string? ReadSerilogMinimumLevel(string[] args)
    {
        try
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true)
                .AddCommandLine(args)
                .Build();
            return config["Serilog:MinimumLevel"];
        }
        catch
        {
            return null;
        }
    }

    private static bool IsAddressInUse(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is SocketException socket &&
                socket.SocketErrorCode is SocketError.AddressAlreadyInUse or SocketError.AccessDenied)
                return true;

            if (current.Message.Contains("address already in use", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("Failed to bind", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
