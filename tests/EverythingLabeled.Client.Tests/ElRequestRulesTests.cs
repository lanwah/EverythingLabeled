using EverythingLabeled.Client;

namespace EverythingLabeled.Client.Tests;

public sealed class ElRequestRulesTests
{
    [Fact]
    public void ParseAliases_empty_fails()
    {
        var r = ElRequestRules.ParseAliases([]);
        Assert.False(r.IsSuccess);
        Assert.Equal(ElErrorCodes.BadRequest, r.Error!.Value.Code);
        Assert.Equal("Provide between 1 and 100 aliases.", r.Error.Value.Message);
    }

    [Fact]
    public void ParseAliases_100_ok_101_fails()
    {
        var hundred = Enumerable.Range(0, 100).Select(i => $"a{i}").ToArray();
        var ok = ElRequestRules.ParseAliases(hundred);
        Assert.True(ok.IsSuccess);
        Assert.Equal(100, ok.Data!.Count);

        var tooMany = hundred.Append("extra").ToArray();
        var fail = ElRequestRules.ParseAliases(tooMany);
        Assert.False(fail.IsSuccess);
        Assert.Equal("Provide between 1 and 100 aliases.", fail.Error!.Value.Message);
    }

    [Fact]
    public void ParseAliases_trims_whitespace()
    {
        var r = ElRequestRules.ParseAliases(["  a  "]);
        Assert.True(r.IsSuccess);
        Assert.Equal(["a"], r.Data);
    }

    [Fact]
    public void ParseLimit_zero_int_fails_and_null_uses_default()
    {
        var zero = ElRequestRules.ParseLimit((int?)0, 50);
        Assert.False(zero.IsSuccess);
        Assert.Equal("limit must be an integer from 1 to 1024.", zero.Error!.Value.Message);

        var omitted = ElRequestRules.ParseLimit((int?)null, 50);
        Assert.True(omitted.IsSuccess);
        Assert.Equal(50, omitted.Data);
    }

    [Fact]
    public void ParseExpand_gates_without_expand_are_bad_request()
    {
        var r = ElRequestRules.ParseExpand(null, 1, null);
        Assert.False(r.IsSuccess);
        Assert.Equal(ElErrorCodes.BadRequest, r.Error!.Value.Code);
        Assert.Contains("expand", r.Error.Value.Message, StringComparison.Ordinal);
        Assert.Equal("max_depth and expanded_tag_limit require expand.", r.Error.Value.Message);
    }

    [Fact]
    public void ParseExpand_child_with_gates_succeeds()
    {
        var r = ElRequestRules.ParseExpand("child", 1, 2);
        Assert.True(r.IsSuccess);
        Assert.Equal("child", r.Data.Expand);
        Assert.Equal(1, r.Data.MaxDepth);
        Assert.Equal(2, r.Data.ExpandedTagLimit);
    }

    [Fact]
    public void ParseTarget_empty_fails()
    {
        var r = ElRequestRules.ParseTarget("");
        Assert.False(r.IsSuccess);
        Assert.Equal(ElErrorCodes.BadRequest, r.Error!.Value.Code);
        Assert.Equal("Target is not a valid path or URI.", r.Error.Value.Message);
    }

    [Fact]
    public void ResolveKindForTag_http_uri_is_url()
    {
        var target = ElRequestRules.ParseTarget("https://example.com/x");
        Assert.True(target.IsSuccess);
        var kind = ElRequestRules.ResolveKindForTag(target.Data);
        Assert.True(kind.IsSuccess);
        Assert.Equal("url", kind.Data);
    }

    [Fact]
    public void ConfirmationRequired_code_is_confirmation_required()
    {
        Assert.Equal("confirmation_required", ElErrorCodes.ConfirmationRequired);
    }
}
