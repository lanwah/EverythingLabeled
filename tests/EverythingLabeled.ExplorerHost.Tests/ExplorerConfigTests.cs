using EverythingLabeled.Client;
using EverythingLabeled.ExplorerHost;

namespace EverythingLabeled.ExplorerHost.Tests;

public sealed class ExplorerConfigTests
{
    [Fact]
    public void LoadFrom_accepts_http_loopback()
    {
        var loaded = ElResult<ElConfig>.Ok(new ElConfig(
            "http://127.0.0.1:5123/",
            "secret",
            true,
            8,
            2));

        var r = ExplorerConfigLoader.LoadFrom(loaded, @"C:\el\config.json");

        Assert.True(r.IsSuccess);
        Assert.Equal("http://127.0.0.1:5123/", r.Data!.BaseUrl);
        Assert.Equal("secret", r.Data.Token);
        Assert.True(r.Data.ConfirmUntag);
        Assert.Equal(8, r.Data.PreviewLimit);
        Assert.Equal(2, r.Data.MaxConcurrency);
    }

    [Fact]
    public void LoadFrom_rejects_localhost()
    {
        var loaded = ElResult<ElConfig>.Ok(new ElConfig(
            "http://localhost:5123/",
            "secret",
            true,
            16,
            4));

        var r = ExplorerConfigLoader.LoadFrom(loaded, @"C:\el\config.json");

        Assert.False(r.IsSuccess);
        Assert.Equal(ElErrorCodes.ConfigMissing, r.Error!.Value.Code);
        Assert.Contains(@"C:\el\config.json", r.Error.Value.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadFrom_rejects_https_loopback()
    {
        var loaded = ElResult<ElConfig>.Ok(new ElConfig(
            "https://127.0.0.1:5123/",
            "secret",
            true,
            16,
            4));

        var r = ExplorerConfigLoader.LoadFrom(loaded, @"C:\el\config.json");

        Assert.False(r.IsSuccess);
        Assert.Equal(ElErrorCodes.ConfigMissing, r.Error!.Value.Code);
        Assert.Contains(@"C:\el\config.json", r.Error.Value.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadFrom_propagates_loader_failure_with_config_path()
    {
        var loaded = ElResult<ElConfig>.Fail(
            new ElError(ElErrorCodes.ConfigMissing, "Configuration is missing or invalid."));

        var r = ExplorerConfigLoader.LoadFrom(loaded, @"D:\cfg\config.json");

        Assert.False(r.IsSuccess);
        Assert.Equal(ElErrorCodes.ConfigMissing, r.Error!.Value.Code);
        Assert.Contains(@"D:\cfg\config.json", r.Error.Value.Message, StringComparison.Ordinal);
    }
}
