namespace EverythingLabeled.ExplorerHost.Tests;

public sealed class ReplaceKindGuessTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_address_has_no_kind(string? raw)
    {
        Assert.Null(ReplaceKindGuess.Kind(raw));
        Assert.Equal("输入地址后会预选类型，仍可修改。", ReplaceKindGuess.Hint(raw, null));
    }

    [Theory]
    [InlineData("https://github.com/lanwah/EverythingLabeled")]
    [InlineData("HTTP://example.com/a.txt")]
    [InlineData("  http://127.0.0.1/x  ")]
    public void Http_address_defaults_to_url(string raw)
    {
        Assert.Equal(ReplaceKindGuess.Url, ReplaceKindGuess.Kind(raw));
    }

    [Theory]
    [InlineData(@"D:\notes.md")]
    [InlineData(@"D:\dir\archive.tar.gz")]
    [InlineData(@"\\server\share\a.txt")]
    public void Path_with_extension_defaults_to_file(string raw)
    {
        Assert.Equal(ReplaceKindGuess.File, ReplaceKindGuess.Kind(raw));
    }

    [Theory]
    [InlineData(@"D:\Projects\EverythingLabeled-v2")]
    [InlineData(@"D:\Projects\EverythingLabeled-v2\")]
    [InlineData(@"C:\foo.bar\baz")]
    [InlineData(".gitignore")]
    public void Path_without_extension_defaults_to_folder(string raw)
    {
        Assert.Equal(ReplaceKindGuess.Folder, ReplaceKindGuess.Kind(raw));
    }

    [Fact]
    public void Hint_mentions_an_override()
    {
        var hint = ReplaceKindGuess.Hint(@"D:\notes.md", ReplaceKindGuess.Folder);

        Assert.Contains("已预选文件", hint);
        Assert.Contains("当前选择是文件夹", hint);
    }
}
