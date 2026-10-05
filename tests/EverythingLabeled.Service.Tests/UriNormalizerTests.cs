using EverythingLabeled.Service.Services.Abstractions;
using EverythingLabeled.Service.Services.Resources;

namespace EverythingLabeled.Service.Tests;

public sealed class UriNormalizerTests
{
    [Fact]
    public void File_drive_upper_slash_no_trailing()
    {
        var r = UriNormalizer.Normalize(@"C:\Users\example\doc.txt", "file");
        Assert.True(r.IsSuccess);
        Assert.Equal("file:///C:/Users/example/doc.txt", r.Data.Uri);
        Assert.Equal("file", r.Data.Kind);
    }

    [Fact]
    public void Https_lower_host_strip_default_port_and_fragment()
    {
        var r = UriNormalizer.Normalize("HTTPS://Example.COM:443/a?x=1#frag", null);
        Assert.True(r.IsSuccess);
        Assert.Equal("https://example.com/a?x=1", r.Data.Uri);
        Assert.Equal("url", r.Data.Kind);
    }

    [Fact]
    public void Https_with_file_kind_is_mismatch()
    {
        var r = UriNormalizer.Normalize("https://example.com/a", "file");
        Assert.False(r.IsSuccess);
        Assert.Equal(ErrorCodes.ValidationFailed, r.Code);
    }

    [Fact]
    public void Mismatch_details_reason_is_kind_uri_mismatch()
    {
        var r = UriNormalizer.Normalize("https://example.com/a", "folder");
        Assert.False(r.IsSuccess);
        var reason = r.Details?.GetType().GetProperty("reason")?.GetValue(r.Details);
        Assert.Equal("kind_uri_mismatch", reason);
    }
}
