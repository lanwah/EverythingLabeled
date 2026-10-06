using System.Text.Json;

namespace EverythingLabeled.Client;

public sealed record ElConfig(string BaseUrl, string Token, bool ConfirmUntag);

public sealed record ElConfigOverrides(
    string? ConfigPathFlag,
    string? BaseUrlFlag,
    string? TokenFlag);

public static class ElConfigLoader
{
    public static ElResult<ElConfig> Load(
        ElConfigOverrides overrides,
        IReadOnlyDictionary<string, string?> env,
        string defaultConfigPath)
    {
        var configPath = FirstNonEmpty(overrides.ConfigPathFlag, Env(env, "EL_CONFIG"), defaultConfigPath);
        FileFields? file = null;

        if (File.Exists(configPath))
        {
            try
            {
                file = ReadFile(configPath);
            }
            catch (JsonException)
            {
                return Missing();
            }
            catch (IOException)
            {
                return Missing();
            }
        }

        var baseUrlRaw = FirstNonEmpty(overrides.BaseUrlFlag, Env(env, "EL_BASE_URL"), file?.BaseUrl);
        var tokenRaw = FirstNonEmpty(overrides.TokenFlag, Env(env, "EL_TOKEN"), file?.Token);
        var confirmUntag = file?.ConfirmUntag ?? true;

        if (!TryNormalizeBaseUrl(baseUrlRaw, out var baseUrl) || string.IsNullOrWhiteSpace(tokenRaw))
            return Missing();

        return ElResult<ElConfig>.Ok(new ElConfig(baseUrl, tokenRaw.Trim(), confirmUntag));
    }

    private static ElResult<ElConfig> Missing() =>
        ElResult<ElConfig>.Fail(new ElError(ElErrorCodes.ConfigMissing, "Configuration is missing or invalid."));

    private static string? Env(IReadOnlyDictionary<string, string?> env, string key) =>
        env.TryGetValue(key, out var value) ? value : null;

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private static bool TryNormalizeBaseUrl(string? raw, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var trimmed = raw.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme is not "http" and not "https")
            return false;

        var builder = new UriBuilder(uri);
        var path = builder.Path;
        if (!path.EndsWith('/'))
            builder.Path = path + "/";

        normalized = builder.Uri.AbsoluteUri;
        return true;
    }

    private static FileFields ReadFile(string path)
    {
        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new JsonException("config root must be an object");

        string? baseUrl = null;
        string? token = null;
        bool? confirmUntag = null;

        if (root.TryGetProperty("baseUrl", out var baseUrlEl) && baseUrlEl.ValueKind == JsonValueKind.String)
            baseUrl = baseUrlEl.GetString();
        if (root.TryGetProperty("token", out var tokenEl) && tokenEl.ValueKind == JsonValueKind.String)
            token = tokenEl.GetString();
        if (root.TryGetProperty("confirmUntag", out var confirmEl) &&
            (confirmEl.ValueKind is JsonValueKind.True or JsonValueKind.False))
            confirmUntag = confirmEl.GetBoolean();

        return new FileFields(baseUrl, token, confirmUntag);
    }

    private sealed record FileFields(string? BaseUrl, string? Token, bool? ConfirmUntag);
}
