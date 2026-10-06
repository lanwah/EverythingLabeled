using System.Text.Json;
using EverythingLabeled.Client;
using EverythingLabeled.Cli;

namespace EverythingLabeled.Cli.Tests;

public sealed class ElAppSkeletonTests
{
    [Fact]
    public async Task Version_prints_0_1_0_without_loading_config()
    {
        var loadCalled = false;
        var (code, stdout, stderr) = await Run(
            ["--version"],
            _ =>
            {
                loadCalled = true;
                throw new InvalidOperationException("LoadConfig must not be called for --version.");
            });

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains("0.1.0", stdout);
        Assert.Equal("", stderr);
        Assert.False(loadCalled);
    }

    [Fact]
    public async Task Json_version_still_prints_plain_text_version()
    {
        var (code, stdout, stderr) = await Run(
            ["--json", "--version"],
            _ => throw new InvalidOperationException("LoadConfig must not be called for --version."));

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains("0.1.0", stdout);
        var err = stderr.TrimStart();
        Assert.False(err.StartsWith('{') && err.Contains("\"error\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Help_is_english_and_lists_json_and_version()
    {
        var (code, stdout, stderr) = await Run(
            ["--help"],
            _ => throw new InvalidOperationException("LoadConfig must not be called for --help."));

        Assert.Equal(ExitCodes.Ok, code);
        var help = stdout + stderr;
        Assert.Contains("--json", help);
        Assert.Contains("--version", help);
        Assert.DoesNotContain("用法", help);
        Assert.DoesNotContain("选项", help);
    }

    [Fact]
    public async Task Json_unknown_command_writes_bad_request_error_json_only()
    {
        var (code, stdout, stderr) = await Run(
            ["--json", "not-a-real-command"],
            _ => throw new InvalidOperationException("LoadConfig must not be called for unknown command."));

        Assert.Equal(ExitCodes.Usage, code);
        Assert.Equal("", stdout);
        Assert.DoesNotContain("Usage:", stderr);

        using var doc = JsonDocument.Parse(stderr);
        var error = doc.RootElement.GetProperty("error");
        Assert.Equal(ElErrorCodes.BadRequest, error.GetProperty("code").GetString());
        Assert.True(error.TryGetProperty("message", out var message));
        Assert.False(string.IsNullOrWhiteSpace(message.GetString()));
    }

    private static async Task<(int Code, string Stdout, string Stderr)> Run(
        string[] args,
        Func<ElConfigOverrides, ElResult<ElConfig>> loadConfig)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var code = await ElApp.RunAsync(new ElInvocation
        {
            Args = args,
            Stdout = stdout,
            Stderr = stderr,
            LoadConfig = loadConfig,
        });
        return (code, stdout.ToString(), stderr.ToString());
    }
}
