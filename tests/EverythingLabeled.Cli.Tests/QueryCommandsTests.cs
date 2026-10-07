using System.Text.Json;
using EverythingLabeled.Client;
using EverythingLabeled.Cli;

namespace EverythingLabeled.Cli.Tests;

public sealed class QueryCommandsTests
{
    [Fact]
    public async Task Query_dash_token_after_terminator_is_q()
    {
        var api = new FakeApi();
        var (code, _, _) = await Run(["query", "--", "-archived"], api);
        Assert.Equal(ExitCodes.Ok, code);
        Assert.Equal(1, api.QueryCalls);
        Assert.Equal("-archived", api.LastQ);
    }

    [Fact]
    public async Task Query_two_positionals_does_not_call_api()
    {
        var api = new FakeApi();
        var (code, _, _) = await Run(["query", "a", "b"], api);
        Assert.Equal(ExitCodes.Usage, code);
        Assert.Equal(0, api.QueryCalls);
    }

    [Fact]
    public async Task Query_max_depth_without_expand_does_not_call_api()
    {
        var api = new FakeApi();
        var (code, _, _) = await Run(["query", "--max-depth", "1", "x"], api);
        Assert.Equal(ExitCodes.Usage, code);
        Assert.Equal(0, api.QueryCalls);
    }

    [Fact]
    public async Task Preview_omitted_limit_sends_10_and_does_not_pass_offset()
    {
        var api = new FakeApi();
        var (code, _, _) = await Run(["preview", "x"], api);
        Assert.Equal(ExitCodes.Ok, code);
        Assert.Equal(1, api.PreviewCalls);
        Assert.Equal(0, api.QueryCalls);
        Assert.Equal(10, api.LastLimit);
        Assert.Null(api.LastOffset);
    }

