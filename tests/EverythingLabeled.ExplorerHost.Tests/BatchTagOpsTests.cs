using EverythingLabeled.Client;
using EverythingLabeled.ExplorerHost;

namespace EverythingLabeled.ExplorerHost.Tests;

public sealed class BatchTagOpsTests
{
    [Fact]
    public async Task Tag_respects_max_concurrency()
    {
        var api = new FakeApi { Delay = TimeSpan.FromMilliseconds(80) };
        var targets = Enumerable.Range(0, 6)
            .Select(i => ($"file:///c:/t{i}.txt", "file"))
            .ToList();

        var summary = await BatchTagOps.TagAsync(
            api,
            targets,
            "work",
            createMissing: false,
            maxConcurrency: 2,
            CancellationToken.None);

        Assert.Equal(6, summary.Success);
        Assert.Equal(0, summary.Failed);
        Assert.Equal(0, summary.CancelledNotSent);
        Assert.True(api.PeakConcurrent <= 2, $"peak was {api.PeakConcurrent}");
        Assert.Equal(6, api.TagCalls);
        Assert.All(api.TagAliasLists, a => Assert.Equal(["work"], a));
        Assert.All(api.TagCreateMissingFlags, f => Assert.False(f));
    }

    [Fact]
    public async Task Tag_cancel_marks_unstarted_and_awaits_in_flight()
    {
        var api = new FakeApi { Delay = TimeSpan.FromMilliseconds(200) };
        using var cts = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        api.OnTagStarted = _ =>
        {
            if (Interlocked.Increment(ref api.StartedSignalCount) == 1)
                started.TrySetResult();
        };

        var targets = Enumerable.Range(0, 4)
            .Select(i => ($"file:///c:/c{i}.txt", "file"))
            .ToList();

        var run = BatchTagOps.TagAsync(
            api,
            targets,
            "work",
            createMissing: true,
            maxConcurrency: 1,
            cts.Token);

        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cts.Cancel();

        var summary = await run;

        Assert.Equal(1, summary.Success);
        Assert.Equal(0, summary.Failed);
        Assert.Equal(3, summary.CancelledNotSent);
        Assert.Equal(1, api.TagCalls);
        Assert.Equal(4, summary.Items.Count);
        Assert.True(summary.Items[0].Ok);
        Assert.False(summary.Items[0].CancelledBeforeSend);
        Assert.All(summary.Items.Skip(1), i =>
        {
            Assert.True(i.CancelledBeforeSend);
            Assert.False(i.Ok);
        });
    }

    [Fact]
    public async Task Tag_401_aborts_remaining_unsent()
    {
        var api = new FakeApi
        {
            Delay = TimeSpan.FromMilliseconds(30),
            ResultByUri =
            {
                ["file:///c:/a.txt"] = ElResult<TagApplyData>.Fail(
                    new ElError("unauthorized", "nope", HttpStatus: 401)),
            },
        };
        var targets = new List<(string Uri, string Kind)>
        {
            ("file:///c:/a.txt", "file"),
            ("file:///c:/b.txt", "file"),
            ("file:///c:/c.txt", "file"),
            ("file:///c:/d.txt", "file"),
        };

        var summary = await BatchTagOps.TagAsync(
            api,
            targets,
            "work",
            createMissing: false,
            maxConcurrency: 1,
            CancellationToken.None);

        Assert.Equal(1, api.TagCalls);
        Assert.Equal(0, summary.Success);
        Assert.Equal(1, summary.Failed);
        Assert.Equal(3, summary.CancelledNotSent);
        Assert.False(summary.Items[0].Ok);
        Assert.Equal("unauthorized", summary.Items[0].ErrorCode);
        Assert.False(summary.Items[0].CancelledBeforeSend);
        Assert.All(summary.Items.Skip(1), i => Assert.True(i.CancelledBeforeSend));
    }

