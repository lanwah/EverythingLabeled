using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace EverythingLabeled.Service.Hosting;

public static class ConfigBootstrap
{
    public const int PortRangeStart = 5123;
    public const int PortRangeEnd = 5132;

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static AppConfig LoadOrBootstrap(AppPaths paths)
    {
        if (!File.Exists(paths.ConfigPath))
            return BootstrapNew(paths);

        return LoadExisting(paths.ConfigPath);
    }

    public static AppConfig LoadExisting(string configPath)
    {
        string json;
        try
        {
            json = File.ReadAllText(configPath, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            throw new InvalidConfigException($"Failed to read config at '{configPath}'.", ex);
        }

        AppConfigDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<AppConfigDto>(json, ReadOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidConfigException($"Config JSON is invalid at '{configPath}'.", ex);
        }

        if (dto is null || string.IsNullOrWhiteSpace(dto.BaseUrl) || string.IsNullOrWhiteSpace(dto.Token))
            throw new InvalidConfigException($"Config is missing required fields at '{configPath}'.");

        if (!TryValidateBaseUrl(dto.BaseUrl, out var port, out var baseUrlError))
            throw new InvalidConfigException($"Config baseUrl is invalid: {baseUrlError}");

        if (!IsValidToken(dto.Token))
            throw new InvalidConfigException("Config token must be 64 lowercase hex characters.");

        return new AppConfig
        {
            BaseUrl = dto.BaseUrl,
            Token = dto.Token,
            ConfirmUntag = dto.ConfirmUntag ?? true,
            PreviewLimit = dto.PreviewLimit ?? 16,
            MaxConcurrency = dto.MaxConcurrency ?? 4,
            Port = port,
        };
    }

    private static AppConfig BootstrapNew(AppPaths paths)
    {
        Directory.CreateDirectory(paths.Root);
        Directory.CreateDirectory(paths.DataDirectory);
        Directory.CreateDirectory(paths.LogsDirectory);

        if (!TryFindFreePort(out var port))
            throw new InvalidOperationException(
                $"No free TCP port on 127.0.0.1 in range {PortRangeStart}-{PortRangeEnd}.");

        var token = GenerateToken();
        var config = new AppConfig
        {
            BaseUrl = $"http://127.0.0.1:{port}",
            Token = token,
            ConfirmUntag = true,
            PreviewLimit = 16,
            MaxConcurrency = 4,
            Port = port,
        };

        WriteAtomic(paths.ConfigPath, config);
        return config;
    }

    public static void WriteAtomic(string configPath, AppConfig config)
    {
        var directory = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        JsonObject root;
        if (File.Exists(configPath))
        {
            try
            {
                var existing = JsonNode.Parse(File.ReadAllText(configPath, Encoding.UTF8));
                root = existing as JsonObject ?? new JsonObject();
            }
            catch (JsonException)
            {
                root = new JsonObject();
            }
        }
        else
        {
            root = new JsonObject();
        }

        root["baseUrl"] = config.BaseUrl;
        root["token"] = config.Token;
        root["confirmUntag"] = config.ConfirmUntag;
        root["previewLimit"] = config.PreviewLimit;
        root["maxConcurrency"] = config.MaxConcurrency;

        var json = root.ToJsonString(WriteOptions);
        var tempPath = configPath + ".tmp";
        File.WriteAllText(tempPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(tempPath, configPath, overwrite: true);
    }

    public static bool TryValidateBaseUrl(string baseUrl, out int port, out string error)
    {
        port = 0;
        error = string.Empty;

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            error = "not an absolute URI";
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            error = "scheme must be http";
            return false;
        }

        if (!string.Equals(uri.Host, "127.0.0.1", StringComparison.Ordinal))
        {
            error = "host must be 127.0.0.1";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            error = "query and fragment are not allowed";
            return false;
        }

        if (uri.AbsolutePath is not ("/" or ""))
        {
            error = "pathname must be empty or /";
            return false;
        }

        port = uri.IsDefaultPort ? 80 : uri.Port;
        return true;
    }

    public static bool IsValidToken(string token) =>
        token.Length == 64 && token.All(static c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));

    public static string GenerateToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static bool TryFindFreePort(out int port)
    {
        for (var candidate = PortRangeStart; candidate <= PortRangeEnd; candidate++)
        {
            if (IsPortFree(candidate))
            {
                port = candidate;
                return true;
            }
        }

        port = 0;
        return false;
    }

    public static bool IsPortFree(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private sealed class AppConfigDto
    {
        [JsonPropertyName("baseUrl")]
        public string? BaseUrl { get; set; }

        [JsonPropertyName("token")]
        public string? Token { get; set; }

        [JsonPropertyName("confirmUntag")]
        public bool? ConfirmUntag { get; set; }

        [JsonPropertyName("previewLimit")]
        public int? PreviewLimit { get; set; }

        [JsonPropertyName("maxConcurrency")]
        public int? MaxConcurrency { get; set; }
    }
}

public sealed class InvalidConfigException : Exception
{
    public InvalidConfigException(string message) : base(message) { }
    public InvalidConfigException(string message, Exception inner) : base(message, inner) { }
}
