using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace EverythingLabeled.Service.Tests;

public sealed class HttpCrudIntegrationTests
{
    [Fact]
    public async Task Tags_suggest_and_meta_roundtrip_requires_bearer_for_meta()
    {
        await using var fx = ServiceProcessFixture.CreateIsolated();
        fx.WriteConfigForFreePort();
        await fx.StartAsync();

        using var anonymousMeta = await fx.GetAsync("/v1/meta");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousMeta.StatusCode);

        using var client = fx.CreateClient(fx.Config!.Token);
        using var createdResponse = await client.PostAsJsonAsync(
            "/v1/tags",
            new { name = "工作", @namespace = (string?)null, seq = (int?)null, aliases = (string[]?)null });
        Assert.Equal(HttpStatusCode.OK, createdResponse.StatusCode);
        var createdJson = await ReadJsonAsync(createdResponse);
        Assert.Equal("ok", createdJson.GetProperty("status").GetString());
        var created = createdJson.GetProperty("data");
        var id = created.GetProperty("id").GetGuid();
        var dataVersion = created.GetProperty("dataVersion").GetInt32();
        Assert.Equal(1, dataVersion);

        using var getResponse = await client.GetAsync($"/v1/tags/{id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var getJson = await ReadJsonAsync(getResponse);
        Assert.Equal("工作", getJson.GetProperty("data").GetProperty("name").GetString());
        Assert.False(getJson.GetProperty("data").TryGetProperty("dataVersion", out _));

        using var suggestResponse = await client.GetAsync("/v1/tags/suggest?q=" + Uri.EscapeDataString("工"));
        Assert.Equal(HttpStatusCode.OK, suggestResponse.StatusCode);
        var suggestJson = await ReadJsonAsync(suggestResponse);
        Assert.Equal(0, suggestJson.GetProperty("data").GetProperty("offset").GetInt32());
        Assert.True(suggestJson.GetProperty("data").GetProperty("total").GetInt32() >= 1);

        using var metaResponse = await client.GetAsync("/v1/meta");
        Assert.Equal(HttpStatusCode.OK, metaResponse.StatusCode);
        var metaJson = await ReadJsonAsync(metaResponse);
        Assert.Equal(dataVersion, metaJson.GetProperty("data").GetProperty("dataVersion").GetInt32());
    }

    [Fact]
    public async Task By_alias_route_uses_single_url_decode()
    {
        // Client sends %2541; ASP.NET binds {alias} as "%41". A second UnescapeDataString
        // would turn that into "A" and miss the stored alias.
        await using var fx = ServiceProcessFixture.CreateIsolated();
        fx.WriteConfigForFreePort();
        await fx.StartAsync();

        using var client = fx.CreateClient(fx.Config!.Token);
        using var createdResponse = await client.PostAsJsonAsync(
            "/v1/tags",
            new { name = "pct", aliases = new[] { "%41" } });
        Assert.Equal(HttpStatusCode.OK, createdResponse.StatusCode);
        var createdJson = await ReadJsonAsync(createdResponse);
        Assert.Equal("ok", createdJson.GetProperty("status").GetString());
        var id = createdJson.GetProperty("data").GetProperty("id").GetGuid();

        using var byAlias = await client.GetAsync("/v1/tags/by-alias/%2541");
        Assert.Equal(HttpStatusCode.OK, byAlias.StatusCode);
        var byAliasJson = await ReadJsonAsync(byAlias);
        Assert.Equal("ok", byAliasJson.GetProperty("status").GetString());
        Assert.Equal(id, byAliasJson.GetProperty("data").GetProperty("id").GetGuid());
        Assert.Contains("%41", byAliasJson.GetProperty("data").GetProperty("aliases").EnumerateArray().Select(e => e.GetString()));
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonSerializer.DeserializeAsync<JsonElement>(stream);
    }
}
