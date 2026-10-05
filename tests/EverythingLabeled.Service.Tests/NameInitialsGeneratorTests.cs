using EverythingLabeled.Service.Services.Abstractions;
using EverythingLabeled.Service.Services.Tags;

namespace EverythingLabeled.Service.Tests;

public sealed class NameInitialsGeneratorTests
{
    private readonly INameInitialsGenerator _gen = new NameInitialsGenerator();

    [Theory]
    [InlineData("工作笔记", "gzbj")]
    [InlineData("API设计", "apisj")]
    [InlineData("ABC", "abc")]
    [InlineData("!!!", "")]
    public void Golden_cases(string name, string expected) =>
        Assert.Equal(expected, _gen.FromName(name));
}
