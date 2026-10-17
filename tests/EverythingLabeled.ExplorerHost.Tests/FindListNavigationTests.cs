using EverythingLabeled.ExplorerHost;

namespace EverythingLabeled.ExplorerHost.Tests;

public sealed class FindListNavigationTests
{
    [Fact]
    public void NextDownIndex_empty_returns_minus_one()
    {
        Assert.Equal(-1, FindListNavigation.NextDownIndex(selectedIndex: -1, count: 0));
        Assert.Equal(-1, FindListNavigation.NextDownIndex(selectedIndex: 0, count: 0));
    }

    [Fact]
    public void NextDownIndex_none_selected_selects_first()
    {
        Assert.Equal(0, FindListNavigation.NextDownIndex(selectedIndex: -1, count: 2));
    }

    [Fact]
    public void NextDownIndex_already_on_first_advances_to_second()
    {
        Assert.Equal(1, FindListNavigation.NextDownIndex(selectedIndex: 0, count: 2));
    }

    [Fact]
    public void NextDownIndex_on_last_stays()
    {
        Assert.Equal(1, FindListNavigation.NextDownIndex(selectedIndex: 1, count: 2));
    }

    [Fact]
    public void NextUpIndex_empty_returns_minus_one()
    {
        Assert.Equal(-1, FindListNavigation.NextUpIndex(selectedIndex: -1, count: 0));
    }

    [Fact]
    public void NextUpIndex_none_selected_selects_last()
    {
        Assert.Equal(1, FindListNavigation.NextUpIndex(selectedIndex: -1, count: 2));
    }

    [Fact]
    public void NextUpIndex_on_first_stays()
    {
        Assert.Equal(0, FindListNavigation.NextUpIndex(selectedIndex: 0, count: 2));
    }
}
