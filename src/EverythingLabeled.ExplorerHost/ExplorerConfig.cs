using System.IO;
using EverythingLabeled.Client;

namespace EverythingLabeled.ExplorerHost;

public sealed record ExplorerRuntimeConfig(
    string BaseUrl,
    string Token,
    bool ConfirmUntag,
    int PreviewLimit,
    int MaxConcurrency);

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
        return LoadFrom(loaded, DefaultConfigPath);
    }

    public static ElResult<ExplorerRuntimeConfig> LoadFrom(
        ElResult<ElConfig> loaded,
        string configPathForMessage)
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
            cfg.MaxConcurrency));
    }

    private static bool RequireExplorerBaseUrl(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
            return false;

        return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && string.Equals(uri.Host, "127.0.0.1", StringComparison.Ordinal);
    }
}
