using EverythingLabeled.Client;
using EverythingLabeled.PathUri;

namespace EverythingLabeled.Client.Tests;

public sealed class FileTargetTests
{
    [Fact]
    public void Temp_file_kind_is_file()
    {
        var path = Path.GetTempFileName();
        try
        {
            Assert.True(PathUriCanonicalizer.TryCanonicalize(path, out var canonical));
            var r = FileTarget.ResolveKindForTag(canonical.Uri);
            Assert.True(r.IsSuccess);
            Assert.Equal("file", r.Data);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Temp_directory_kind_is_folder()
    {
        var dir = Directory.CreateTempSubdirectory("el-ft-");
        try
        {
            Assert.True(PathUriCanonicalizer.TryCanonicalize(dir.FullName, out var canonical));
            var r = FileTarget.ResolveKindForTag(canonical.Uri);
            Assert.True(r.IsSuccess);
            Assert.Equal("folder", r.Data);
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Fact]
    public void Missing_path_is_path_not_found()
    {
        var missing = Path.Combine(Path.GetTempPath(), "el-missing-" + Guid.NewGuid().ToString("N"), "nope.txt");
        Assert.True(PathUriCanonicalizer.TryCanonicalize(missing, out var canonical));
        var r = FileTarget.ResolveKindForTag(canonical.Uri);
        Assert.False(r.IsSuccess);
        Assert.Equal(ElErrorCodes.PathNotFound, r.Error!.Value.Code);
    }

    [Fact]
    public void Symlink_to_directory_is_folder_and_keeps_link_uri()
    {
        var root = Directory.CreateTempSubdirectory("el-link-");
        try
        {
            var target = Directory.CreateDirectory(Path.Combine(root.FullName, "target"));
            var link = Path.Combine(root.FullName, "link");
            if (!TryCreateSymbolicLink(link, target.FullName, directory: true))
                return; // no privilege for CreateSymbolicLink on this machine

            Assert.True(PathUriCanonicalizer.TryCanonicalize(link, out var canonical));
            var r = FileTarget.ResolveKindForTag(canonical.Uri);
            Assert.True(r.IsSuccess);
            Assert.Equal("folder", r.Data);
            Assert.Contains("link", canonical.Uri, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [Fact]
    public void Broken_symlink_is_path_not_found()
    {
        var root = Directory.CreateTempSubdirectory("el-broken-");
        try
        {
            var missing = Path.Combine(root.FullName, "gone");
            var link = Path.Combine(root.FullName, "broken");
            if (!TryCreateSymbolicLink(link, missing, directory: true))
                return; // no privilege for CreateSymbolicLink on this machine

            Assert.True(PathUriCanonicalizer.TryCanonicalize(link, out var canonical));
            var r = FileTarget.ResolveKindForTag(canonical.Uri);
            Assert.False(r.IsSuccess);
            Assert.Equal(ElErrorCodes.PathNotFound, r.Error!.Value.Code);
        }
        finally
        {
            root.Delete(true);
        }
    }

    private static bool TryCreateSymbolicLink(string linkPath, string targetPath, bool directory)
    {
        try
        {
            if (directory)
                Directory.CreateSymbolicLink(linkPath, targetPath);
            else
                File.CreateSymbolicLink(linkPath, targetPath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
    }
}
