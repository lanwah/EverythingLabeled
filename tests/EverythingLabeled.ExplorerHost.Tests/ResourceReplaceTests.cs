using EverythingLabeled.Client;

namespace EverythingLabeled.ExplorerHost.Tests;

public sealed class ResourceReplaceTests
{
    [Fact]
    public async Task Copies_tags_then_deletes_the_old_resource()
    {
        var source = Guid.NewGuid();
        var created = Guid.NewGuid();
        var api = new FakeApi(created, "file:///D:/new.txt");

        var result = await ResourceReplace.ExecuteAsync(
            api,
            source,
            ["工作", "项目"],
            @"D:\new.txt",
            ReplaceKindGuess.File);

        Assert.True(result.IsSuccess);
        Assert.Equal(["file:///D:/new.txt"], api.TaggedUris);
        Assert.Equal(source, api.Deleted);
    }

    [Fact]
    public async Task Same_canonical_resource_is_not_deleted()
    {
        var source = Guid.NewGuid();
        var api = new FakeApi(source, "file:///D:/same.txt");

        var result = await ResourceReplace.ExecuteAsync(
            api,
            source,
            ["工作"],
            @"D:\same.txt",
            ReplaceKindGuess.File);

        Assert.False(result.IsSuccess);
        Assert.Equal(Copy.ReplaceSameResource, result.Error?.Message);
        Assert.Null(api.Deleted);
    }

    [Fact]
    public async Task Url_kind_on_a_file_path_is_rejected()
    {
        var api = new FakeApi(Guid.NewGuid(), "file:///D:/a.txt");

        var result = await ResourceReplace.ExecuteAsync(
            api,
            Guid.NewGuid(),
            ["工作"],
            @"D:\a.txt",
            ReplaceKindGuess.Url);

        Assert.False(result.IsSuccess);
        Assert.Equal(Copy.ReplaceKindMismatch, result.Error?.Message);
        Assert.Empty(api.TaggedUris);
    }

    private sealed class FakeApi(Guid createdId, string createdUri) : IElApi
    {
        public List<string> TaggedUris { get; } = [];

        public Guid? Deleted { get; private set; }

        public Task<ElResult<TagApplyData>> TagAsync(
            string uri,
            string kind,
            IReadOnlyList<string> aliases,
            bool createMissing,
            CancellationToken ct = default)
        {
            TaggedUris.Add(uri);
            var data = new TagApplyData(
                new ResourceDetail(
                    createdId,
                    createdUri,
                    kind,
                    null,
                    DateTimeOffset.UnixEpoch,
                    DateTimeOffset.UnixEpoch,
                    0,
                    []),
                [],
                [],
                [],
                1);
            return Task.FromResult(ElResult<TagApplyData>.Ok(data));
        }

        public Task<ElResult<MetaData>> DeleteResourceAsync(Guid id, CancellationToken ct = default)
        {
            Deleted = id;
            return Task.FromResult(ElResult<MetaData>.Ok(new MetaData(2)));
        }

        public Task<ElResult<TagApplyData>> UntagAsync(
            string uri,
            IReadOnlyList<string> aliases,
            CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<ElResult<TagsPage>> GetTagsAsync(string uri, CancellationToken ct = default) =>
            throw new NotImplementedException();

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

        public Task<ElResult<TagInfo>> RenameTagAsync(string from, string to, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<ElResult<MetaData>> DeleteTagAsync(Guid id, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<ElResult<HealthData>> HealthAsync(CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<ElResult<MetaData>> MetaAsync(CancellationToken ct = default) =>
            throw new NotImplementedException();
    }
}
