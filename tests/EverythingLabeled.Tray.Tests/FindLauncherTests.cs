namespace EverythingLabeled.Tray.Tests;

public sealed class FindLauncherTests
{
    [Fact]
    public void ResolveHostPath_combines_directory_and_exe_name()
    {
        var path = FindLauncher.ResolveHostPath(@"D:\apps\tray");
        Assert.Equal(
            Path.Combine(@"D:\apps\tray", FindLauncher.HostExeName),
            path);
    }

    [Fact]
    public void TryActivateOrStart_missing_host_returns_false()
    {
        var dir = Path.Combine(Path.GetTempPath(), "el-tray-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var pipe = "EL.Tray.MissingHost." + Guid.NewGuid().ToString("N");
            var ok = FindLauncher.TryActivateOrStart(dir, pipe, TimeSpan.FromMilliseconds(150));
            Assert.False(ok);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void HostProcessName_matches_exe_without_extension()
    {
        Assert.Equal("EverythingLabeled.ExplorerHost", FindLauncher.HostProcessName);
    }

    [Fact]
    public void TryRestart_missing_host_does_not_stop_processes()
    {
        var dir = CreateEmptyDir();
        var stopped = false;
        FindLauncher.StopHostProcessesOverride = _ =>
        {
            stopped = true;
            return true;
        };
        try
        {
            var result = FindLauncher.TryRestart(dir, TimeSpan.FromMilliseconds(1));
            Assert.Equal(FindRestartResult.HostMissing, result);
            Assert.False(stopped);
        }
        finally
        {
            FindLauncher.StopHostProcessesOverride = null;
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TryRestart_stop_failed_does_not_start()
    {
        var dir = CreateDirWithFakeHost();
        FindLauncher.StopHostProcessesOverride = _ => false;
        try
        {
            var result = FindLauncher.TryRestart(dir, TimeSpan.FromMilliseconds(1));
            Assert.Equal(FindRestartResult.StopFailed, result);
        }
        finally
        {
            FindLauncher.StopHostProcessesOverride = null;
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TryRestart_invalid_host_image_returns_start_failed()
    {
        var dir = CreateDirWithFakeHost();
        FindLauncher.StopHostProcessesOverride = _ => true;
        try
        {
            var result = FindLauncher.TryRestart(dir, TimeSpan.FromMilliseconds(1));
            Assert.Equal(FindRestartResult.StartFailed, result);
        }
        finally
        {
            FindLauncher.StopHostProcessesOverride = null;
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string CreateEmptyDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "el-tray-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string CreateDirWithFakeHost()
    {
        var dir = CreateEmptyDir();
        File.WriteAllText(Path.Combine(dir, FindLauncher.HostExeName), "not a pe image");
        return dir;
    }
}
