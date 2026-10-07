using System.Text.Json;
using EverythingLabeled.Tray;

namespace EverythingLabeled.Tray.Tests;

public sealed class TrayConfigStoreTests
{
    [Fact]
    public void Load_missing_file_returns_defaults()
    {
        var path = Path.Combine(Path.GetTempPath(), "el-tray-missing-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var cfg = TrayConfigStore.Load(path);
            Assert.Equal(TrayConfigStore.DefaultFindHotkey, cfg.FindHotkey);
            Assert.Equal(TrayConfigStore.DefaultRunAtStartup, cfg.RunAtStartup);
        }
        finally
        {
            // nothing created
        }
    }

    [Fact]
    public void Load_bad_json_returns_defaults()
    {
        var path = Path.Combine(Path.GetTempPath(), "el-tray-bad-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, "{ not json");
        try
        {
            var cfg = TrayConfigStore.Load(path);
            Assert.Equal(TrayConfigStore.DefaultFindHotkey, cfg.FindHotkey);
            Assert.True(cfg.RunAtStartup);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_reads_known_keys_as_is()
    {
        var path = Path.Combine(Path.GetTempPath(), "el-tray-load-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, """
            {
              "findHotkey": "Ctrl+K",
              "runAtStartup": false,
              "token": "abc",
              "extra": 1
            }
            """);
        try
        {
            var cfg = TrayConfigStore.Load(path);
            Assert.Equal("Ctrl+K", cfg.FindHotkey);
            Assert.False(cfg.RunAtStartup);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TrySetRunAtStartup_preserves_unknown_keys()
    {
        var path = Path.Combine(Path.GetTempPath(), "el-tray-rmw-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, """
            {
              "findHotkey": "Ctrl+K",
              "runAtStartup": false,
              "token": "abc",
              "extra": 1
            }
            """);
        try
        {
            var ok = TrayConfigStore.TrySetRunAtStartup(path, true);
            Assert.True(ok);

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            Assert.True(root.GetProperty("runAtStartup").GetBoolean());
            Assert.Equal("abc", root.GetProperty("token").GetString());
            Assert.Equal(1, root.GetProperty("extra").GetInt32());
            Assert.Equal("Ctrl+K", root.GetProperty("findHotkey").GetString());

            var reloaded = TrayConfigStore.Load(path);
            Assert.True(reloaded.RunAtStartup);
            Assert.Equal("Ctrl+K", reloaded.FindHotkey);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
            if (File.Exists(path + ".tmp"))
                File.Delete(path + ".tmp");
        }
    }

    [Fact]
    public void DefaultConfigPath_uses_appdata_everything_labeled()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "EverythingLabeled",
            "config.json");

        // Clear override for this assertion when possible: DefaultConfigPath reads live env.
        // When EL_APPDATA is unset, path must match AppPaths-style default.
        var previous = Environment.GetEnvironmentVariable("EL_APPDATA");
        try
        {
            Environment.SetEnvironmentVariable("EL_APPDATA", null);
            Assert.Equal(expected, TrayConfigStore.DefaultConfigPath());
        }
        finally
        {
            Environment.SetEnvironmentVariable("EL_APPDATA", previous);
        }
    }

    [Fact]
    public void DefaultConfigPath_respects_el_appdata()
    {
        var root = Path.Combine(Path.GetTempPath(), "el-appdata-" + Guid.NewGuid().ToString("N"));
        var previous = Environment.GetEnvironmentVariable("EL_APPDATA");
        try
        {
            Environment.SetEnvironmentVariable("EL_APPDATA", root);
            Assert.Equal(Path.Combine(Path.GetFullPath(root), "config.json"), TrayConfigStore.DefaultConfigPath());
        }
        finally
        {
            Environment.SetEnvironmentVariable("EL_APPDATA", previous);
        }
    }

    [Fact]
    public void Load_missing_keys_use_defaults()
    {
        var path = Path.Combine(Path.GetTempPath(), "el-tray-partial-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, """{ "token": "x" }""");
        try
        {
            var cfg = TrayConfigStore.Load(path);
            Assert.Equal(TrayConfigStore.DefaultFindHotkey, cfg.FindHotkey);
            Assert.Equal(TrayConfigStore.DefaultRunAtStartup, cfg.RunAtStartup);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
