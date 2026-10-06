using EverythingLabeled.Client;
using EverythingLabeled.Mcp;

namespace EverythingLabeled.Mcp.Tests;

public sealed class McpAppOfflineTests
{
    [Fact]
    public async Task Version_is_offline()
    {
        var hostCalls = 0;
        var loadCalls = 0;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var code = await McpApp.RunAsync(new McpInvocation
        {
            Args = ["--version"],
            Stdout = stdout,
            Stderr = stderr,
            LoadConfig = _ =>
            {
                loadCalls++;
                throw new InvalidOperationException("LoadConfig must not be called for --version.");
            },
            RunHost = (_, _) =>
            {
                hostCalls++;
                return Task.FromResult(0);
            },
        });

        Assert.Equal(0, code);
        Assert.Contains("0.1.0", stdout.ToString());
        Assert.Equal("", stderr.ToString());
        Assert.Equal(0, hostCalls);
        Assert.Equal(0, loadCalls);
    }

    [Fact]
    public async Task Help_is_offline()
    {
        var hostCalls = 0;
        var loadCalls = 0;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var code = await McpApp.RunAsync(new McpInvocation
        {
            Args = ["--help"],
            Stdout = stdout,
            Stderr = stderr,
            LoadConfig = _ =>
            {
                loadCalls++;
                throw new InvalidOperationException("LoadConfig must not be called for --help.");
            },
            RunHost = (_, _) =>
            {
                hostCalls++;
                return Task.FromResult(0);
            },
        });

        Assert.Equal(0, code);
        Assert.Contains("--config", stdout.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, hostCalls);
        Assert.Equal(0, loadCalls);
    }

    [Fact]
    public async Task Unknown_flag_does_not_start_host()
    {
        var hostCalls = 0;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var code = await McpApp.RunAsync(new McpInvocation
        {
            Args = ["--nope"],
            Stdout = stdout,
            Stderr = stderr,
            RunHost = (_, _) =>
            {
                hostCalls++;
                return Task.FromResult(0);
            },
        });

        Assert.Equal(1, code);
        Assert.Equal(0, hostCalls);
        Assert.False(string.IsNullOrWhiteSpace(stderr.ToString()));
        Assert.DoesNotContain("jsonrpc", stdout.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Flags_are_passed_to_loader_and_host_starts()
    {
        ElConfigOverrides? seen = null;
        var hostCalls = 0;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var code = await McpApp.RunAsync(new McpInvocation
        {
            Args = ["--config", @"C:\el\config.json", "--base-url", "http://127.0.0.1:9/", "--token", "secret"],
            Stdout = stdout,
            Stderr = stderr,
            LoadConfig = overrides =>
            {
                seen = overrides;
                return ElResult<ElConfig>.Ok(new ElConfig("http://127.0.0.1:9/", "secret", true));
            },
            RunHost = (runtime, _) =>
            {
                hostCalls++;
                Assert.True(runtime.HasBaseUrl);
                Assert.True(runtime.HasToken);
                return Task.FromResult(0);
            },
        });

        Assert.Equal(0, code);
        Assert.Equal(1, hostCalls);
        Assert.NotNull(seen);
        Assert.Equal(@"C:\el\config.json", seen.ConfigPathFlag);
        Assert.Equal("http://127.0.0.1:9/", seen.BaseUrlFlag);
        Assert.Equal("secret", seen.TokenFlag);
    }
}
