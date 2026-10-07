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
}
