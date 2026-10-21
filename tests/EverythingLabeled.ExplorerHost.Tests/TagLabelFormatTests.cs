using EverythingLabeled.Client;
using EverythingLabeled.ExplorerHost;

namespace EverythingLabeled.ExplorerHost.Tests;

public sealed class TagLabelFormatTests
{
    [Fact]
    public void Format_same_alias_is_name_or_ns_slash_name()
    {
        Assert.Equal("工作", TagLabelFormat.Format("工作", null, "工作"));
        Assert.Equal("proj/工作", TagLabelFormat.Format("工作", "proj", "工作"));
    }

    [Fact]
    public void Format_other_alias_appends_head()
    {
        Assert.Equal("gz  (工作)", TagLabelFormat.Format("gz", null, "工作"));
    }

    [Fact]
    public void FromSuggest_uses_alias_display()
    {
        var item = new SuggestItem(Guid.Empty, "gz", "工作", null, "gz");
        var row = TagLabelFormat.FromSuggest(item);
        Assert.Equal("gz", row.Alias);
        Assert.Equal("gz  (工作)", row.Display);
        Assert.Equal("gz", row.NameInitials);
    }
}
