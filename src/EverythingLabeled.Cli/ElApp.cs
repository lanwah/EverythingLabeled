using System.Collections;
using System.Globalization;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using EverythingLabeled.Client;
using System.CommandLine;

namespace EverythingLabeled.Cli;

public sealed class ElInvocation
{
    public required string[] Args { get; init; }
    public required TextWriter Stdout { get; init; }
    public required TextWriter Stderr { get; init; }
    public TextReader Stdin { get; init; } = TextReader.Null;
    public bool IsTty { get; init; }
    public IElApi? Api { get; init; }
    public Func<ElConfigOverrides, ElResult<ElConfig>>? LoadConfig { get; init; }
    public string DefaultConfigPath { get; init; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EverythingLabeled", "config.json");
    public CancellationToken CancellationToken { get; init; }
}

public static class ElApp
{
    public static async Task<int> RunAsync(ElInvocation inv)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        var english = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentCulture = english;
        CultureInfo.CurrentUICulture = english;
        try
        {
            inv.CancellationToken.ThrowIfCancellationRequested();

            // VersionOption cannot be combined with other tokens (e.g. --json); handle offline.
            if (HasToken(inv.Args, "--version"))
            {
                inv.Stdout.WriteLine(GetInformationalVersion());
                return ExitCodes.Ok;
            }

            var root = BuildRoot();
            var parseResult = root.Parse(inv.Args);
            var json = HasToken(inv.Args, "--json") || parseResult.GetValue(JsonOption);

            if (parseResult.Errors.Count > 0 && json)
            {
                var message = parseResult.Errors[0].Message;
                WriteError(inv, json: true, new ElError(ElErrorCodes.BadRequest, message));
                return ExitCodes.Usage;
            }

            var invocation = new InvocationConfiguration
            {
                Output = inv.Stdout,
                Error = inv.Stderr,
                EnableDefaultExceptionHandler = false,
            };

            return await parseResult.InvokeAsync(invocation, inv.CancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            WriteError(
                inv,
                HasToken(inv.Args, "--json"),
                new ElError(ElErrorCodes.ConnectionFailed, "Unable to reach the service."));
            return ExitCodes.ConfigOrConnection;
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    internal static readonly Option<bool> JsonOption = new("--json")
    {
        Recursive = true,
        Description = "Write machine-readable JSON on stdout (errors on stderr).",
    };

    internal static readonly Option<string?> BaseUrlOption = new("--base-url")
    {
        Recursive = true,
        Description = "Service base URL (overrides config and EL_BASE_URL).",
    };

    internal static readonly Option<string?> TokenOption = new("--token")
    {
        Recursive = true,
        Description = "Bearer token (overrides config and EL_TOKEN).",
    };

    internal static readonly Option<string?> ConfigOption = new("--config")
    {
        Recursive = true,
        Description = "Path to config.json (overrides EL_CONFIG).",
    };

    private static RootCommand BuildRoot()
    {
        var root = new RootCommand("Tag local files and URLs via the EverythingLabeled service.");
        root.Options.Add(JsonOption);
        root.Options.Add(BaseUrlOption);
        root.Options.Add(TokenOption);
        root.Options.Add(ConfigOption);
        root.Options.Add(new VersionOption());
        return root;
    }

    internal static void WriteError(ElInvocation inv, bool json, ElError error)
    {
        if (json)
        {
            var payload = new JsonErrorEnvelope(new JsonErrorBody(error.Code, error.Message));
            inv.Stderr.Write(JsonSerializer.Serialize(payload, ElJson.Options));
            return;
        }

        inv.Stderr.WriteLine(error.Message);
    }

    internal static ElResult<ElConfig> LoadConfig(ElInvocation inv, ElConfigOverrides overrides)
    {
        var load = inv.LoadConfig ?? (o => ElConfigLoader.Load(o, CaptureEnv(), inv.DefaultConfigPath));
        return load(overrides);
    }

    internal static async Task<int> RunNeedingNetworkAsync(
        ElInvocation inv,
        ElConfigOverrides overrides,
        bool json,
        Func<IElApi, ElConfig, CancellationToken, Task<int>> action)
    {
        inv.CancellationToken.ThrowIfCancellationRequested();
        var loaded = LoadConfig(inv, overrides);
        if (!loaded.IsSuccess)
        {
            WriteError(inv, json, loaded.Error!.Value);
            return ExitCodes.ConfigOrConnection;
        }

        var config = loaded.Data!;
        if (inv.Api is not null)
            return await action(inv.Api, config, inv.CancellationToken).ConfigureAwait(false);

        using var http = new HttpClient
        {
            BaseAddress = new Uri(config.BaseUrl),
            Timeout = TimeSpan.FromSeconds(30),
        };
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        IElApi api = new HttpElApi(http, config.Token);
        return await action(api, config, inv.CancellationToken).ConfigureAwait(false);
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
        var informational = typeof(ElApp).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+');
            return plus >= 0 ? informational[..plus] : informational;
        }

        var version = typeof(ElApp).Assembly.GetName().Version;
        return version is null ? "0.1.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private sealed record JsonErrorEnvelope(JsonErrorBody Error);

    private sealed record JsonErrorBody(string Code, string Message);
}
