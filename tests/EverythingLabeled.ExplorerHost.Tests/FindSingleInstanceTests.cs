using EverythingLabeled.ExplorerHost;
using EverythingLabeled.ExplorerShared;

namespace EverythingLabeled.ExplorerHost.Tests;

public sealed class FindSingleInstanceTests
{
    [Fact]
    public void TryAcquire_second_fails_until_first_disposed()
    {
        var mutexName = @"Local\EL.FindSingle." + Guid.NewGuid().ToString("N");
        var pipeName = "EL.FindSingle.Pipe." + Guid.NewGuid().ToString("N");

        Assert.True(FindSingleInstance.TryAcquire(mutexName, pipeName, static () => { }, out var first));
        Assert.NotNull(first);

        try
        {
            Assert.False(FindSingleInstance.TryAcquire(mutexName, pipeName, static () => { }, out var second));
            Assert.Null(second);
        }
        finally
        {
            first!.Dispose();
        }

        Assert.True(FindSingleInstance.TryAcquire(mutexName, pipeName, static () => { }, out var third));
        Assert.NotNull(third);
        third!.Dispose();
    }

    [Fact]
    public void TryAcquire_pipe_activate_invokes_callback()
    {
        var mutexName = @"Local\EL.FindSingle.Act." + Guid.NewGuid().ToString("N");
        var pipeName = "EL.FindSingle.ActPipe." + Guid.NewGuid().ToString("N");
        using var signaled = new ManualResetEventSlim(false);

        Assert.True(FindSingleInstance.TryAcquire(mutexName, pipeName, () => signaled.Set(), out var instance));
        Assert.NotNull(instance);

        try
        {
            // Give the server thread a moment to start listening.
            Thread.Sleep(50);

            var sent = false;
            for (var i = 0; i < 20 && !sent; i++)
            {
                sent = FindHostIpc.TrySendActivate(pipeName, TimeSpan.FromMilliseconds(200));
                if (!sent)
                    Thread.Sleep(50);
            }

            Assert.True(sent);
            Assert.True(signaled.Wait(TimeSpan.FromSeconds(2)));
        }
        finally
        {
            instance!.Dispose();
        }
    }
}
