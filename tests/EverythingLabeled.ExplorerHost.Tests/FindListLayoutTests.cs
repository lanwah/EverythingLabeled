using EverythingLabeled.ExplorerHost;

namespace EverythingLabeled.ExplorerHost.Tests;

public sealed class FindListLayoutTests
{
    [Fact]
    public void Draft_suggest_without_chips_uses_main_area()
    {
        var (suggest, preview) = FindListLayout.Resolve(true, 3, 0, false);
        Assert.Equal(FindSuggestPlacement.Main, suggest);
        Assert.False(preview);
    }

    [Fact]
    public void Chips_with_preview_hides_suggest_when_no_draft()
    {
        var (suggest, preview) = FindListLayout.Resolve(false, 0, 2, true);
        Assert.Equal(FindSuggestPlacement.Hidden, suggest);
        Assert.True(preview);
    }

    [Fact]
    public void Chips_and_draft_shows_compact_suggest_and_preview()
    {
        var (suggest, preview) = FindListLayout.Resolve(true, 2, 1, true);
        Assert.Equal(FindSuggestPlacement.Compact, suggest);
        Assert.True(preview);
    }

    [Fact]
    public void Idle_hides_both_lists()
    {
        var (suggest, preview) = FindListLayout.Resolve(false, 0, 0, false);
        Assert.Equal(FindSuggestPlacement.Hidden, suggest);
        Assert.False(preview);
    }
}
