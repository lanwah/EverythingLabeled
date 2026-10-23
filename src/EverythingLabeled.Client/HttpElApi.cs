using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace EverythingLabeled.Client;

public sealed class HttpElApi : IElApi
{
    private readonly HttpClient _http;
    private readonly string _token;

    public HttpElApi(HttpClient http, string token)
    {
        _http = http;
        _token = token;
    }

    public Task<ElResult<TagApplyData>> TagAsync(
        string uri,
        string kind,
        IReadOnlyList<string> aliases,
        bool createMissing,
        CancellationToken ct = default) =>
        SendAsync<TagApplyData>(
            HttpMethod.Post,
            "v1/resources/tags",
            new { uri, kind, add = aliases, createMissing },
            authorize: true,
            ct);

    public Task<ElResult<TagApplyData>> UntagAsync(
        string uri,
        IReadOnlyList<string> aliases,
        CancellationToken ct = default) =>
        SendAsync<TagApplyData>(
            HttpMethod.Post,
            "v1/resources/tags",
            new { uri, remove = aliases },
            authorize: true,
            ct);

    public async Task<ElResult<TagsPage>> GetTagsAsync(string uri, CancellationToken ct = default)
    {
        var all = new List<TagInfo>();
        var offset = 0;
        string? message = null;

        while (true)
        {
            var path =
                "v1/resources/tags?uri=" + Uri.EscapeDataString(uri)
                + "&offset=" + offset.ToString(CultureInfo.InvariantCulture)
                + "&limit=1024";
            var page = await SendAsync<TagsPage>(HttpMethod.Get, path, body: null, authorize: true, ct)
                .ConfigureAwait(false);
            if (!page.IsSuccess)
                return page;

            message = page.Message;
            var data = page.Data!;
            if (data.Items.Count == 0)
                break;

            all.AddRange(data.Items);
            offset += data.Items.Count;
            if (offset >= data.Total)
                break;
        }

        return ElResult<TagsPage>.Ok(new TagsPage(all, 0, all.Count, all.Count), message);
    }

    public Task<ElResult<QueryPage>> QueryAsync(
        string q,
        string? expand,
        int? maxDepth,
        int? expandedTagLimit,
        int limit,
        int offset,
        string? kind = null,
        CancellationToken ct = default) =>
        SendAsync<QueryPage>(
            HttpMethod.Get,
            "v1/query?" + BuildQuery(q, expand, maxDepth, expandedTagLimit, limit, offset, kind),
            body: null,
            authorize: true,
            ct);

    public Task<ElResult<PreviewPage>> PreviewAsync(
        string q,
        string? expand,
        int? maxDepth,
        int? expandedTagLimit,
        int limit,
        string? kind = null,
        CancellationToken ct = default) =>
        SendAsync<PreviewPage>(
            HttpMethod.Get,
            "v1/query/preview?" + BuildQuery(q, expand, maxDepth, expandedTagLimit, limit, offset: null, kind),
            body: null,
            authorize: true,
            ct);

    public Task<ElResult<SuggestPage>> SuggestAsync(string prefix, int limit, CancellationToken ct = default) =>
        SendAsync<SuggestPage>(
            HttpMethod.Get,
            "v1/tags/suggest?q=" + Uri.EscapeDataString(prefix)
            + "&limit=" + limit.ToString(CultureInfo.InvariantCulture),
            body: null,
            authorize: true,
            ct);

    public Task<ElResult<TagsPage>> ListTagsAsync(
        string? q,
        int limit,
        int offset,
        CancellationToken ct = default)
    {
        var path =
            "v1/tags?limit=" + limit.ToString(CultureInfo.InvariantCulture)
            + "&offset=" + offset.ToString(CultureInfo.InvariantCulture);
        if (q is not null)
            path += "&q=" + Uri.EscapeDataString(q);
        return SendAsync<TagsPage>(HttpMethod.Get, path, body: null, authorize: true, ct);
    }

