using System.Text.Json;
using EverythingLabeled.Service.Hosting;

namespace EverythingLabeled.Service.Tests;

public sealed class ConfigBootstrapPreserveTests
{
    [Fact]
    public void WriteAtomic_existing_file_preserves_unknown_keys()
    {
        var dir = Path.Combine(Path.GetTempPath(), "el-cfg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "config.json");

        try
        {
            const string token = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
            File.WriteAllText(
                path,
                """
                {
                  "baseUrl": "http://127.0.0.1:5123",
                  "token": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                  "findHotkey": "Ctrl+K",
                  "extra": true
                }
                """);

            var config = new AppConfig
            {
                BaseUrl = "http://127.0.0.1:5124",
                Token = token,
                ConfirmUntag = false,
                PreviewLimit = 20,
                MaxConcurrency = 8,
                Port = 5124,
            };

            ConfigBootstrap.WriteAtomic(path, config);

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            Assert.Equal("http://127.0.0.1:5124", root.GetProperty("baseUrl").GetString());
            Assert.Equal(token, root.GetProperty("token").GetString());
            Assert.False(root.GetProperty("confirmUntag").GetBoolean());
            Assert.Equal(20, root.GetProperty("previewLimit").GetInt32());
            Assert.Equal(8, root.GetProperty("maxConcurrency").GetInt32());
            Assert.Equal("Ctrl+K", root.GetProperty("findHotkey").GetString());
            Assert.True(root.GetProperty("extra").GetBoolean());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
