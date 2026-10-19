using EverythingLabeled.ExplorerHost;

namespace EverythingLabeled.ExplorerHost.Tests;

public sealed class HostArgsParserTests
{
    [Fact]
    public void TryParse_tag_with_paths_file()
    {
        var ok = HostArgsParser.TryParse(
            ["--mode", "tag", "--paths-file", @"C:\a.txt"],
            out var args,
            out var error);

        Assert.True(ok);
        Assert.Equal(string.Empty, error);
        Assert.Equal(HostMode.Tag, args.Mode);
        Assert.Equal(@"C:\a.txt", args.PathsFilePath);
    }

    [Fact]
    public void TryParse_find_without_paths_file()
    {
        var ok = HostArgsParser.TryParse(["--mode", "find"], out var args, out var error);

        Assert.True(ok);
        Assert.Equal(string.Empty, error);
        Assert.Equal(HostMode.Find, args.Mode);
        Assert.Null(args.PathsFilePath);
    }

    [Fact]
    public void TryParse_illegal_mode_fails()
    {
        var ok = HostArgsParser.TryParse(["--mode", "nope"], out _, out var error);

        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void TryParse_library_without_paths_file()
    {
        var ok = HostArgsParser.TryParse(["--mode", "library"], out var args, out var error);

        Assert.True(ok);
        Assert.Equal(string.Empty, error);
        Assert.Equal(HostMode.Library, args.Mode);
        Assert.Null(args.PathsFilePath);
    }

    [Theory]
    [InlineData("untag", HostMode.Untag)]
    [InlineData("tags", HostMode.Tags)]
    [InlineData("library", HostMode.Library)]
    [InlineData("TAG", HostMode.Tag)]
    public void TryParse_mode_case_insensitive(string mode, HostMode expected)
    {
        var ok = HostArgsParser.TryParse(["--mode", mode], out var args, out _);

        Assert.True(ok);
        Assert.Equal(expected, args.Mode);
    }
}
