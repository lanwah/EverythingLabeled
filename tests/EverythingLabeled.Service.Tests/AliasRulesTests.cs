using EverythingLabeled.Service.Services.Tags;

namespace EverythingLabeled.Service.Tests;

public sealed class AliasRulesTests
{
    [Theory]
    [InlineData("工作")]
    [InlineData("proj:foo")]
    [InlineData("Work")]
    public void Accepts_legal_aliases(string alias) =>
        Assert.True(AliasRules.TryValidate(alias, out _));

    [Theory]
    [InlineData("", "alias_rejected_empty")]
    [InlineData("   ", "alias_rejected_empty")]
    [InlineData("-archived", "alias_rejected_leading_dash")]
    [InlineData("a b", "alias_rejected_whitespace")]
    [InlineData("or", "alias_rejected_reserved")]
    [InlineData("AND", "alias_rejected_reserved")]
    [InlineData("Not", "alias_rejected_reserved")]
    public void Rejects_illegal_aliases(string alias, string reason)
    {
        Assert.False(AliasRules.TryValidate(alias, out var actual));
        Assert.Equal(reason, actual);
    }

    [Fact]
    public void Rejects_control_characters()
    {
        Assert.False(AliasRules.TryValidate("a\u0001b", out var actual));
        Assert.Equal("alias_rejected_control", actual);
    }
}
