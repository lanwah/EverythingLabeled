using EverythingLabeled.ExplorerHost;

namespace EverythingLabeled.ExplorerHost.Tests;

public sealed class CurrentTagFilterTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_query_matches_every_label(string? query)
    {
        Assert.False(CurrentTagFilter.IsFiltering(query));
        Assert.True(CurrentTagFilter.Matches("React", "React", "react", query));
        Assert.True(CurrentTagFilter.Matches("", "", "", query));
    }

    [Theory]
    [InlineData("标", "打标", "打标", "db", true)]
    [InlineData("标", "React", "React", "react", false)]
    [InlineData("react", "React", "React", "react", true)]
    [InlineData("0", "10", "10", "10", true)]
    [InlineData("0", "工作", "工作", "gz", false)]
    public void Match_is_case_insensitive_contains(string query, string label, string alias, string initials, bool expected)
    {
        Assert.True(CurrentTagFilter.IsFiltering(query));
        Assert.Equal(expected, CurrentTagFilter.Matches(label, alias, initials, query));
    }

    [Theory]
    [InlineData("cx", "程序", "程序", "cx", true)]
    [InlineData("c", "程序", "程序", "cx", true)]
    [InlineData("xy", "程序", "程序", "cx", false)]
    [InlineData("gz", "工作  (工作)", "工作", "gz", true)]
    public void Match_supports_name_initials_prefix(string query, string label, string alias, string initials, bool expected)
    {
        Assert.Equal(expected, CurrentTagFilter.Matches(label, alias, initials, query));
    }

    [Fact]
    public void No_tags_hides_count_and_shows_empty()
    {
        var view = CurrentTagFilter.Describe(0, 0, "标");

        Assert.Equal(Copy.UntagNoCurrentTags, view.Status);
        Assert.False(view.ShowCount);
        Assert.False(view.ShowMiss);
        Assert.True(view.ShowEmpty);
    }

    [Fact]
    public void Idle_hint_hides_count()
    {
        var view = CurrentTagFilter.Describe(18, 18, null);

        Assert.Equal(Copy.UntagChipHint, view.Status);
        Assert.False(view.ShowCount);
        Assert.False(view.ShowMiss);
        Assert.False(view.ShowEmpty);
    }

    [Fact]
    public void Filter_match_shows_count()
    {
        var view = CurrentTagFilter.Describe(18, 2, "标");

        Assert.Equal(Copy.UntagFilterMatch(2, 18), view.Status);
        Assert.Equal("2 / 18", view.CountText);
        Assert.True(view.ShowCount);
        Assert.False(view.ShowMiss);
    }

    [Fact]
    public void Filter_miss_shows_zero_count()
    {
        var view = CurrentTagFilter.Describe(17, 0, "zzz");

        Assert.Equal(Copy.UntagFilterMiss, view.Status);
        Assert.Equal("0 / 17", view.CountText);
        Assert.True(view.ShowCount);
        Assert.True(view.ShowMiss);
        Assert.False(view.ShowEmpty);
    }
}
