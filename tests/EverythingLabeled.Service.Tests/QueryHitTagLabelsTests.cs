using EverythingLabeled.Service.Data.Entities;
using EverythingLabeled.Service.Services.Query;

namespace EverythingLabeled.Service.Tests;

public sealed class QueryHitTagLabelsTests
{
    [Fact]
    public void PrimaryAlias_uses_lexicographic_first_alias()
    {
        var tag = new Tag
        {
            Id = Guid.NewGuid(),
            Name = "工作",
            NameNormalized = "工作",
            NameInitials = "gz",
            Aliases =
            [
                new TagAlias { TagId = Guid.Empty, Alias = "beta", AliasNormalized = "beta" },
                new TagAlias { TagId = Guid.Empty, Alias = "alpha", AliasNormalized = "alpha" },
            ],
        };

        Assert.Equal("alpha", QueryHitTagLabels.PrimaryAlias(tag));
    }

    [Fact]
    public void PrimaryAlias_falls_back_to_name_when_no_aliases()
    {
        var tag = new Tag
        {
            Id = Guid.NewGuid(),
            Name = "标签",
            NameNormalized = "标签",
            NameInitials = "bq",
            Aliases = [],
        };

        Assert.Equal("标签", QueryHitTagLabels.PrimaryAlias(tag));
    }
}
