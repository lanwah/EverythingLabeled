using EverythingLabeled.ExplorerShared;

namespace EverythingLabeled.ExplorerHost.Tests;

public sealed class FindHostIpcTests
{
    [Fact]
    public void ResolveMutexName_without_env_returns_default()
    {
        var name = FindHostIpc.ResolveMutexName(new Dictionary<string, string?>());
        Assert.Equal(FindHostIpc.DefaultMutexName, name);
    }

    [Fact]
    public void ResolveMutexName_uses_override_when_non_blank()
    {
        var name = FindHostIpc.ResolveMutexName(new Dictionary<string, string?>
        {
            ["EL_FIND_MUTEX_NAME"] = @"Local\Test.FindMutex",
        });
        Assert.Equal(@"Local\Test.FindMutex", name);
    }

    [Fact]
    public void ResolveMutexName_blank_override_falls_back_to_default()
    {
        var name = FindHostIpc.ResolveMutexName(new Dictionary<string, string?>
        {
            ["EL_FIND_MUTEX_NAME"] = "   ",
        });
        Assert.Equal(FindHostIpc.DefaultMutexName, name);
    }

    [Fact]
    public void ResolvePipeName_without_env_returns_default()
    {
        var name = FindHostIpc.ResolvePipeName(new Dictionary<string, string?>());
        Assert.Equal(FindHostIpc.DefaultPipeName, name);
    }

    [Fact]
    public void ResolvePipeName_uses_override_when_non_blank()
    {
        var name = FindHostIpc.ResolvePipeName(new Dictionary<string, string?>
        {
            ["EL_FIND_PIPE_NAME"] = "Test.FindPipe",
        });
        Assert.Equal("Test.FindPipe", name);
    }

    [Fact]
    public void TrySendActivate_without_server_returns_false()
    {
        var pipeName = "EL.FindHostIpc.NoServer." + Guid.NewGuid().ToString("N");
        var ok = FindHostIpc.TrySendActivate(pipeName, TimeSpan.FromMilliseconds(200));
        Assert.False(ok);
    }

    [Fact]
    public void ActivateLine_is_Activate_with_newline()
    {
        Assert.Equal("Activate\n", FindHostIpc.ActivateLine);
    }
}
