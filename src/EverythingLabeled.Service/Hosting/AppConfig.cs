using System.Text.Json.Serialization;

namespace EverythingLabeled.Service.Hosting;

public sealed class AppConfig
{
    [JsonPropertyName("baseUrl")]
    public required string BaseUrl { get; init; }

    [JsonPropertyName("token")]
    public required string Token { get; init; }

    [JsonPropertyName("confirmUntag")]
    public bool ConfirmUntag { get; init; } = true;

    [JsonPropertyName("previewLimit")]
    public int PreviewLimit { get; init; } = 16;

    [JsonPropertyName("suggestLimit")]
    public int SuggestLimit { get; init; } = 8;

    [JsonPropertyName("maxConcurrency")]
    public int MaxConcurrency { get; init; } = 4;

    public int Port { get; init; }
}
