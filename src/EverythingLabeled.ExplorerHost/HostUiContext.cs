using System.Net.Http;
using EverythingLabeled.Client;

namespace EverythingLabeled.ExplorerHost;

public sealed class HostUiContext
{
    public required HostMode Mode { get; init; }
    public IElApi? Api { get; init; }
    public ExplorerRuntimeConfig? Config { get; init; }
    public SelectionResult? Selection { get; init; }
    public string? Banner { get; init; }
    public bool BusinessEnabled { get; init; }
    public HttpClient? Http { get; init; }
}
