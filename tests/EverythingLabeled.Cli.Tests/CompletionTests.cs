using EverythingLabeled.Client;
using EverythingLabeled.Cli;

namespace EverythingLabeled.Cli.Tests;

public sealed class CompletionTests
{
    [Fact]
    public async Task Completion_powershell_prints_script_without_config()
    {
        var loadCalled = false;
        var (code, stdout, _) = await Run(
            ["completion", "powershell"],
            _ =>
            {
                loadCalled = true;
                throw new InvalidOperationException("LoadConfig must not be called.");
            });

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains("Register-ArgumentCompleter", stdout, StringComparison.Ordinal);
        Assert.Contains("suggest --json", stdout, StringComparison.Ordinal);
        Assert.False(loadCalled);
    }

    [Fact]
    public async Task Json_completion_powershell_still_prints_script()
    {
        var (code, stdout, stderr) = await Run(
            ["--json", "completion", "powershell"],
            _ => throw new InvalidOperationException("LoadConfig must not be called."));

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains("Register-ArgumentCompleter", stdout, StringComparison.Ordinal);
        var err = stderr.TrimStart();
        Assert.False(err.StartsWith('{') && err.Contains("\"error\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Completion_bash_is_bad_request()
    {
        var (code, stdout, stderr) = await Run(["completion", "bash"], _ => ElResult<ElConfig>.Ok(
            new ElConfig("http://127.0.0.1:5123/", "t", true, 16, 4)));

        Assert.Equal(ExitCodes.Usage, code);
        Assert.Equal("", stdout);
        Assert.Contains("powershell", stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Docs_cli_exists()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        Assert.True(File.Exists(Path.Combine(root, "docs", "cli.md")));
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
