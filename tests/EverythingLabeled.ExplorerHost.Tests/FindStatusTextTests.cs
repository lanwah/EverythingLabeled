namespace EverythingLabeled.ExplorerHost.Tests;

public sealed class FindStatusTextTests
{
    [Fact]
    public void IdleOrSuggest_no_draft_is_preview_zero()
    {
        Assert.Equal(Copy.PreviewCount(0), FindStatusText.IdleOrSuggest(null, 3));
        Assert.Equal(Copy.PreviewCount(0), FindStatusText.IdleOrSuggest(null, 0));
    }

    [Fact]
    public void IdleOrSuggest_draft_uses_suggest_count()
    {
        Assert.Equal(Copy.SuggestCount(3), FindStatusText.IdleOrSuggest("g", 3));
        Assert.Equal(Copy.SuggestCount(0), FindStatusText.IdleOrSuggest("g", 0));
    }
}
