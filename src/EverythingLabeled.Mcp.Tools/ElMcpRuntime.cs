using EverythingLabeled.Client;

namespace EverythingLabeled.Mcp.Tools;

public sealed record ElMcpRuntime
{
    public required IElApi? Api { get; init; }
    public required bool HasBaseUrl { get; init; }
    public required bool HasToken { get; init; }
    public required bool ConfirmUntag { get; init; }
}
