using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EverythingLabeled.Client;

namespace EverythingLabeled.Client.Tests;

public sealed class HttpElApiTests
{
    private static readonly Guid TagId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TagId2 = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task Tag_posts_relative_v1_path_with_createMissing_and_no_displayName()
    {
        var api = CreateApi((req, body, _) =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("http://127.0.0.1:5123/v1/resources/tags", req.RequestUri!.ToString());
            Assert.Equal("Bearer", req.Headers.Authorization!.Scheme);
            Assert.Equal("tok", req.Headers.Authorization.Parameter);
            using var doc = JsonDocument.Parse(body);
            Assert.True(doc.RootElement.GetProperty("createMissing").GetBoolean());
            Assert.Equal("file", doc.RootElement.GetProperty("kind").GetString());
            Assert.False(doc.RootElement.TryGetProperty("displayName", out JsonElement _));
            Assert.False(doc.RootElement.TryGetProperty("resourceId", out JsonElement _));
            return Ok(TagApplyJson());
        });

        var r = await api.TagAsync("file:///C:/a.txt", "file", ["工作"], createMissing: true);
        Assert.True(r.IsSuccess);
        Assert.Equal(7, r.Data!.DataVersion);
        Assert.Equal("ns", r.Data.Resource.Tags[0].Namespace);
        Assert.Equal(3, r.Data.Resource.Tags[0].Revision);
    }

    [Fact]
    public async Task Untag_omits_kind()
    {
        var api = CreateApi((_, body, _) =>
        {
            using var doc = JsonDocument.Parse(body);
            Assert.False(doc.RootElement.TryGetProperty("kind", out JsonElement _));
            Assert.True(doc.RootElement.TryGetProperty("remove", out JsonElement _));
            return Ok(TagApplyJson());
        });

        var r = await api.UntagAsync("file:///C:/a.txt", ["工作"]);
        Assert.True(r.IsSuccess);
    }

    [Fact]
    public async Task Health_is_get_v1_health_without_authorization()
    {
        var api = CreateApi((req, _, _) =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.Equal("http://127.0.0.1:5123/v1/health", req.RequestUri!.ToString());
            Assert.Null(req.Headers.Authorization);
            return Ok("""{"health":"ok","dataVersion":4}""");
        });

        var r = await api.HealthAsync();
        Assert.True(r.IsSuccess);
        Assert.Equal("ok", r.Data!.Health);
        Assert.Equal(4, r.Data.DataVersion);
    }

    [Fact]
    public async Task Meta_is_get_v1_meta_with_bearer()
    {
        var api = CreateApi((req, _, _) =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.Equal("http://127.0.0.1:5123/v1/meta", req.RequestUri!.ToString());
            Assert.Equal("Bearer", req.Headers.Authorization!.Scheme);
            Assert.Equal("tok", req.Headers.Authorization.Parameter);
            return Ok("""{"dataVersion":9}""");
        });

        var r = await api.MetaAsync();
        Assert.True(r.IsSuccess);
        Assert.Equal(9, r.Data!.DataVersion);
    }

    [Fact]
    public async Task Unauthorized_maps_code_and_http_status_401()
    {
        var api = CreateApi((_, _, _) => Error(
            HttpStatusCode.Unauthorized,
            "unauthorized",
            "Nope."));

        var r = await api.GetTagsAsync("file:///C:/a.txt");
        Assert.False(r.IsSuccess);
        Assert.Equal("unauthorized", r.Error!.Value.Code);
        Assert.Equal(401, r.Error.Value.HttpStatus);
    }

    [Fact]
    public async Task Timeout_is_connection_failed()
    {
        var handler = new ScriptedHandler(async (_, _, _, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            return Ok("""{"items":[],"offset":0,"limit":0,"total":0}""");
        });
        using var http = NewClient(handler);
        http.Timeout = TimeSpan.FromMilliseconds(40);
        var api = new HttpElApi(http, "tok");

        var r = await api.QueryAsync("a", null, null, null, 50, 0);
        Assert.False(r.IsSuccess);
        Assert.Equal(ElErrorCodes.ConnectionFailed, r.Error!.Value.Code);
        Assert.Null(r.Error.Value.HttpStatus);
    }

    [Fact]
    public async Task GetTags_concatenates_pages_into_synthetic_page()
    {
        var api = CreateApi((req, _, i) =>
        {
            Assert.StartsWith("http://127.0.0.1:5123/v1/resources/tags?", req.RequestUri!.ToString());
            Assert.Contains("limit=1024", req.RequestUri.Query);
            Assert.Contains("uri=" + Uri.EscapeDataString("file:///C:/a.txt"), req.RequestUri.Query);
            return i switch
            {
                0 => Ok(TagsPageJson(offset: 0, total: 2, TagJson("a", TagId))),
                _ => Ok(TagsPageJson(offset: 1, total: 2, TagJson("b", TagId2))),
            };
        });

        var r = await api.GetTagsAsync("file:///C:/a.txt");
        Assert.True(r.IsSuccess);
        Assert.Equal(0, r.Data!.Offset);
        Assert.Equal(2, r.Data.Limit);
        Assert.Equal(2, r.Data.Total);
        Assert.Equal(["a", "b"], r.Data.Items.Select(t => t.Name).ToArray());
    }

    [Fact]
    public async Task Query_without_expand_omits_expand_query_param()
    {
        var api = CreateApi((req, _, _) =>
        {
            Assert.Equal("http://127.0.0.1:5123/v1/query?q=foo&limit=50&offset=0", req.RequestUri!.ToString());
            Assert.DoesNotContain("expand=", req.RequestUri.Query);
            return Ok("""{"items":[],"offset":0,"limit":50,"total":0}""");
        });

        var r = await api.QueryAsync("foo", expand: "none", maxDepth: null, expandedTagLimit: null, 50, 0);
        Assert.True(r.IsSuccess);
    }

    [Fact]
    public async Task Query_notice_sets_message_with_empty_items()
    {
        var api = CreateApi((_, _, _) => Envelope(
            HttpStatusCode.OK,
            "ok",
            "truncated",
            """{"items":[],"offset":0,"limit":50,"total":0}"""));

        var r = await api.QueryAsync("x", null, null, null, 50, 0);
        Assert.True(r.IsSuccess);
        Assert.Equal("truncated", r.Message);
        Assert.Empty(r.Data!.Items);
    }

    [Fact]
    public async Task Preview_does_not_send_offset()
    {
        var api = CreateApi((req, _, _) =>
        {
            Assert.StartsWith("http://127.0.0.1:5123/v1/query/preview?", req.RequestUri!.ToString());
            Assert.DoesNotContain("offset=", req.RequestUri.Query);
            Assert.Contains("expand=child", req.RequestUri.Query);
            Assert.Contains("maxDepth=1", req.RequestUri.Query);
            Assert.Contains("expandedTagLimit=8", req.RequestUri.Query);
            return Ok("""{"items":[],"offset":0,"limit":10}""");
        });

        var r = await api.PreviewAsync("q", "child", 1, 8, 10);
        Assert.True(r.IsSuccess);
    }

    [Fact]
    public async Task Preview_sends_kind_when_not_all()
    {
        var api = CreateApi((req, _, _) =>
        {
            Assert.Contains("kind=url", req.RequestUri!.Query);
            return Ok("""{"items":[],"offset":0,"limit":10}""");
        });

        var r = await api.PreviewAsync("q", null, null, null, 10, kind: "url");
        Assert.True(r.IsSuccess);
    }

    [Fact]
    public async Task Preview_omits_kind_when_all_or_null()
    {
        var api = CreateApi((req, _, i) =>
        {
            Assert.DoesNotContain("kind=", req.RequestUri!.Query);
            return Ok("""{"items":[],"offset":0,"limit":10}""");
        });

        Assert.True((await api.PreviewAsync("q", null, null, null, 10)).IsSuccess);
        Assert.True((await api.PreviewAsync("q", null, null, null, 10, kind: "all")).IsSuccess);
    }

    [Fact]
    public async Task Suggest_uses_q_not_prefix()
    {
        var api = CreateApi((req, _, _) =>
        {
            Assert.Equal("http://127.0.0.1:5123/v1/tags/suggest?q=wo&limit=10", req.RequestUri!.ToString());
            return Ok("""{"items":[],"offset":0,"limit":10,"total":0}""");
        });

        var r = await api.SuggestAsync("wo", 10);
        Assert.True(r.IsSuccess);
    }

    [Fact]
    public async Task ListTags_includes_optional_q()
    {
        var api = CreateApi((req, _, _) =>
        {
            var query = req.RequestUri!.Query;
            Assert.Contains("limit=50", query);
            Assert.Contains("offset=0", query);
            Assert.True(query.Contains("q=%E5%B7%A5") || query.Contains("q=工"));
            return Ok("""{"items":[],"offset":0,"limit":50,"total":0}""");
        });

        var r = await api.ListTagsAsync("工", 50, 0);
        Assert.True(r.IsSuccess);
    }

    [Fact]
    public async Task RenameTag_posts_from_to()
    {
        var api = CreateApi((req, body, _) =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("http://127.0.0.1:5123/v1/tags/rename", req.RequestUri!.ToString());
            using var doc = JsonDocument.Parse(body);
            Assert.Equal("工做", doc.RootElement.GetProperty("from").GetString());
            Assert.Equal("工作", doc.RootElement.GetProperty("to").GetString());
            return Ok("""{"id":"11111111-1111-1111-1111-111111111111","name":"工作","namespace":null,"seq":null,"nameInitials":"gz","aliases":["工作"],"createdAt":"2026-01-01T00:00:00Z","updatedAt":"2026-01-01T00:00:00Z","revision":1}""");
        });

        var r = await api.RenameTagAsync("工做", "工作");
        Assert.True(r.IsSuccess);
        Assert.Equal("工作", r.Data!.Name);
    }

    [Fact]
    public async Task DeleteTag_uses_guid_path()
    {
        var id = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var api = CreateApi((req, _, _) =>
        {
            Assert.Equal(HttpMethod.Delete, req.Method);
            Assert.Equal(
                "http://127.0.0.1:5123/v1/tags/22222222-2222-2222-2222-222222222222",
                req.RequestUri!.ToString());
            return Ok("""{"dataVersion":3}""");
        });

        var r = await api.DeleteTagAsync(id);
        Assert.True(r.IsSuccess);
        Assert.Equal(3, r.Data!.DataVersion);
    }

    private static HttpElApi CreateApi(Func<HttpRequestMessage, string, int, HttpResponseMessage> script)
    {
        var handler = new ScriptedHandler(script);
        return new HttpElApi(NewClient(handler), "tok");
    }

    private static HttpClient NewClient(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://127.0.0.1:5123/"),
            Timeout = TimeSpan.FromSeconds(30),
        };
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return http;
    }

    private static HttpResponseMessage Ok(string dataJson) =>
        Envelope(HttpStatusCode.OK, "ok", null, dataJson);

    private static HttpResponseMessage Error(HttpStatusCode status, string code, string message) =>
        Envelope(status, "error", message, $$"""{"code":"{{code}}"}""");

    private static HttpResponseMessage Envelope(HttpStatusCode status, string envelopeStatus, string? message, string dataJson)
    {
        var msg = message is null ? "null" : JsonSerializer.Serialize(message);
        var json = $$"""{"status":"{{envelopeStatus}}","message":{{msg}},"data":{{dataJson}}}""";
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    private static string TagJson(string name, Guid id) =>
        $$"""
        {"id":"{{id}}","name":"{{name}}","namespace":"ns","seq":1,"nameInitials":"gz","aliases":["{{name}}"],"createdAt":"2026-01-01T00:00:00+00:00","updatedAt":"2026-01-02T00:00:00+00:00","revision":3}
        """;

    private static string TagsPageJson(int offset, int total, string itemJson) =>
        $$"""{"items":[{{itemJson}}],"offset":{{offset}},"limit":1024,"total":{{total}}}""";

    private static string TagApplyJson() =>
        $$"""
        {"resource":{"id":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa","uri":"file:///C:/a.txt","kind":"file","displayName":null,"createdAt":"2026-01-01T00:00:00+00:00","updatedAt":"2026-01-02T00:00:00+00:00","revision":1,"tags":[{{TagJson("工作", TagId)}}]},"createdTags":[],"added":["{{TagId}}"],"removed":[],"dataVersion":7}
        """;
}
