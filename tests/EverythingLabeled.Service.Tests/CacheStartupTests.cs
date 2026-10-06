namespace EverythingLabeled.Service.Tests;

public sealed class CacheStartupTests
{
    [Fact]
    public async Task Redis_unreachable_exits_nonzero()
    {
        await using var fx = ServiceProcessFixture.CreateIsolated();
        fx.WriteConfigForFreePort();
        var exit = await fx.RunExpectExitAsync(
            timeout: TimeSpan.FromSeconds(20),
            extraEnv: new Dictionary<string, string>
            {
                ["Cache__Provider"] = "Redis",
                ["Cache__RedisConnection"] = "127.0.0.1:1",
                ["Cache__RedisConnectTimeout"] = "00:00:01",
                ["Cache__KeyPrefix"] = "el:qc:",
            });
        Assert.NotEqual(0, exit);
    }

    [Fact]
    public async Task Null_provider_health_ok()
    {
        await using var fx = ServiceProcessFixture.CreateIsolated();
        fx.WriteConfigForFreePort();
        await fx.StartAsync(extraEnv: new Dictionary<string, string>
        {
            ["Cache__Provider"] = "Null",
        });
        using var response = await fx.GetAsync("/v1/health");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }
}
