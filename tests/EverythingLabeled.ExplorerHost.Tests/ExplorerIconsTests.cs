using EverythingLabeled.ExplorerShared;

namespace EverythingLabeled.ExplorerHost.Tests;

public sealed class ExplorerIconsTests
{
    [Theory]
    [InlineData("tag", "ElTag.ico")]
    [InlineData("untag", "ElUntag.ico")]
    [InlineData("tags", "ElTags.ico")]
    [InlineData("find", "ElFind.ico")]
    [InlineData("library", "ElLibrary.ico")]
    public void FileNameForMode_maps_the_verbs(string mode, string expected)
    {
        Assert.Equal(expected, ExplorerIcons.FileNameForMode(mode));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Tag")]
    [InlineData("unknown")]
    public void FileNameForMode_unknown_mode_has_no_icon(string mode)
    {
        Assert.Null(ExplorerIcons.FileNameForMode(mode));
    }

    [Fact]
    public void Resolve_returns_absolute_path_when_icon_exists()
    {
        var directory = CreateDirectoryWithIcon("tag", "ElTag.ico");
        try
        {
            var path = ExplorerIcons.Resolve(directory, "tag");

            Assert.NotNull(path);
            Assert.Equal(Path.Combine(directory, "Assets", "ElTag.ico"), path);
            Assert.True(Path.IsPathFullyQualified(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Resolve_missing_icon_returns_null()
    {
        var directory = CreateDirectoryWithIcon("tag", "ElTag.ico");
        try
        {
            Assert.Null(ExplorerIcons.Resolve(directory, "find"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Resolve_without_directory_returns_null(string? directory)
    {
        Assert.Null(ExplorerIcons.Resolve(directory, "tag"));
    }

    private static string CreateDirectoryWithIcon(string mode, string fileName)
    {
        var directory = Path.Combine(Path.GetTempPath(), "el-icons-" + Guid.NewGuid().ToString("N"));
        var assets = Directory.CreateDirectory(Path.Combine(directory, ExplorerIcons.AssetsFolderName)).FullName;
        Assert.NotNull(ExplorerIcons.FileNameForMode(mode));
        File.WriteAllBytes(Path.Combine(assets, fileName), [0x00]);
        return directory;
    }
}
