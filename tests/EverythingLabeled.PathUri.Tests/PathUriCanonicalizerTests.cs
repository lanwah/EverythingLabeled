using EverythingLabeled.PathUri;

namespace EverythingLabeled.PathUri.Tests;

public sealed class PathUriCanonicalizerTests
{
    [Fact]
    public void File_drive_upper_slash_no_trailing()
    {
        Assert.True(PathUriCanonicalizer.TryCanonicalize(@"C:\Users\example\doc.txt", out var result));
        Assert.Equal("file:///C:/Users/example/doc.txt", result.Uri);
        Assert.Equal("file", result.Scheme);
    }

    [Fact]
    public void Https_lower_host_strip_default_port_and_fragment()
    {
        Assert.True(PathUriCanonicalizer.TryCanonicalize("HTTPS://Example.COM:443/a?x=1#frag", out var result));
        Assert.Equal("https://example.com/a?x=1", result.Uri);
        Assert.Equal("https", result.Scheme);
    }

    [Fact]
    public void Unc_host_lower_and_share_path()
    {
        Assert.True(PathUriCanonicalizer.TryCanonicalize(@"\\server\Share\a", out var result));
        Assert.Equal("file://server/Share/a", result.Uri);
        Assert.Equal("file", result.Scheme);
    }

    [Fact]
    public void Relative_path_matches_full_path_canonicalize()
    {
        const string name = "el-pathuri-rel.txt";
        var full = Path.GetFullPath(name);
        Assert.True(PathUriCanonicalizer.TryCanonicalize(name, out var fromRel));
        Assert.True(PathUriCanonicalizer.TryCanonicalize(full, out var fromAbs));
        Assert.Equal(fromAbs.Uri, fromRel.Uri);
        Assert.Equal("file", fromRel.Scheme);
    }

    [Fact]
    public void Empty_or_whitespace_is_false()
    {
        Assert.False(PathUriCanonicalizer.TryCanonicalize("  ", out _));
        Assert.False(PathUriCanonicalizer.TryCanonicalize("", out _));
    }

    [Fact]
    public void Unsupported_scheme_is_false()
    {
        Assert.False(PathUriCanonicalizer.TryCanonicalize("ftp://example.com/a", out _));
    }

    [Fact]
    public void Https_is_not_compatible_with_file_kind()
    {
        Assert.False(PathUriCanonicalizer.IsKindCompatible("https", "file"));
    }

    [Fact]
    public void File_scheme_is_compatible_with_folder_kind()
    {
        Assert.True(PathUriCanonicalizer.IsKindCompatible("file", "folder"));
    }
}
