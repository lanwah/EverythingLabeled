using System.Collections;
using System.Globalization;
using System.Net.Http.Headers;
using System.Reflection;
using EverythingLabeled.Client;
using EverythingLabeled.Mcp.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using ModelContextProtocol.Server;
using System.CommandLine;

namespace EverythingLabeled.Mcp;

public sealed class McpInvocation
{
    public required string[] Args { get; init; }
    public required TextWriter Stdout { get; init; }
    public required TextWriter Stderr { get; init; }
    public Func<ElConfigOverrides, ElResult<ElConfig>>? LoadConfig { get; init; }
    public Func<ElConfigOverrides, ElResult<ElBaseUrlConfig>>? LoadBaseUrl { get; init; }
    public string DefaultConfigPath { get; init; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "EverythingLabeled", "config.json");
    public IReadOnlyDictionary<string, string?>? Env { get; init; }
    public Func<ElMcpRuntime, CancellationToken, Task<int>>? RunHost { get; init; }
}

public static class McpApp
{
    internal static readonly Option<string?> BaseUrlOption = new("--base-url")
    {
        Description = "Service base URL (overrides config and EL_BASE_URL).",
    };

    internal static readonly Option<string?> TokenOption = new("--token")
    {
        Description = "Bearer token (overrides config and EL_TOKEN).",
    };

    internal static readonly Option<string?> ConfigOption = new("--config")
    {
        Description = "Path to config.json (overrides EL_CONFIG).",
    };

    public static async Task<int> RunAsync(McpInvocation inv)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        var english = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentCulture = english;
        CultureInfo.CurrentUICulture = english;
        try
        {
            if (HasToken(inv.Args, "--version"))
            {
                inv.Stdout.WriteLine(GetInformationalVersion());
                return 0;
            }

            var root = BuildRoot();
            var parseResult = root.Parse(inv.Args);
            var invocation = new InvocationConfiguration
            {
                Output = inv.Stdout,
                Error = inv.Stderr,
                EnableDefaultExceptionHandler = false,
            };

            if (parseResult.Errors.Count > 0)
            {
                inv.Stderr.WriteLine(parseResult.Errors[0].Message);
                return 1;
            }

            if (HasToken(inv.Args, "--help") || HasToken(inv.Args, "-h"))
                return await parseResult.InvokeAsync(invocation).ConfigureAwait(false);

            var overrides = new ElConfigOverrides(
                parseResult.GetValue(ConfigOption),
                parseResult.GetValue(BaseUrlOption),
                parseResult.GetValue(TokenOption));

            var env = inv.Env ?? CaptureEnv();
            var loadConfig = inv.LoadConfig ?? (o => ElConfigLoader.Load(o, env, inv.DefaultConfigPath));
            var loadBaseUrl = inv.LoadBaseUrl ?? (o => ElConfigLoader.LoadBaseUrl(o, env, inv.DefaultConfigPath));

            var loaded = loadConfig(overrides);
            string? baseUrl = null;
            var token = "";
            bool hasBaseUrl;
            bool hasToken;
            bool confirmUntag;

            if (loaded.IsSuccess)
            {
                var cfg = loaded.Data!;
                hasBaseUrl = true;
                hasToken = true;
                confirmUntag = cfg.ConfirmUntag;
                baseUrl = cfg.BaseUrl;
                token = cfg.Token;
            }
            else
            {
                var baseLoaded = loadBaseUrl(overrides);
                if (baseLoaded.IsSuccess)
                {
                    var baseCfg = baseLoaded.Data!;
                    hasBaseUrl = true;
                    hasToken = false;
                    confirmUntag = baseCfg.ConfirmUntag;
                    baseUrl = baseCfg.BaseUrl;
                    token = "";
                }
                else
                {
                    hasBaseUrl = false;
                    hasToken = false;
                    confirmUntag = true;
                }
            }

            var runtime = new ElMcpRuntime
            {
                Api = null,
                HasBaseUrl = hasBaseUrl,
                HasToken = hasToken,
                ConfirmUntag = confirmUntag,
            };

            if (inv.RunHost is not null)
                return await inv.RunHost(runtime, CancellationToken.None).ConfigureAwait(false);

            return await RunStdioHostAsync(runtime, baseUrl, token, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    private static RootCommand BuildRoot()
    {
        var root = new RootCommand("EverythingLabeled MCP stdio host.")
        {
            ConfigOption,
            BaseUrlOption,
            TokenOption,
        };
        return root;
    }

    private static async Task<int> RunStdioHostAsync(
        ElMcpRuntime runtime,
        string? baseUrl,
        string token,
        CancellationToken ct)
    {
        var builder = Host.CreateApplicationBuilder(Array.Empty<string>());
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Warning);
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        if (runtime.HasBaseUrl && baseUrl is not null)
        {
            builder.Services.AddSingleton(_ =>
            {
                var http = new HttpClient
                {
                    BaseAddress = new Uri(baseUrl),
                    Timeout = TimeSpan.FromSeconds(30),
                };
                http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                return http;
            });
            builder.Services.AddSingleton<IElApi>(sp => new HttpElApi(sp.GetRequiredService<HttpClient>(), token));
        }

        builder.Services.AddSingleton(sp => runtime with { Api = sp.GetService<IElApi>() });
        builder.Services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new() { Name = "everything-labeled", Version = "0.1.0" };
            })
            .WithStdioServerTransport()
            .WithTools<ElTools>();

        using var host = builder.Build();
        await host.RunAsync(ct).ConfigureAwait(false);
        return 0;
    }

    private static IReadOnlyDictionary<string, string?> CaptureEnv()
    {
        var map = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key)
                map[key] = entry.Value as string;
        }

        return map;
    }

    private static bool HasToken(string[] args, string token)
    {
        foreach (var arg in args)
        {
            if (string.Equals(arg, token, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static string GetInformationalVersion()
    {
        var informational = typeof(McpApp).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+');
            return plus >= 0 ? informational[..plus] : informational;
        }

        var version = typeof(McpApp).Assembly.GetName().Version;
        return version is null ? "0.1.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
