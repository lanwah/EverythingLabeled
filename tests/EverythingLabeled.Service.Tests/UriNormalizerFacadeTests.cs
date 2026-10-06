using EverythingLabeled.Service.Services.Abstractions;
using EverythingLabeled.Service.Services.Resources;

namespace EverythingLabeled.Service.Tests;

public sealed class UriNormalizerFacadeTests
{
    [Fact]
    public void File_drive_wraps_canonical_uri_and_kind()
    {
        var r = UriNormalizer.Normalize(@"C:\Users\example\doc.txt", "file");
        Assert.True(r.IsSuccess);
        Assert.Equal("file:///C:/Users/example/doc.txt", r.Data.Uri);
        Assert.Equal("file", r.Data.Kind);
    }

    [Fact]
    public void Https_infers_url_kind_when_hint_is_null()
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
        Assert.Equal("URI scheme is incompatible with kind.", r.Message);
    }

    [Fact]
    public void Folder_kind_on_https_has_kind_uri_mismatch_details()
    {
        var r = UriNormalizer.Normalize("https://example.com/a", "folder");
        Assert.False(r.IsSuccess);
        Assert.Equal(ErrorCodes.ValidationFailed, r.Code);
        var reason = r.Details?.GetType().GetProperty("reason")?.GetValue(r.Details);
        Assert.Equal("kind_uri_mismatch", reason);
    }

    [Fact]
    public void Empty_uri_is_required()
    {
        var r = UriNormalizer.Normalize("  ", null);
        Assert.False(r.IsSuccess);
        Assert.Equal(ErrorCodes.ValidationFailed, r.Code);
        Assert.Equal("URI is required.", r.Message);
    }

    [Fact]
    public void Unsupported_scheme_message_is_preserved()
    {
        var r = UriNormalizer.Normalize("ftp://example.com/a", "url");
        Assert.False(r.IsSuccess);
        Assert.Equal(ErrorCodes.ValidationFailed, r.Code);
        Assert.Equal("URI scheme is not supported.", r.Message);
    }
}
