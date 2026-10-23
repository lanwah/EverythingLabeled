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
            3,
            2));

        var r = ExplorerConfigLoader.LoadFrom(loaded, @"C:\el\config.json");

        Assert.True(r.IsSuccess);
        Assert.Equal("http://127.0.0.1:5123/", r.Data!.BaseUrl);
        Assert.Equal("secret", r.Data.Token);
        Assert.True(r.Data.ConfirmUntag);
        Assert.Equal(8, r.Data.PreviewLimit);
        Assert.Equal(3, r.Data.SuggestLimit);
        Assert.Equal(2, r.Data.MaxConcurrency);
        Assert.False(r.Data.FindUseOr);
    }

    [Fact]
    public void LoadFrom_maps_findUseOr_when_requested()
    {
        var loaded = ElResult<ElConfig>.Ok(new ElConfig(
            "http://127.0.0.1:5123/",
            "secret",
            true,
            16,
            8,
            4));

        var r = ExplorerConfigLoader.LoadFrom(loaded, @"C:\el\config.json", findUseOr: true);

        Assert.True(r.IsSuccess);
        Assert.True(r.Data!.FindUseOr);
    }

    [Theory]
    [InlineData("or", true)]
    [InlineData("OR", true)]
    [InlineData("and", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ParseUseOr_recognizes_or_only(string? raw, bool expected)
    {
        Assert.Equal(expected, FindTagLogicConfig.ParseUseOr(raw));
    }

    [Fact]
    public void ReadFindUseOr_reads_or_from_config_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "el-explorer-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "config.json");
        File.WriteAllText(
            path,
            """
            {
              "baseUrl": "http://127.0.0.1:5123",
              "token": "secret",
              "findTagLogic": "or"
            }
            """);

        try
        {
            Assert.True(ExplorerConfigLoader.ReadFindUseOr(path));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LoadFrom_rejects_localhost()
    {
        var loaded = ElResult<ElConfig>.Ok(new ElConfig(
            "http://localhost:5123/",
            "secret",
            true,
            16,
            8,
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
            8,
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
