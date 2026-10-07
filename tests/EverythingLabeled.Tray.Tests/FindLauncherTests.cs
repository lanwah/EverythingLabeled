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
}