    public Task<ElResult<TagInfo>> RenameTagAsync(string from, string to, CancellationToken ct = default) =>
        SendAsync<TagInfo>(
            HttpMethod.Post,
            "v1/tags/rename",
            new { from, to },
            authorize: true,
            ct);

    public Task<ElResult<MetaData>> DeleteTagAsync(Guid id, CancellationToken ct = default) =>
        SendAsync<MetaData>(
            HttpMethod.Delete,
            "v1/tags/" + id.ToString("D", CultureInfo.InvariantCulture),
            body: null,
            authorize: true,
            ct);

    public Task<ElResult<MetaData>> DeleteResourceAsync(Guid id, CancellationToken ct = default) =>
        SendAsync<MetaData>(
            HttpMethod.Delete,
            "v1/resources/" + id.ToString("D", CultureInfo.InvariantCulture),
            body: null,
            authorize: true,
            ct);

    public Task<ElResult<HealthData>> HealthAsync(CancellationToken ct = default) =>
        SendAsync<HealthData>(HttpMethod.Get, "v1/health", body: null, authorize: false, ct);

    public Task<ElResult<MetaData>> MetaAsync(CancellationToken ct = default) =>
        SendAsync<MetaData>(HttpMethod.Get, "v1/meta", body: null, authorize: true, ct);

    private static string BuildQuery(
        string q,
        string? expand,
        int? maxDepth,
        int? expandedTagLimit,
        int limit,
        int? offset,
        string? kind = null)
    {
        var parts = new List<string>
        {
            "q=" + Uri.EscapeDataString(q),
            "limit=" + limit.ToString(CultureInfo.InvariantCulture),
        };
        if (offset is { } off)
            parts.Add("offset=" + off.ToString(CultureInfo.InvariantCulture));
        if (expand == "child")
            parts.Add("expand=child");
        if (maxDepth is { } depth)
            parts.Add("maxDepth=" + depth.ToString(CultureInfo.InvariantCulture));
        if (expandedTagLimit is { } tagLimit)
            parts.Add("expandedTagLimit=" + tagLimit.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(kind)
            && !kind.Equals("all", StringComparison.Ordinal))
            parts.Add("kind=" + Uri.EscapeDataString(kind));
        return string.Join("&", parts);
    }

    private async Task<ElResult<T>> SendAsync<T>(
        HttpMethod method,
        string relativeUri,
        object? body,
        bool authorize,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, relativeUri);
        if (authorize)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, ElJson.Options);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        try
        {
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            var payload = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ParseEnvelope<T>(payload, response.StatusCode);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return ElResult<T>.Fail(new ElError(ElErrorCodes.ConnectionFailed, "Unable to reach the service."));
        }
    }

    private static ElResult<T> ParseEnvelope<T>(string payload, HttpStatusCode statusCode)
    {
        Envelope? envelope = null;
        try
        {
            envelope = JsonSerializer.Deserialize<Envelope>(payload, ElJson.Options);
        }
        catch (JsonException)
        {
            // fall through to HTTP mapping
        }

        var httpOk = (int)statusCode is >= 200 and <= 299;
        if (envelope?.Status == "ok" && httpOk)
        {
            T? data = default;
            if (envelope.Data.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                data = envelope.Data.Deserialize<T>(ElJson.Options);
            return ElResult<T>.Ok(data!, envelope.Message);
        }

        var code = ReadErrorCode(envelope);
        code ??= (int)statusCode >= 500 ? "internal_error" : ElErrorCodes.BadRequest;
        var message = envelope?.Message ?? statusCode.ToString();
        return ElResult<T>.Fail(new ElError(code, message, (int)statusCode));
    }

    private static string? ReadErrorCode(Envelope? envelope)
    {
        if (envelope?.Data.ValueKind != JsonValueKind.Object)
            return null;
        return envelope.Data.TryGetProperty("code", out var codeEl) ? codeEl.GetString() : null;
    }

    private sealed class Envelope
    {
        public string? Status { get; set; }
        public string? Message { get; set; }
        public JsonElement Data { get; set; }
    }
}
