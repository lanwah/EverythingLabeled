using EverythingLabeled.ExplorerShared;

namespace EverythingLabeled.ExplorerHost.Tests;

public sealed class PathsFileTests
{
    [Fact]
    public void Write_then_ReadAndDelete_roundtrips_and_deletes_file()
    {
        PathsFile.EnsureTmpDirectory();
        var path = PathsFile.Write([@"C:\one.txt", @"D:\two\file.bin"]);

        Assert.True(File.Exists(path));
        Assert.StartsWith(PathsFile.TmpDirectory, path, StringComparison.OrdinalIgnoreCase);

        var lines = PathsFile.ReadAndDelete(path);

        Assert.Equal([@"C:\one.txt", @"D:\two\file.bin"], lines);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void ReadAndDelete_missing_file_returns_empty()
    {
        var missing = Path.Combine(PathsFile.TmpDirectory, Guid.NewGuid().ToString("N") + ".txt");
        var lines = PathsFile.ReadAndDelete(missing);
        Assert.Empty(lines);
    }

    [Fact]
    public void CleanupStale_deletes_old_keeps_new()
    {
        PathsFile.EnsureTmpDirectory();
        var oldPath = Path.Combine(PathsFile.TmpDirectory, "stale-" + Guid.NewGuid().ToString("N") + ".txt");
        var newPath = Path.Combine(PathsFile.TmpDirectory, "fresh-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(oldPath, "old");
        File.WriteAllText(newPath, "new");

        var now = DateTimeOffset.UtcNow;
        File.SetLastWriteTimeUtc(oldPath, now.AddHours(-2).UtcDateTime);
        File.SetLastWriteTimeUtc(newPath, now.AddMinutes(-10).UtcDateTime);

        try
        {
            PathsFile.CleanupStale(now, TimeSpan.FromMinutes(PathsFile.StaleMinutes));

            Assert.False(File.Exists(oldPath));
            Assert.True(File.Exists(newPath));
        }
        finally
        {
            if (File.Exists(oldPath))
            {
                File.Delete(oldPath);
            }

            if (File.Exists(newPath))
            {
                File.Delete(newPath);
            }
        }
    }

    [Fact]
    public void TmpDirectory_is_under_local_appdata()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EverythingLabeled",
            "tmp");
        Assert.Equal(expected, PathsFile.TmpDirectory);
        Assert.Equal(60, PathsFile.StaleMinutes);
    }
}
