using System.Net;
using System.Text.Json;
using EverythingLabeled.Service.Hosting;
using Microsoft.Data.Sqlite;

namespace EverythingLabeled.Service.Tests;

public sealed class ServiceSkeletonTests
{
    [Fact]
    public async Task Bootstrap_WithoutConfig_CreatesValidConfigAndStarts()
    {
        await using var fx = ServiceProcessFixture.CreateIsolated();
        Assert.False(File.Exists(fx.ConfigPath));

        await fx.StartAsync();

        Assert.True(File.Exists(fx.ConfigPath));
        var config = ConfigBootstrap.LoadExisting(fx.ConfigPath);
        Assert.True(ConfigBootstrap.IsValidToken(config.Token));
        Assert.True(ConfigBootstrap.TryValidateBaseUrl(config.BaseUrl, out var port, out _));
        Assert.InRange(port, ConfigBootstrap.PortRangeStart, ConfigBootstrap.PortRangeEnd);
        Assert.True(Directory.Exists(fx.LogsDirectory));
    }

    [Fact]
    public async Task InvalidConfig_FailsWithoutOverwrite()
    {
        await using var fx = ServiceProcessFixture.CreateIsolated();
        Directory.CreateDirectory(fx.AppDataRoot);
        const string invalid = """{"baseUrl":"https://example.com","token":"not-a-valid-token"}""";
        await File.WriteAllTextAsync(fx.ConfigPath, invalid);

        var exitCode = await fx.RunExpectExitAsync();

        Assert.NotEqual(0, exitCode);
        var after = await File.ReadAllTextAsync(fx.ConfigPath);
        Assert.Equal(invalid, after);
    }

    [Fact]
    public async Task Health_IsAnonymous_AndReturnsNumberDataVersion()
    {
        await using var fx = ServiceProcessFixture.CreateIsolated();
        fx.WriteConfigForFreePort();
        await fx.StartAsync();

        using var response = await fx.GetAsync("/v1/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await fx.ReadJsonAsync(response);
        Assert.Equal("ok", json.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("message").ValueKind);
        var data = json.GetProperty("data");
        Assert.Equal("ok", data.GetProperty("health").GetString());
        Assert.Equal(JsonValueKind.Number, data.GetProperty("dataVersion").ValueKind);
        Assert.Equal(0, data.GetProperty("dataVersion").GetInt32());
    }

    [Fact]
    public async Task ProtectedRoute_WithoutOrWrongBearer_ReturnsUnauthorized()
    {
        await using var fx = ServiceProcessFixture.CreateIsolated();
        fx.WriteConfigForFreePort();
        await fx.StartAsync();

        using var missing = await fx.GetAsync("/v1/meta");
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        var missingJson = await fx.ReadJsonAsync(missing);
        Assert.Equal("error", missingJson.GetProperty("status").GetString());
        Assert.Equal("unauthorized", missingJson.GetProperty("data").GetProperty("code").GetString());

        using var wrong = await fx.GetAsync("/v1/meta", bearerToken: new string('a', 64));
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        var wrongJson = await fx.ReadJsonAsync(wrong);
        Assert.Equal("unauthorized", wrongJson.GetProperty("data").GetProperty("code").GetString());
    }

    [Fact]
    public async Task UnknownV1Route_WithValidBearer_ReturnsEnvelope404()
    {
        await using var fx = ServiceProcessFixture.CreateIsolated();
        fx.WriteConfigForFreePort();
        await fx.StartAsync();

        using var response = await fx.GetAsync("/v1/does-not-exist", bearerToken: fx.Config!.Token);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var json = await fx.ReadJsonAsync(response);
        Assert.Equal("error", json.GetProperty("status").GetString());
        Assert.Equal("not_found", json.GetProperty("data").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Migration_CreatesDbWithDataVersion()
    {
        await using var fx = ServiceProcessFixture.CreateIsolated();
        fx.WriteConfigForFreePort();
        await fx.StartAsync();

        Assert.True(File.Exists(fx.DatabasePath));

        await using var connection = new SqliteConnection($"Data Source={fx.DatabasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM Meta WHERE Key = 'data_version';";
        var value = (string?)await command.ExecuteScalarAsync();
        Assert.Equal("0", value);

        command.CommandText = "PRAGMA journal_mode;";
        var mode = (string?)await command.ExecuteScalarAsync();
        Assert.Equal("wal", mode, ignoreCase: true);
    }

    [Fact]
    public async Task SecondInstance_SameMutex_ExitsZero()
    {
        await using var fx = ServiceProcessFixture.CreateIsolated();
        fx.WriteConfigForFreePort();
        await fx.StartAsync();

        var exitCode = await fx.RunExpectExitAsync();
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task PortInUse_ExitsNonZero_AndDoesNotChangeConfig()
    {
        await using var fx = ServiceProcessFixture.CreateIsolated();
        Directory.CreateDirectory(fx.AppDataRoot);
        Directory.CreateDirectory(Path.Combine(fx.AppDataRoot, "data"));
        Directory.CreateDirectory(Path.Combine(fx.AppDataRoot, "logs"));

        // Use a high ephemeral port unlikely to collide with the 5123-5132 bootstrap range for other tests.
        const int port = 58991;
        var token = ConfigBootstrap.GenerateToken();
        var config = new AppConfig
        {
            BaseUrl = $"http://127.0.0.1:{port}",
            Token = token,
            Port = port,
        };
        ConfigBootstrap.WriteAtomic(fx.ConfigPath, config);
        var before = await File.ReadAllTextAsync(fx.ConfigPath);

        using (ServiceProcessFixture.HoldPort(port))
        {
            var exitCode = await fx.RunExpectExitAsync();
            Assert.NotEqual(0, exitCode);
        }

        var after = await File.ReadAllTextAsync(fx.ConfigPath);
        Assert.Equal(before, after);
    }
}
