using System.IO;
using System.Text.Json;
using EverythingLabeled.Client;

namespace EverythingLabeled.ExplorerHost;

public sealed record ExplorerRuntimeConfig(
    string BaseUrl,
    string Token,
    bool ConfirmUntag,
    int PreviewLimit,
    int MaxConcurrency,
    bool FindUseOr);

public static class FindTagLogicConfig
{
    public const string And = "and";
    public const string Or = "or";

    public static bool ParseUseOr(string? raw) =>
        string.Equals(raw?.Trim(), Or, StringComparison.OrdinalIgnoreCase);
}

public static class ExplorerConfigLoader
{
    public static string DefaultConfigPath { get; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "EverythingLabeled",
            "config.json");

    public static ElResult<ExplorerRuntimeConfig> Load()
    {
        var overrides = new ElConfigOverrides(null, null, null);
        IReadOnlyDictionary<string, string?> env = new Dictionary<string, string?>();
        var loaded = ElConfigLoader.Load(overrides, env, DefaultConfigPath);
        var findUseOr = ReadFindUseOr(DefaultConfigPath);
        return LoadFrom(loaded, configPathForMessage: DefaultConfigPath, findUseOr);
    }

    public static ElResult<ExplorerRuntimeConfig> LoadFrom(
        ElResult<ElConfig> loaded,
        string configPathForMessage,
        bool findUseOr = false)
    {
        if (!loaded.IsSuccess)
        {
            return ElResult<ExplorerRuntimeConfig>.Fail(
                new ElError(ElErrorCodes.ConfigMissing, Copy.ConfigInvalid(configPathForMessage)));
        }

        var cfg = loaded.Data!;
        if (!RequireExplorerBaseUrl(cfg.BaseUrl))
        {
            return ElResult<ExplorerRuntimeConfig>.Fail(
                new ElError(ElErrorCodes.ConfigMissing, Copy.ConfigInvalid(configPathForMessage)));
        }

        return ElResult<ExplorerRuntimeConfig>.Ok(new ExplorerRuntimeConfig(
            cfg.BaseUrl,
            cfg.Token,
            cfg.ConfirmUntag,
            cfg.PreviewLimit,
            cfg.MaxConcurrency,
            findUseOr));
    }

    public static bool ReadFindUseOr(string configPath)
    {
        if (!File.Exists(configPath))
            return false;

        try
        {
            using var stream = File.OpenRead(configPath);
            using var doc = JsonDocument.Parse(stream);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return false;

            if (!doc.RootElement.TryGetProperty("findTagLogic", out var logicEl)
                || logicEl.ValueKind != JsonValueKind.String)
                return false;

            return FindTagLogicConfig.ParseUseOr(logicEl.GetString());
        }
        catch (JsonException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static bool RequireExplorerBaseUrl(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
            return false;

        return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && string.Equals(uri.Host, "127.0.0.1", StringComparison.Ordinal);
    }
}
