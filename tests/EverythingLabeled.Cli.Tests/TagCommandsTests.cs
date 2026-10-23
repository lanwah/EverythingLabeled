using System.Text.Json;
using EverythingLabeled.Client;
using EverythingLabeled.Cli;

namespace EverythingLabeled.Cli.Tests;

public sealed class TagCommandsTests
{
    [Fact]
    public async Task Tag_file_captures_kind_uri_and_createMissing()
    {
        var path = CreateTempFile();
        var api = new FakeApi { TagResult = OkApply(added: []) };

        var (code, _, _) = await Run(["tag", path, "work"], api);

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Equal(1, api.TagCalls);
        Assert.Equal("file", api.LastKind);
        Assert.StartsWith("file://", api.LastUri, StringComparison.OrdinalIgnoreCase);
        Assert.False(api.LastCreateMissing);
        Assert.Equal(["work"], api.LastAliases);
    }

    [Fact]
    public async Task Tag_without_alias_does_not_call_api()
    {
        var path = CreateTempFile();
        var api = new FakeApi();

        var (code, _, stderr) = await Run(["tag", path], api);

        Assert.Equal(ExitCodes.Usage, code);
        Assert.Equal(0, api.TagCalls);
        Assert.Contains("alias", stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Tag_keeps_duplicate_aliases_in_user_order()
    {
        var path = CreateTempFile();
        var api = new FakeApi { TagResult = OkApply(added: []) };

        var (code, _, _) = await Run(["tag", path, "工作", "工作"], api);

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Equal(["工作", "工作"], api.LastAliases);
    }

    [Fact]
    public async Task Untag_non_tty_without_yes_does_not_call_api()
    {
        var path = CreateTempFile();
        var api = new FakeApi();

        var (code, _, stderr) = await Run(
            ["untag", path, "work"],
            api,
            isTty: false,
            confirmUntag: true);

        Assert.Equal(ExitCodes.Usage, code);
        Assert.Equal(0, api.UntagCalls);
        Assert.Contains("--yes", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Untag_tty_n_does_not_call_api()
    {
        var path = CreateTempFile();
        var api = new FakeApi();

        var (code, _, stderr) = await Run(
            ["untag", path, "work"],
            api,
            isTty: true,
            stdin: "n\n",
            confirmUntag: true);

        Assert.Equal(ExitCodes.Usage, code);
        Assert.Equal(0, api.UntagCalls);
        Assert.Contains("Remove alias(es)", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Untag_tty_y_calls_api()
    {
        var path = CreateTempFile();
        var api = new FakeApi { UntagResult = OkApply(removed: []) };

        var (code, _, _) = await Run(
            ["untag", path, "work"],
            api,
            isTty: true,
            stdin: "y\n",
            confirmUntag: true);

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Equal(1, api.UntagCalls);
    }

    [Fact]
    public async Task Tag_idempotent_prints_unchanged()
    {
        var path = CreateTempFile();
        var api = new FakeApi { TagResult = OkApply(added: []) };

        var (code, stdout, _) = await Run(["tag", path, "工作"], api);

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Equal("unchanged" + Environment.NewLine, stdout);
    }

    [Fact]
    public async Task Tag_added_prints_alias_in_user_order()
    {
        var path = CreateTempFile();
        var tagId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var api = new FakeApi
        {
            TagResult = OkApply(
                added: [tagId],
                tags: [Tag(tagId, "Work", ["工作"])]),
        };

        var (code, stdout, _) = await Run(["tag", path, "工作"], api);

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Equal("added: 工作" + Environment.NewLine, stdout);
    }

    [Fact]
    public async Task Tag_create_prints_created_name_on_second_line()
    {
        var path = CreateTempFile();
        var tagId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var created = Tag(tagId, "Work", ["工作"]);
        var api = new FakeApi
        {
            TagResult = OkApply(
                added: [tagId],
                tags: [created],
                created: [created]),
        };

        var (code, stdout, _) = await Run(["tag", "--create", path, "工作"], api);

        Assert.Equal(ExitCodes.Ok, code);
        Assert.True(api.LastCreateMissing);
        Assert.Equal(
            "added: 工作" + Environment.NewLine + "created: Work" + Environment.NewLine,
            stdout);
    }

    [Fact]
    public async Task Tag_json_writes_data_without_envelope_status()
    {
        var path = CreateTempFile();
        var tagId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var tag = Tag(tagId, "Work", ["工作"], ns: "job");
        var api = new FakeApi
        {
            TagResult = OkApply(added: [tagId], tags: [tag], created: [tag]),
        };

        var (code, stdout, _) = await Run(["--json", "tag", path, "工作"], api);

        Assert.Equal(ExitCodes.Ok, code);
        using var doc = JsonDocument.Parse(stdout);
        Assert.False(doc.RootElement.TryGetProperty("status", out _));
        Assert.True(doc.RootElement.TryGetProperty("resource", out _));
        Assert.True(doc.RootElement.TryGetProperty("createdTags", out _));
        Assert.Equal("job", doc.RootElement.GetProperty("resource").GetProperty("tags")[0].GetProperty("namespace").GetString());
    }

    [Fact]
    public async Task Tags_prints_aliases_in_api_then_alias_order()
    {
        var path = CreateTempFile();
        var a = Tag(Guid.NewGuid(), "A", ["a1", "a2"]);
        var b = Tag(Guid.NewGuid(), "B", ["b1"]);
        var api = new FakeApi
        {
            TagsResult = ElResult<TagsPage>.Ok(new TagsPage([a, b], 0, 2, 2)),
        };

        var (code, stdout, _) = await Run(["tags", path], api);

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Equal(
            "a1" + Environment.NewLine + "a2" + Environment.NewLine + "b1" + Environment.NewLine,
            stdout);
    }

    [Fact]
    public async Task Tags_json_includes_page_fields()
    {
        var path = CreateTempFile();
        var api = new FakeApi
        {
            TagsResult = ElResult<TagsPage>.Ok(new TagsPage([], 0, 0, 0)),
        };

        var (code, stdout, _) = await Run(["--json", "tags", path], api);

        Assert.Equal(ExitCodes.Ok, code);
        using var doc = JsonDocument.Parse(stdout);
        Assert.True(doc.RootElement.TryGetProperty("items", out _));
        Assert.Equal(0, doc.RootElement.GetProperty("offset").GetInt32());
        Assert.Equal(0, doc.RootElement.GetProperty("limit").GetInt32());
        Assert.Equal(0, doc.RootElement.GetProperty("total").GetInt32());
    }

    private static string CreateTempFile()
    {
        var path = Path.GetTempFileName();
        return path;
    }

    private static ElResult<TagApplyData> OkApply(
        IReadOnlyList<Guid>? added = null,
        IReadOnlyList<Guid>? removed = null,
        IReadOnlyList<TagInfo>? tags = null,
        IReadOnlyList<TagInfo>? created = null) =>
        ElResult<TagApplyData>.Ok(new TagApplyData(
            new ResourceDetail(
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                "file:///tmp/x",
                "file",
                null,
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch,
                1,
                tags ?? []),
            created ?? [],
            added ?? [],
            removed ?? [],
            1));

    private static TagInfo Tag(Guid id, string name, IReadOnlyList<string> aliases, string? ns = null) =>
        new(
            id,
            name,
            ns,
            null,
            "w",
            aliases,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            1);

    private static async Task<(int Code, string Stdout, string Stderr)> Run(
        string[] args,
        FakeApi api,
        bool isTty = false,
        string stdin = "",
        bool confirmUntag = true)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        using var stdinReader = new StringReader(stdin);
        var code = await ElApp.RunAsync(new ElInvocation
        {
            Args = args,
            Stdout = stdout,
            Stderr = stderr,
            Stdin = stdinReader,
            IsTty = isTty,
            Api = api,
            LoadConfig = _ => ElResult<ElConfig>.Ok(
                new ElConfig("http://127.0.0.1:5123/", "token", confirmUntag, 16, 8, 4)),
        });
        return (code, stdout.ToString(), stderr.ToString());
    }

    private sealed class FakeApi : IElApi
    {
        public int TagCalls;
        public int UntagCalls;
        public string? LastUri;
        public string? LastKind;
        public IReadOnlyList<string>? LastAliases;
        public bool? LastCreateMissing;
        public ElResult<TagApplyData>? TagResult;
        public ElResult<TagApplyData>? UntagResult;
        public ElResult<TagsPage>? TagsResult;

        public Task<ElResult<TagApplyData>> TagAsync(
            string uri,
            string kind,
            IReadOnlyList<string> aliases,
            bool createMissing,
            CancellationToken ct = default)
        {
            TagCalls++;
            LastUri = uri;
            LastKind = kind;
            LastAliases = aliases;
            LastCreateMissing = createMissing;
            return Task.FromResult(TagResult ?? OkApply(added: []));
        }

        public Task<ElResult<TagApplyData>> UntagAsync(
            string uri,
            IReadOnlyList<string> aliases,
            CancellationToken ct = default)
        {
            UntagCalls++;
            LastUri = uri;
            LastAliases = aliases;
            return Task.FromResult(UntagResult ?? OkApply(removed: []));
        }

        public Task<ElResult<TagsPage>> GetTagsAsync(string uri, CancellationToken ct = default)
        {
            LastUri = uri;
            return Task.FromResult(TagsResult ?? ElResult<TagsPage>.Ok(new TagsPage([], 0, 0, 0)));
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
            throw new NotImplementedException();

        public Task<ElResult<PreviewPage>> PreviewAsync(
            string q,
            string? expand,
            int? maxDepth,
            int? expandedTagLimit,
            int limit,
            string? kind = null,
            CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<ElResult<SuggestPage>> SuggestAsync(string prefix, int limit, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<ElResult<TagsPage>> ListTagsAsync(
            string? q,
            int limit,
            int offset,
            CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<ElResult<TagInfo>> RenameTagAsync(
            string from,
            string to,
            CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<ElResult<MetaData>> DeleteTagAsync(Guid id, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<ElResult<HealthData>> HealthAsync(CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<ElResult<MetaData>> MetaAsync(CancellationToken ct = default) =>
            throw new NotImplementedException();
    }
}
