using EverythingLabeled.ExplorerHost;

namespace EverythingLabeled.ExplorerHost.Tests;

public sealed class UntagClickDeleteTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(100, false)]
    public void IsEnabled_only_for_single_included_path(int included, bool expected)
    {
        Assert.Equal(expected, UntagClickDelete.IsEnabled(included));
    }

    [Theory]
    [InlineData(HostMode.Tag, 1, true)]
    [InlineData(HostMode.Untag, 1, true)]
    [InlineData(HostMode.Tags, 1, false)]
    [InlineData(HostMode.Find, 1, false)]
    [InlineData(HostMode.Tag, 2, false)]
    [InlineData(HostMode.Untag, 0, false)]
    public void ShowsPanel_on_tag_and_untag_for_one_path(HostMode mode, int included, bool expected)
    {
        Assert.Equal(expected, UntagClickDelete.ShowsPanel(mode, included));
    }
}
