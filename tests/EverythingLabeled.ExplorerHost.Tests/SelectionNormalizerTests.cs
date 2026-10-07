using EverythingLabeled.ExplorerHost;
using EverythingLabeled.PathUri;

namespace EverythingLabeled.ExplorerHost.Tests;

public sealed class SelectionNormalizerTests
{
    [Fact]
    public void Illegal_path_is_ignored_unsupported()
    {
        var result = SelectionNormalizer.Normalize(
            ["not a valid path\0", "https://example.com/x"],
            HostMode.Untag);

        Assert.Equal(2, result.IgnoredUnsupportedCount);
        Assert.Empty(result.Items);
        Assert.Equal(0, result.DedupedCount);
        Assert.False(result.RejectedOverLimit);
    }

    [Fact]
    public void Duplicate_paths_dedupe_to_one()
    {
        var path = Path.GetTempFileName();
        try
        {
            Assert.True(PathUriCanonicalizer.TryCanonicalize(path, out var canonical));

            var result = SelectionNormalizer.Normalize([path, path], HostMode.Untag);

            Assert.Equal(0, result.IgnoredUnsupportedCount);
            Assert.Equal(1, result.DedupedCount);
            Assert.False(result.RejectedOverLimit);
            Assert.Single(result.Items);
            Assert.True(result.Items[0].Included);
            Assert.Equal(canonical.Uri, result.Items[0].CanonicalUri);
            Assert.Null(result.Items[0].Kind);
            Assert.Null(result.Items[0].ExcludeReason);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void One_hundred_one_unique_paths_reject_over_limit()
    {
        var root = Directory.CreateTempSubdirectory("el-sel-limit-");
        try
        {
            var paths = new List<string>(101);
            for (var i = 0; i < 101; i++)
            {
                var file = Path.Combine(root.FullName, $"f{i}.txt");
                File.WriteAllText(file, "x");
                paths.Add(file);
            }

            var result = SelectionNormalizer.Normalize(paths, HostMode.Untag);

            Assert.Equal(101, result.DedupedCount);
            Assert.True(result.RejectedOverLimit);
            Assert.Equal(101, result.Items.Count);
            Assert.All(result.Items, item => Assert.True(item.Included));
        }
        finally
        {
            root.Delete(true);
        }
    }

    [Fact]
    public void Tag_mode_missing_file_is_excluded_with_reason()
    {
        var missing = Path.Combine(
            Path.GetTempPath(),
            "el-sel-missing-" + Guid.NewGuid().ToString("N"),
            "nope.txt");

        Assert.True(PathUriCanonicalizer.TryCanonicalize(missing, out var canonical));

        var result = SelectionNormalizer.Normalize([missing], HostMode.Tag);

        Assert.Equal(0, result.IgnoredUnsupportedCount);
        Assert.Equal(1, result.DedupedCount);
        Assert.False(result.RejectedOverLimit);
        Assert.Single(result.Items);
        var item = result.Items[0];
        Assert.False(item.Included);
        Assert.Equal(canonical.Uri, item.CanonicalUri);
        Assert.Null(item.Kind);
        Assert.Equal(Copy.PathNotFoundOrBroken, item.ExcludeReason);
    }

    [Fact]
    public void Untag_mode_missing_file_still_included()
    {
        var missing = Path.Combine(
            Path.GetTempPath(),
            "el-sel-missing-" + Guid.NewGuid().ToString("N"),
            "nope.txt");

        Assert.True(PathUriCanonicalizer.TryCanonicalize(missing, out var canonical));

        var result = SelectionNormalizer.Normalize([missing], HostMode.Untag);

        Assert.Equal(0, result.IgnoredUnsupportedCount);
        Assert.Equal(1, result.DedupedCount);
        Assert.False(result.RejectedOverLimit);
        Assert.Single(result.Items);
        var item = result.Items[0];
        Assert.True(item.Included);
        Assert.Equal(canonical.Uri, item.CanonicalUri);
        Assert.Null(item.Kind);
        Assert.Null(item.ExcludeReason);
    }

    [Fact]
    public void Tag_mode_existing_file_includes_kind()
    {
        var path = Path.GetTempFileName();
        try
        {
            Assert.True(PathUriCanonicalizer.TryCanonicalize(path, out var canonical));

            var result = SelectionNormalizer.Normalize([path], HostMode.Tag);

            Assert.Single(result.Items);
            var item = result.Items[0];
            Assert.True(item.Included);
            Assert.Equal(canonical.Uri, item.CanonicalUri);
            Assert.Equal("file", item.Kind);
            Assert.Null(item.ExcludeReason);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