    [Fact]
    public async Task Untag_three_ok_one_fail_summary()
    {
        var api = new FakeApi
        {
            ResultByUri =
            {
                ["file:///c:/bad.txt"] = ElResult<TagApplyData>.Fail(
                    new ElError("not_found", "missing tag")),
            },
        };
        var uris = new[]
        {
            "file:///c:/1.txt",
            "file:///c:/2.txt",
            "file:///c:/bad.txt",
            "file:///c:/4.txt",
        };

        var summary = await BatchTagOps.UntagAsync(
            api,
            uris,
            "work",
            maxConcurrency: 2,
            CancellationToken.None);

        Assert.Equal(3, summary.Success);
        Assert.Equal(1, summary.Failed);
        Assert.Equal(0, summary.CancelledNotSent);
        Assert.Equal(4, api.UntagCalls);
        Assert.All(api.UntagAliasLists, a => Assert.Equal(["work"], a));
        var bad = Assert.Single(summary.Items, i => i.Uri == "file:///c:/bad.txt");
        Assert.False(bad.Ok);
        Assert.Equal("not_found", bad.ErrorCode);
        Assert.Equal("missing tag", bad.ErrorMessage);
        Assert.False(bad.CancelledBeforeSend);
    }

    private sealed class FakeApi : IElApi
    {
        private int _concurrent;
        public int PeakConcurrent;
        public int TagCalls;
        public int UntagCalls;
        public int StartedSignalCount;
        public TimeSpan Delay = TimeSpan.Zero;
        public Action<string>? OnTagStarted;
        public Dictionary<string, ElResult<TagApplyData>> ResultByUri { get; } = new(StringComparer.Ordinal);
        public List<IReadOnlyList<string>> TagAliasLists { get; } = [];
        public List<IReadOnlyList<string>> UntagAliasLists { get; } = [];
        public List<bool> TagCreateMissingFlags { get; } = [];

        public async Task<ElResult<TagApplyData>> TagAsync(
            string uri,
            string kind,
            IReadOnlyList<string> aliases,
            bool createMissing,
            CancellationToken ct = default)
        {
            Interlocked.Increment(ref TagCalls);
            TagAliasLists.Add(aliases.ToArray());
            TagCreateMissingFlags.Add(createMissing);
            OnTagStarted?.Invoke(uri);

            var now = Interlocked.Increment(ref _concurrent);
            UpdatePeak(now);
            try
            {
                if (Delay > TimeSpan.Zero)
                    await Task.Delay(Delay, CancellationToken.None);
                return Lookup(uri);
            }
            finally
            {
                Interlocked.Decrement(ref _concurrent);
            }
        }

        public async Task<ElResult<TagApplyData>> UntagAsync(
            string uri,
            IReadOnlyList<string> aliases,
            CancellationToken ct = default)
        {
            Interlocked.Increment(ref UntagCalls);
            UntagAliasLists.Add(aliases.ToArray());

            var now = Interlocked.Increment(ref _concurrent);
            UpdatePeak(now);
            try
            {
                if (Delay > TimeSpan.Zero)
                    await Task.Delay(Delay, CancellationToken.None);
                return Lookup(uri);
            }
            finally
            {
                Interlocked.Decrement(ref _concurrent);
            }
        }

        private void UpdatePeak(int now)
        {
            int peak;
            do
            {
                peak = PeakConcurrent;
                if (now <= peak)
                    return;
            } while (Interlocked.CompareExchange(ref PeakConcurrent, now, peak) != peak);
        }

        private ElResult<TagApplyData> Lookup(string uri) =>
            ResultByUri.TryGetValue(uri, out var r) ? r : OkApply(uri);

        private static ElResult<TagApplyData> OkApply(string uri) =>
            ElResult<TagApplyData>.Ok(new TagApplyData(
                new ResourceDetail(
                    Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                    uri,
                    "file",
                    null,
                    DateTimeOffset.UnixEpoch,
                    DateTimeOffset.UnixEpoch,
                    1,
                    []),
                [],
                [],
                [],
                1));

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

        public Task<ElResult<SuggestPage>> SuggestAsync(string prefix, int limit, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<ElResult<HealthData>> HealthAsync(CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<ElResult<MetaData>> MetaAsync(CancellationToken ct = default) =>
            throw new NotImplementedException();
    }
}
