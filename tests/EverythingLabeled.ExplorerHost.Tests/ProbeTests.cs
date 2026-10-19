using EverythingLabeled.Client;
using EverythingLabeled.ExplorerHost;

namespace EverythingLabeled.ExplorerHost.Tests;

public sealed class ProbeTests
{
    [Fact]
    public async Task RunAsync_fails_when_health_fails()
    {
        var api = new FakeApi
        {
            Health = ElResult<HealthData>.Fail(
                new ElError(ElErrorCodes.ConnectionFailed, "down")),
        };

        var r = await Probe.RunAsync(api, CancellationToken.None);

        Assert.False(r.IsSuccess);
        Assert.Equal(Copy.ServiceUnreachable, r.Error!.Value.Message);
        Assert.Equal(0, api.MetaCalls);
    }

    [Fact]
    public async Task RunAsync_fails_when_meta_fails()
    {
        var api = new FakeApi
        {
            Health = ElResult<HealthData>.Ok(new HealthData("ok", 1)),
            Meta = ElResult<MetaData>.Fail(
                new ElError("unauthorized", "Nope.", 401)),
        };

        var r = await Probe.RunAsync(api, CancellationToken.None);

        Assert.False(r.IsSuccess);
        Assert.Equal(Copy.TokenInvalid, r.Error!.Value.Message);
        Assert.Equal(1, api.HealthCalls);
        Assert.Equal(1, api.MetaCalls);
    }

    [Fact]
    public async Task RunAsync_succeeds_when_health_and_meta_ok()
    {
        var api = new FakeApi
        {
            Health = ElResult<HealthData>.Ok(new HealthData("ok", 3)),
            Meta = ElResult<MetaData>.Ok(new MetaData(3)),
        };

        var r = await Probe.RunAsync(api, CancellationToken.None);

        Assert.True(r.IsSuccess);
        Assert.Equal(1, api.HealthCalls);
        Assert.Equal(1, api.MetaCalls);
    }

    private sealed class FakeApi : IElApi
    {
        public ElResult<HealthData> Health { get; init; } =
            ElResult<HealthData>.Ok(new HealthData("ok", 1));

        public ElResult<MetaData> Meta { get; init; } =
            ElResult<MetaData>.Ok(new MetaData(1));

        public int HealthCalls { get; private set; }
        public int MetaCalls { get; private set; }

        public Task<ElResult<TagApplyData>> TagAsync(
            string uri,
            string kind,
            IReadOnlyList<string> aliases,
            bool createMissing,
            CancellationToken ct = default) =>
            throw new NotImplementedException();

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
            CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<ElResult<PreviewPage>> PreviewAsync(
            string q,
            string? expand,
            int? maxDepth,
            int? expandedTagLimit,
            int limit,
            CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<ElResult<SuggestPage>> SuggestAsync(
            string prefix,
            int limit,
            CancellationToken ct = default) =>
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

        public Task<ElResult<HealthData>> HealthAsync(CancellationToken ct = default)
        {
            HealthCalls++;
            return Task.FromResult(Health);
        }

        public Task<ElResult<MetaData>> MetaAsync(CancellationToken ct = default)
        {
            MetaCalls++;
            return Task.FromResult(Meta);
        }
    }
}
