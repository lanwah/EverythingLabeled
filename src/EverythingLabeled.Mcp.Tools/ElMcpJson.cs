using System.Text.Json;
using EverythingLabeled.Client;
using ModelContextProtocol.Protocol;

namespace EverythingLabeled.Mcp.Tools;

public static class ElMcpJson
{
    public static CallToolResult Ok<T>(T data, string? message) =>
        Envelope(new SuccessEnvelope<T>(data, message), isError: false);

    public static CallToolResult Fail(ElError error) =>
        Envelope(new FailureEnvelope(new ErrorBody(error.Code, error.Message)), isError: true);

    private static CallToolResult Envelope<T>(T payload, bool isError)
    {
        var json = JsonSerializer.Serialize(payload, ElJson.Options);
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = json }],
            IsError = isError,
        };
    }

    private sealed record SuccessEnvelope<T>(T Data, string? Message);

    private sealed record FailureEnvelope(ErrorBody Error);

    private sealed record ErrorBody(string Code, string Message);
}
