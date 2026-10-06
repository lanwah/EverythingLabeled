using System.Text.Json;

namespace EverythingLabeled.Client;

public sealed record ElConfig(string BaseUrl, string Token, bool ConfirmUntag);

public sealed record ElBaseUrlConfig(string BaseUrl, bool ConfirmUntag);

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
        if (!TryReadMerged(overrides, env, defaultConfigPath, out var merged, out var error))
            return ElResult<ElConfig>.Fail(error);

        if (!TryNormalizeBaseUrl(merged.BaseUrlRaw, out var baseUrl) || string.IsNullOrWhiteSpace(merged.TokenRaw))
            return ElResult<ElConfig>.Fail(MissingError());

        return ElResult<ElConfig>.Ok(new ElConfig(baseUrl, merged.TokenRaw.Trim(), merged.ConfirmUntag));
    }

    public static ElResult<ElBaseUrlConfig> LoadBaseUrl(
        ElConfigOverrides overrides,
        IReadOnlyDictionary<string, string?> env,
        string defaultConfigPath)
    {
        if (!TryReadMerged(overrides, env, defaultConfigPath, out var merged, out var error))
            return ElResult<ElBaseUrlConfig>.Fail(error);

        if (!TryNormalizeBaseUrl(merged.BaseUrlRaw, out var baseUrl))
            return ElResult<ElBaseUrlConfig>.Fail(MissingError());

        return ElResult<ElBaseUrlConfig>.Ok(new ElBaseUrlConfig(baseUrl, merged.ConfirmUntag));
    }

    private static ElError MissingError() =>
        new(ElErrorCodes.ConfigMissing, "Configuration is missing or invalid.");

    private static bool TryReadMerged(
        ElConfigOverrides overrides,
        IReadOnlyDictionary<string, string?> env,
        string defaultConfigPath,
        out MergedFields merged,
        out ElError error)
    {
        merged = default;
        error = default;
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
                error = MissingError();
                return false;
            }
            catch (IOException)
            {
                error = MissingError();
                return false;
            }
        }

        merged = new MergedFields(
            FirstNonEmpty(overrides.BaseUrlFlag, Env(env, "EL_BASE_URL"), file?.BaseUrl),
            FirstNonEmpty(overrides.TokenFlag, Env(env, "EL_TOKEN"), file?.Token),
            file?.ConfirmUntag ?? true);
        return true;
    }

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

    private readonly record struct MergedFields(string? BaseUrlRaw, string? TokenRaw, bool ConfirmUntag);
}
