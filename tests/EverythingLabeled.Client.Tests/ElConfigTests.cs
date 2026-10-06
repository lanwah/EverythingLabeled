using EverythingLabeled.Client;

namespace EverythingLabeled.Client.Tests;

public sealed class ElConfigTests
{
    [Fact]
    public void Full_json_loads_fields_and_trailing_slash()
    {
        using var dir = new TempDir();
        var path = dir.Write("config.json", """
            {"baseUrl":"http://127.0.0.1:5123","token":"secret","confirmUntag":true}
            """);

        var r = ElConfigLoader.Load(EmptyFlags(), EmptyEnv(), path);

        Assert.True(r.IsSuccess);
        Assert.Equal("http://127.0.0.1:5123/", r.Data!.BaseUrl);
        Assert.Equal("secret", r.Data.Token);
        Assert.True(r.Data.ConfirmUntag);
    }

    [Fact]
    public void Missing_file_without_overrides_is_config_missing()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "config.json");
        var r = ElConfigLoader.Load(EmptyFlags(), EmptyEnv(), missing);
        Assert.False(r.IsSuccess);
        Assert.Equal(ElErrorCodes.ConfigMissing, r.Error!.Value.Code);
    }

    [Fact]
    public void Missing_file_with_flag_url_and_token_succeeds_with_default_confirm()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "config.json");
        var r = ElConfigLoader.Load(
            new ElConfigOverrides(null, "http://127.0.0.1:5123", "tok"),
            EmptyEnv(),
            missing);

        Assert.True(r.IsSuccess);
        Assert.Equal("http://127.0.0.1:5123/", r.Data!.BaseUrl);
        Assert.Equal("tok", r.Data.Token);
        Assert.True(r.Data.ConfirmUntag);
    }

    [Fact]
    public void File_confirm_false_kept_when_flag_only_replaces_token()
    {
        using var dir = new TempDir();
        var path = dir.Write("config.json", """
            {"baseUrl":"http://127.0.0.1:5123","token":"old","confirmUntag":false}
            """);

        var r = ElConfigLoader.Load(
            new ElConfigOverrides(null, null, "new-token"),
            EmptyEnv(),
            path);

        Assert.True(r.IsSuccess);
        Assert.False(r.Data!.ConfirmUntag);
        Assert.Equal("new-token", r.Data.Token);
        Assert.Equal("http://127.0.0.1:5123/", r.Data.BaseUrl);
    }

    [Fact]
    public void Unknown_json_keys_are_ignored()
    {
        using var dir = new TempDir();
        var path = dir.Write("config.json", """
            {"baseUrl":"http://127.0.0.1:5123","token":"secret","foo":1}
            """);

        var r = ElConfigLoader.Load(EmptyFlags(), EmptyEnv(), path);
        Assert.True(r.IsSuccess);
        Assert.True(r.Data!.ConfirmUntag);
    }

    [Fact]
    public void Env_base_url_overrides_file_when_flag_empty()
    {
        using var dir = new TempDir();
        var path = dir.Write("config.json", """
            {"baseUrl":"http://127.0.0.1:5123","token":"secret"}
            """);

        var r = ElConfigLoader.Load(
            EmptyFlags(),
            new Dictionary<string, string?> { ["EL_BASE_URL"] = "http://127.0.0.1:9999" },
            path);

        Assert.True(r.IsSuccess);
        Assert.Equal("http://127.0.0.1:9999/", r.Data!.BaseUrl);
    }

    [Fact]
    public void Config_flag_path_wins_over_el_config_env()
    {
        using var dir = new TempDir();
        var flagged = dir.Write("flag.json", """
            {"baseUrl":"http://127.0.0.1:1","token":"a"}
            """);
        var envPath = dir.Write("env.json", """
            {"baseUrl":"http://127.0.0.1:2","token":"b"}
            """);

        var r = ElConfigLoader.Load(
            new ElConfigOverrides(flagged, null, null),
            new Dictionary<string, string?> { ["EL_CONFIG"] = envPath },
            dir.Write("default.json", """{"baseUrl":"http://127.0.0.1:3","token":"c"}"""));

        Assert.True(r.IsSuccess);
        Assert.Equal("http://127.0.0.1:1/", r.Data!.BaseUrl);
        Assert.Equal("a", r.Data.Token);
    }

    [Fact]
    public void Relative_host_without_scheme_is_config_missing()
    {
        using var dir = new TempDir();
        var path = dir.Write("config.json", """
            {"baseUrl":"localhost:5123","token":"secret"}
            """);

        var r = ElConfigLoader.Load(EmptyFlags(), EmptyEnv(), path);
        Assert.False(r.IsSuccess);
        Assert.Equal(ElErrorCodes.ConfigMissing, r.Error!.Value.Code);
    }

    [Fact]
    public void LoadBaseUrl_file_without_token_succeeds_with_trailing_slash()
    {
        using var dir = new TempDir();
        var path = dir.Write("config.json", """
            {"baseUrl":"http://127.0.0.1:5123"}
            """);

        var load = ElConfigLoader.Load(EmptyFlags(), EmptyEnv(), path);
        Assert.False(load.IsSuccess);
        Assert.Equal(ElErrorCodes.ConfigMissing, load.Error!.Value.Code);

        var r = ElConfigLoader.LoadBaseUrl(EmptyFlags(), EmptyEnv(), path);
        Assert.True(r.IsSuccess);
        Assert.Equal("http://127.0.0.1:5123/", r.Data!.BaseUrl);
        Assert.True(r.Data.ConfirmUntag);
    }

    [Fact]
    public void LoadBaseUrl_file_confirm_false_is_kept()
    {
        using var dir = new TempDir();
        var path = dir.Write("config.json", """
            {"baseUrl":"http://127.0.0.1:5123","confirmUntag":false}
            """);

        var r = ElConfigLoader.LoadBaseUrl(EmptyFlags(), EmptyEnv(), path);
        Assert.True(r.IsSuccess);
        Assert.False(r.Data!.ConfirmUntag);
    }

    [Fact]
    public void LoadBaseUrl_missing_file_without_overrides_is_config_missing()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "config.json");
        var r = ElConfigLoader.LoadBaseUrl(EmptyFlags(), EmptyEnv(), missing);
        Assert.False(r.IsSuccess);
        Assert.Equal(ElErrorCodes.ConfigMissing, r.Error!.Value.Code);
        Assert.Equal("Configuration is missing or invalid.", r.Error.Value.Message);
    }

    [Fact]
    public void LoadBaseUrl_missing_file_with_flag_url_succeeds_with_default_confirm()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "config.json");
        var r = ElConfigLoader.LoadBaseUrl(
            new ElConfigOverrides(null, "http://127.0.0.1:5123", null),
            EmptyEnv(),
            missing);

        Assert.True(r.IsSuccess);
        Assert.Equal("http://127.0.0.1:5123/", r.Data!.BaseUrl);
        Assert.True(r.Data.ConfirmUntag);
    }

    [Fact]
    public void LoadBaseUrl_relative_host_without_scheme_is_config_missing()
    {
        using var dir = new TempDir();
        var path = dir.Write("config.json", """
            {"baseUrl":"localhost:5123"}
            """);

        var r = ElConfigLoader.LoadBaseUrl(EmptyFlags(), EmptyEnv(), path);
        Assert.False(r.IsSuccess);
        Assert.Equal(ElErrorCodes.ConfigMissing, r.Error!.Value.Code);
    }

    [Fact]
    public void Base_url_with_and_without_slash_normalize_equal()
    {
        using var dir = new TempDir();
        var a = dir.Write("a.json", """{"baseUrl":"http://127.0.0.1:5123","token":"t"}""");
        var b = dir.Write("b.json", """{"baseUrl":"http://127.0.0.1:5123/","token":"t"}""");

        var ra = ElConfigLoader.Load(EmptyFlags(), EmptyEnv(), a);
        var rb = ElConfigLoader.Load(EmptyFlags(), EmptyEnv(), b);

        Assert.True(ra.IsSuccess);
        Assert.True(rb.IsSuccess);
        Assert.Equal(ra.Data!.BaseUrl, rb.Data!.BaseUrl);
        Assert.Equal("http://127.0.0.1:5123/", ra.Data.BaseUrl);
    }

    private static ElConfigOverrides EmptyFlags() => new(null, null, null);

    private static Dictionary<string, string?> EmptyEnv() => new();

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "el-cfg-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Path);

        public string Write(string name, string contents)
        {
            var file = System.IO.Path.Combine(Path, name);
            File.WriteAllText(file, contents);
            return file;
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }
}
