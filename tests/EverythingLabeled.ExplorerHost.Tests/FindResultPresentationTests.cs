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
    [InlineData("folder", FindResultPresentation.IconKeyFolder)]
    [InlineData("file", FindResultPresentation.IconKeyFile)]
    [InlineData("url", FindResultPresentation.IconKeyUrl)]
    [InlineData("other", FindResultPresentation.IconKeyFile)]
    public void IconKey_maps_kind(string kind, string expected)
    {
        Assert.Equal(expected, FindResultPresentation.IconKey(kind));
    }

    [Fact]
    public void TagLabels_returns_empty_when_null_or_empty()
    {
        Assert.Empty(FindResultPresentation.TagLabels(null));
        Assert.Empty(FindResultPresentation.TagLabels(Array.Empty<string>()));
    }

    [Fact]
    public void TagLabels_preserves_order()
    {
        var labels = FindResultPresentation.TagLabels(["标签", "项目"]);
        Assert.Equal(["标签", "项目"], labels);
    }
}