    [Fact]
    public async Task Query_hit_without_displayName_prints_uri()
    {
        var api = new FakeApi
        {
            QueryResult = ElResult<QueryPage>.Ok(new QueryPage(
                [new QueryHit(Guid.NewGuid(), "file:///a", "file", null)],
                0,
                50,
                1)),
        };
        var (code, stdout, stderr) = await Run(["query", "x"], api);
        Assert.Equal(ExitCodes.Ok, code);
        Assert.Equal("file:///a" + Environment.NewLine, stdout);
        Assert.Contains("offset=0", stderr, StringComparison.Ordinal);
        Assert.Contains("limit=50", stderr, StringComparison.Ordinal);
        Assert.Contains("total=1", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Query_notice_writes_stderr_exit_0()
    {
        var api = new FakeApi
        {
            QueryResult = ElResult<QueryPage>.Ok(new QueryPage([], 0, 50, 0), "unknown term"),
        };
        var (code, stdout, stderr) = await Run(["query", "x"], api);
        Assert.Equal(ExitCodes.Ok, code);
        Assert.Equal("", stdout);
        Assert.Contains("unknown term", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Limit_zero_is_local_bad_request()
    {
        var api = new FakeApi();
        var (code, _, _) = await Run(["query", "--limit", "0", "x"], api);
        Assert.Equal(ExitCodes.Usage, code);
        Assert.Equal(0, api.QueryCalls);
    }

    [Fact]
    public async Task Limit_1025_is_local_bad_request()
    {
        var api = new FakeApi();
        var (code, _, _) = await Run(["query", "--limit", "1025", "x"], api);
        Assert.Equal(ExitCodes.Usage, code);
        Assert.Equal(0, api.QueryCalls);
    }

    [Fact]
    public async Task Json_query_writes_items_without_envelope_status()
    {
        var api = new FakeApi
        {
            QueryResult = ElResult<QueryPage>.Ok(new QueryPage([], 0, 50, 0)),
        };
        var (code, stdout, _) = await Run(["--json", "query", "x"], api);
        Assert.Equal(ExitCodes.Ok, code);
        using var doc = JsonDocument.Parse(stdout);
        Assert.False(doc.RootElement.TryGetProperty("status", out _));
        Assert.True(doc.RootElement.TryGetProperty("items", out _));
    }

    [Fact]
    public async Task Json_invalid_limit_writes_error_json_only()
    {
        var api = new FakeApi();
        var (code, stdout, stderr) = await Run(["--json", "query", "--limit=foo", "x"], api);
        Assert.Equal(ExitCodes.Usage, code);
        Assert.Equal("", stdout);
        Assert.DoesNotContain("Usage:", stderr, StringComparison.Ordinal);
        using var doc = JsonDocument.Parse(stderr);
        Assert.Equal(ElErrorCodes.BadRequest, doc.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(0, api.QueryCalls);
    }

    [Fact]
    public async Task Suggest_prints_tab_separated_fields()
    {
        var api = new FakeApi
        {
            SuggestResult = ElResult<SuggestPage>.Ok(new SuggestPage(
                [new SuggestItem(Guid.NewGuid(), "work", "Work", null, "w")],
                0,
                10,
                1)),
        };
        var (code, stdout, _) = await Run(["suggest", "wo"], api);
        Assert.Equal(ExitCodes.Ok, code);
        Assert.Equal("work\tWork\tw" + Environment.NewLine, stdout);
        Assert.Equal("wo", api.LastPrefix);
        Assert.Equal(10, api.LastLimit);
    }

    private static async Task<(int Code, string Stdout, string Stderr)> Run(string[] args, FakeApi api)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var code = await ElApp.RunAsync(new ElInvocation
        {
            Args = args,
            Stdout = stdout,
            Stderr = stderr,
            Api = api,
            LoadConfig = _ => ElResult<ElConfig>.Ok(
                new ElConfig("http://127.0.0.1:5123/", "token", true, 16, 4)),
        });
        return (code, stdout.ToString(), stderr.ToString());
    }

    private sealed class FakeApi : IElApi
    {
        public int QueryCalls;
        public int PreviewCalls;
        public string? LastQ;
        public string? LastPrefix;
        public int? LastLimit;
        public int? LastOffset;
        public ElResult<QueryPage>? QueryResult;
        public ElResult<SuggestPage>? SuggestResult;

        public Task<ElResult<TagApplyData>> TagAsync(
            string uri, string kind, IReadOnlyList<string> aliases, bool createMissing, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<ElResult<TagApplyData>> UntagAsync(
            string uri, IReadOnlyList<string> aliases, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<ElResult<TagsPage>> GetTagsAsync(string uri, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<ElResult<QueryPage>> QueryAsync(
            string q, string? expand, int? maxDepth, int? expandedTagLimit, int limit, int offset, CancellationToken ct = default)
        {
            QueryCalls++;
            LastQ = q;
            LastLimit = limit;
            LastOffset = offset;
            return Task.FromResult(QueryResult ?? ElResult<QueryPage>.Ok(new QueryPage([], 0, limit, 0)));
        }

        public Task<ElResult<PreviewPage>> PreviewAsync(
            string q, string? expand, int? maxDepth, int? expandedTagLimit, int limit, CancellationToken ct = default)
        {
            PreviewCalls++;
            LastQ = q;
            LastLimit = limit;
            LastOffset = null;
            return Task.FromResult(ElResult<PreviewPage>.Ok(new PreviewPage([], 0, limit)));
        }

        public Task<ElResult<SuggestPage>> SuggestAsync(string prefix, int limit, CancellationToken ct = default)
        {
            LastPrefix = prefix;
            LastLimit = limit;
            return Task.FromResult(SuggestResult ?? ElResult<SuggestPage>.Ok(new SuggestPage([], 0, limit, 0)));
        }

        public Task<ElResult<HealthData>> HealthAsync(CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<ElResult<MetaData>> MetaAsync(CancellationToken ct = default) =>
            throw new NotImplementedException();
    }
}
