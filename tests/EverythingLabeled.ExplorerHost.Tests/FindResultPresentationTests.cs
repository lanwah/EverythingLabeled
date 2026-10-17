using EverythingLabeled.ExplorerHost;

namespace EverythingLabeled.ExplorerHost.Tests;

public sealed class FindResultPresentationTests
{
    [Fact]
    public void Title_prefers_display_name()
    {
        var title = FindResultPresentation.Title(
            "file:///D:/Work/Git/repo",
            "React");

        Assert.Equal("React", title);
    }

    [Fact]
    public void Title_falls_back_to_file_name_from_uri()
    {
        var title = FindResultPresentation.Title(
            "file:///D:/Work/Git/NIS_SE/React",
            displayName: null);

        Assert.Equal("React", title);
    }

    [Fact]
    public void Title_falls_back_to_uri_when_not_file()
    {
        var title = FindResultPresentation.Title(
            "https://example.com/path",
            displayName: "  ");

        Assert.Equal("https://example.com/path", title);
    }

    [Fact]
    public void Subtitle_includes_kind_label_and_uri()
    {
        var subtitle = FindResultPresentation.Subtitle(
            "file:///D:/Work/a",
            "folder",
            canOpen: true);

        Assert.Equal("文件夹  ·  file:///D:/Work/a", subtitle);
    }

    [Fact]
    public void Subtitle_marks_cannot_open()
    {
        var subtitle = FindResultPresentation.Subtitle(
            "file:///D:/Work/a.txt",
            "file",
            canOpen: false);

        Assert.Contains(Copy.CannotOpen, subtitle);
        Assert.StartsWith("文件  ·  ", subtitle);
    }

    [Theory]
    [InlineData("folder", "\uF12B")]
    [InlineData("file", "\uE8A5")]
    [InlineData("url", "\uE774")]
    [InlineData("other", "\uE8A5")]
    public void Glyph_maps_kind(string kind, string expected)
    {
        Assert.Equal(expected, FindResultPresentation.Glyph(kind));
    }
}
