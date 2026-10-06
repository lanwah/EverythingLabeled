namespace EverythingLabeled.Service.Tests;

public sealed class QueryCacheSingleflightTests
{
    [Fact]
    public async Task Same_key_shares_one_load()
    {
        var flight = new EverythingLabeled.Service.Services.Caching.QueryCacheSingleflight();
        var loadCalls = 0;
        var gate = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<string> Load()
        {
            Interlocked.Increment(ref loadCalls);
            return await gate.Task;
        }

        var first = flight.RunAsync("k", Load);
        var second = flight.RunAsync("k", Load);
        gate.SetResult("shared");

        Assert.Equal("shared", await first);
        Assert.Equal("shared", await second);
        Assert.Equal(1, loadCalls);
    }

    [Fact]
    public async Task Failed_load_is_removed_so_next_run_retries()
    {
        var flight = new EverythingLabeled.Service.Services.Caching.QueryCacheSingleflight();
        var loadCalls = 0;

        async Task<string> Load()
        {
            loadCalls++;
            if (loadCalls == 1)
                throw new InvalidOperationException("boom");
            return "ok";
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => flight.RunAsync("k", Load));
        Assert.Equal("ok", await flight.RunAsync("k", Load));
        Assert.Equal(2, loadCalls);
    }
}
