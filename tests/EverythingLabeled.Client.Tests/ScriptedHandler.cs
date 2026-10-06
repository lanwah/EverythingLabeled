using System.Net;
using System.Text;

namespace EverythingLabeled.Client.Tests;

internal sealed class ScriptedHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, string, int, CancellationToken, Task<HttpResponseMessage>> _script;
    private int _index;

    public ScriptedHandler(Func<HttpRequestMessage, string, int, CancellationToken, Task<HttpResponseMessage>> script)
    {
        _script = script;
    }

    public ScriptedHandler(Func<HttpRequestMessage, string, int, HttpResponseMessage> script)
        : this((req, body, i, _) => Task.FromResult(script(req, body, i)))
    {
    }

    public IList<(HttpRequestMessage Request, string Body)> Calls { get; } = new List<(HttpRequestMessage, string)>();

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? ""
            : await request.Content.ReadAsStringAsync(cancellationToken);
        Calls.Add((request, body));
        return await _script(request, body, _index++, cancellationToken);
    }
}
