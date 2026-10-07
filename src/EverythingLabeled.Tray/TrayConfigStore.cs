using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EverythingLabeled.Tray;

public sealed record TrayConfig(string FindHotkey, bool RunAtStartup);

public static class TrayConfigStore
{
    public const string DefaultFindHotkey = "Alt+L";
    public const bool DefaultRunAtStartup = true;

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
    };

    public static string DefaultConfigPath()
    {
        var overrideRoot = Environment.GetEnvironmentVariable("EL_APPDATA");
        var root = string.IsNullOrWhiteSpace(overrideRoot)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "EverythingLabeled")
            : Path.GetFullPath(overrideRoot);

        return Path.Combine(root, "config.json");
    }

    public static TrayConfig Load(string configPath)
    {
        if (!File.Exists(configPath))
            return Defaults();

        try
        {
            var text = File.ReadAllText(configPath);
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return Defaults();

            var root = doc.RootElement;
            var findHotkey = DefaultFindHotkey;
            var runAtStartup = DefaultRunAtStartup;

            if (root.TryGetProperty("findHotkey", out var hotkeyEl)
                && hotkeyEl.ValueKind == JsonValueKind.String)
            {
                findHotkey = hotkeyEl.GetString() ?? DefaultFindHotkey;
            }

            if (root.TryGetProperty("runAtStartup", out var runEl)
                && runEl.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                runAtStartup = runEl.GetBoolean();
            }

            return new TrayConfig(findHotkey, runAtStartup);
        }
        catch (JsonException)
        {
            return Defaults();
        }
        catch (IOException)
        {
            return Defaults();
        }
    }

    public static bool TrySetRunAtStartup(string configPath, bool value, int retries = 2)
    {
        var attempts = Math.Max(1, retries + 1);
        for (var i = 0; i < attempts; i++)
        {
            try
            {
                WriteRunAtStartup(configPath, value);
                return true;
            }
            catch (IOException)
            {
                if (i == attempts - 1)
                    return false;
            }
            catch (UnauthorizedAccessException)
            {
                if (i == attempts - 1)
                    return false;
            }
            catch (JsonException)
            {
                if (i == attempts - 1)
                    return false;
            }
        }

        return false;
    }

    private static void WriteRunAtStartup(string configPath, bool value)
    {
        var directory = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        JsonObject root;
        if (File.Exists(configPath))
        {
            var text = File.ReadAllText(configPath);
            var node = JsonNode.Parse(text);
            root = node as JsonObject ?? new JsonObject();
        }
        else
        {
            root = new JsonObject();
        }

        root["runAtStartup"] = value;

        var json = root.ToJsonString(WriteOptions);
        var tempPath = configPath + ".tmp";
        File.WriteAllText(tempPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(tempPath, configPath, overwrite: true);
    }

    private static TrayConfig Defaults() => new(DefaultFindHotkey, DefaultRunAtStartup);
}
