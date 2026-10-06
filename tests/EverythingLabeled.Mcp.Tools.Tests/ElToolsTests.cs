using System.Text.Json;
using EverythingLabeled.Client;
using EverythingLabeled.Mcp.Tools;
using ModelContextProtocol.Protocol;

namespace EverythingLabeled.Mcp.Tools.Tests;

public sealed class ElToolsTests
{
    [Fact]
    public async Task Health_without_base_url_is_config_missing()
    {
        var tools = new ElTools(new ElMcpRuntime
        {
            Api = null,
            HasBaseUrl = false,
            HasToken = false,
            ConfirmUntag = true,
        });

        var result = await tools.Health();

        using var doc = ParseJson(result);
        Assert.True(IsError(result));
        Assert.Equal(ElErrorCodes.ConfigMissing, ErrorCode(doc));
        Assert.Equal("Configuration is missing or invalid.", ErrorMessage(doc));
        AssertNoHttpStatus(doc);
    }

    [Fact]
    public async Task Health_does_not_require_token()
    {
        var api = new FakeApi
        {
            HealthResult = ElResult<HealthData>.Ok(new HealthData("ok", 1)),
        };
        var tools = Ready(api, hasToken: false);

        var result = await tools.Health();

        using var doc = ParseJson(result);
        Assert.Equal(1, api.HealthCalls);
        Assert.False(IsError(result));
        Assert.Equal("ok", doc.RootElement.GetProperty("data").GetProperty("health").GetString());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("message").ValueKind);
    }

    [Fact]
    public async Task Tag_without_token_does_not_call_api()
    {
        var api = new FakeApi();
        var tools = Ready(api, hasToken: false);

        var result = await tools.Tag("https://example.com/file", ["work"]);

        using var doc = ParseJson(result);
        Assert.Equal(0, api.TagCalls);
        Assert.True(IsError(result));
        Assert.Equal(ElErrorCodes.ConfigMissing, ErrorCode(doc));
        Assert.Equal("Configuration is missing or invalid.", ErrorMessage(doc));
    }

    [Fact]
    public async Task Untag_requires_confirm_when_confirm_untag_true()
    {
        var api = new FakeApi();
        var tools = Ready(api, confirmUntag: true);

        var result = await tools.Untag("https://example.com/file", ["work"]);

        using var doc = ParseJson(result);
        Assert.Equal(0, api.UntagCalls);
        Assert.True(IsError(result));
        Assert.Equal(ElErrorCodes.ConfirmationRequired, ErrorCode(doc));
        Assert.Equal("Call el_untag again with confirm=true to remove the aliases.", ErrorMessage(doc));
        AssertNoHttpStatus(doc);
    }

    [Fact]
    public async Task Untag_confirm_true_calls_api()
    {
        var api = new FakeApi { UntagResult = OkApply() };
        var tools = Ready(api, confirmUntag: true);

        var result = await tools.Untag("https://example.com/file", ["work"], confirm: true);

        Assert.False(IsError(result));
        Assert.Equal(1, api.UntagCalls);
        Assert.Equal(["work"], api.LastAliases);
    }

    [Fact]
    public async Task Query_limit_defaults_to_50()
    {
        var api = new FakeApi();
        var tools = Ready(api);

        var result = await tools.Query("work");

        Assert.False(IsError(result));
        Assert.Equal(1, api.QueryCalls);
        Assert.Equal(50, api.LastLimit);
        Assert.Equal(0, api.LastOffset);
        Assert.Equal("work", api.LastQ);
    }

    [Fact]
    public async Task Preview_limit_defaults_to_10()
    {
        var api = new FakeApi();
        var tools = Ready(api);

        var result = await tools.Preview("work");

        Assert.False(IsError(result));
        Assert.Equal(1, api.PreviewCalls);
        Assert.Equal(0, api.QueryCalls);
        Assert.Equal(10, api.LastLimit);
        Assert.Null(api.LastOffset);
    }

    [Fact]
    public async Task Query_gate_without_expand_is_bad_request()
    {
        var api = new FakeApi();
        var tools = Ready(api);

        var result = await tools.Query("work", max_depth: 1);

        using var doc = ParseJson(result);
        Assert.Equal(0, api.QueryCalls);
        Assert.True(IsError(result));
        Assert.Equal(ElErrorCodes.BadRequest, ErrorCode(doc));
        Assert.Contains("expand", ErrorMessage(doc), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Success_notice_keeps_message_and_isError_false()
    {
        var api = new FakeApi
        {
            QueryResult = ElResult<QueryPage>.Ok(new QueryPage([], 0, 50, 0), "unknown term"),
        };
        var tools = Ready(api);

        var result = await tools.Query("missing");

        using var doc = ParseJson(result);
        Assert.False(IsError(result));
        Assert.Equal("unknown term", doc.RootElement.GetProperty("message").GetString());
        Assert.True(doc.RootElement.TryGetProperty("data", out _));
    }

    [Fact]
    public async Task Http_error_sets_isError_and_passes_code()
    {
        var api = new FakeApi
        {
            QueryResult = ElResult<QueryPage>.Fail(new ElError("unauthorized", "no", 401)),
        };
        var tools = Ready(api);

        var result = await tools.Query("work");

        using var doc = ParseJson(result);
        Assert.True(IsError(result));
        Assert.Equal("unauthorized", ErrorCode(doc));
        Assert.Equal("no", ErrorMessage(doc));
        AssertNoHttpStatus(doc);
    }

    [Fact]
    public async Task Aliases_empty_is_bad_request()
    {
        var api = new FakeApi();
        var tools = Ready(api);

        var result = await tools.Tag("https://example.com/file", []);

        using var doc = ParseJson(result);
        Assert.Equal(0, api.TagCalls);
        Assert.True(IsError(result));
        Assert.Equal(ElErrorCodes.BadRequest, ErrorCode(doc));
    }

    private static ElTools Ready(FakeApi api, bool hasToken = true, bool confirmUntag = true) =>
        new(new ElMcpRuntime
        {
            Api = api,
            HasBaseUrl = true,
            HasToken = hasToken,
            ConfirmUntag = confirmUntag,
        });

    private static bool IsError(CallToolResult result)
    {
        var prop = result.GetType().GetProperty("IsError");
        if (prop is not null)
        {
            var value = prop.GetValue(result);
            return value is true || value is bool and true;
        }

        return false;
    }

    private static JsonDocument ParseJson(CallToolResult result)
    {
        Assert.NotEmpty(result.Content);
        var text = result.Content[0] switch
        {
            TextContentBlock block => block.Text,
            _ => throw new InvalidOperationException("Expected a text content block."),
        };
        return JsonDocument.Parse(text);
    }

    private static string? ErrorCode(JsonDocument doc) =>
        doc.RootElement.GetProperty("error").GetProperty("code").GetString();

    private static string? ErrorMessage(JsonDocument doc) =>
        doc.RootElement.GetProperty("error").GetProperty("message").GetString();

    private static void AssertNoHttpStatus(JsonDocument doc)
    {
        Assert.False(doc.RootElement.GetProperty("error").TryGetProperty("httpStatus", out _));
    }

    private static ElResult<TagApplyData> OkApply() =>
        ElResult<TagApplyData>.Ok(new TagApplyData(
            new ResourceDetail(
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                "https://example.com/file",
                "url",
                null,
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch,
                1,
                []),
            [],
            [],
            [],
            1));

    private sealed class FakeApi : IElApi
    {
        public int TagCalls;
        public int UntagCalls;
        public int QueryCalls;
        public int PreviewCalls;
        public int HealthCalls;
        public IReadOnlyList<string>? LastAliases;
        public string? LastQ;
        public int? LastLimit;
        public int? LastOffset;
        public ElResult<TagApplyData>? UntagResult;
        public ElResult<QueryPage>? QueryResult;
        public ElResult<HealthData>? HealthResult;

        public Task<ElResult<TagApplyData>> TagAsync(
            string uri,
            string kind,
            IReadOnlyList<string> aliases,
            bool createMissing,
            CancellationToken ct = default)
        {
            TagCalls++;
            LastAliases = aliases;
            return Task.FromResult(OkApply());
        }

        public Task<ElResult<TagApplyData>> UntagAsync(
            string uri,
            IReadOnlyList<string> aliases,
            CancellationToken ct = default)
        {
            UntagCalls++;
            LastAliases = aliases;
            return Task.FromResult(UntagResult ?? OkApply());
        }

        public Task<ElResult<TagsPage>> GetTagsAsync(string uri, CancellationToken ct = default) =>
            Task.FromResult(ElResult<TagsPage>.Ok(new TagsPage([], 0, 0, 0)));

        public Task<ElResult<QueryPage>> QueryAsync(
            string q,
            string? expand,
            int? maxDepth,
            int? expandedTagLimit,
            int limit,
            int offset,
            CancellationToken ct = default)
        {
            QueryCalls++;
            LastQ = q;
            LastLimit = limit;
            LastOffset = offset;
            return Task.FromResult(QueryResult ?? ElResult<QueryPage>.Ok(new QueryPage([], 0, limit, 0)));
        }

        public Task<ElResult<PreviewPage>> PreviewAsync(
            string q,
            string? expand,
            int? maxDepth,
            int? expandedTagLimit,
            int limit,
            CancellationToken ct = default)
        {
            PreviewCalls++;
            LastQ = q;
            LastLimit = limit;
            LastOffset = null;
            return Task.FromResult(ElResult<PreviewPage>.Ok(new PreviewPage([], 0, limit)));
        }

        public Task<ElResult<SuggestPage>> SuggestAsync(string prefix, int limit, CancellationToken ct = default) =>
            Task.FromResult(ElResult<SuggestPage>.Ok(new SuggestPage([], 0, limit, 0)));

        public Task<ElResult<HealthData>> HealthAsync(CancellationToken ct = default)
        {
            HealthCalls++;
            return Task.FromResult(HealthResult ?? ElResult<HealthData>.Ok(new HealthData("ok", 1)));
        }
    }
}
