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

    [Fact]
    public async Task Business_routes_reject_missing_and_wrong_bearer()
    {
        await using var fx = ServiceProcessFixture.CreateIsolated();
        fx.WriteConfigForFreePort();
        await fx.StartAsync();

        using var anonymous = await fx.GetAsync("/v1/tags");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        AssertErrorCode(await ReadJsonAsync(anonymous), "unauthorized");

        using var wrong = fx.CreateClient("deadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeef");
        using var wrongResponse = await wrong.GetAsync("/v1/tags");
        Assert.Equal(HttpStatusCode.Unauthorized, wrongResponse.StatusCode);
        AssertErrorCode(await ReadJsonAsync(wrongResponse), "unauthorized");
    }

    [Fact]
    public async Task Tag_crud_create_get_patch_alias_delete_roundtrip()
    {
        await using var fx = ServiceProcessFixture.CreateIsolated();
        fx.WriteConfigForFreePort();
        await fx.StartAsync();
        using var client = fx.CreateClient(fx.Config!.Token);

        using var createdResponse = await client.PostAsJsonAsync(
            "/v1/tags",
            new { name = "alpha", aliases = new[] { "alpha" } });
        Assert.Equal(HttpStatusCode.OK, createdResponse.StatusCode);
        var created = await ReadJsonAsync(createdResponse);
        Assert.Equal("ok", created.GetProperty("status").GetString());
        var id = created.GetProperty("data").GetProperty("id").GetGuid();
        Assert.True(created.GetProperty("data").TryGetProperty("dataVersion", out var dv));
        Assert.True(dv.GetInt32() >= 1);

        using var getResponse = await client.GetAsync($"/v1/tags/{id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Equal("alpha", (await ReadJsonAsync(getResponse)).GetProperty("data").GetProperty("name").GetString());

        using var patchResponse = await client.PatchAsJsonAsync(
            $"/v1/tags/{id}",
            new { name = "alpha2", @namespace = (string?)null, seq = 3 });
        Assert.Equal(HttpStatusCode.OK, patchResponse.StatusCode);
        var patched = await ReadJsonAsync(patchResponse);
        Assert.Equal("alpha2", patched.GetProperty("data").GetProperty("name").GetString());
        Assert.Equal(3, patched.GetProperty("data").GetProperty("seq").GetInt32());
        Assert.True(patched.GetProperty("data").TryGetProperty("dataVersion", out _));

        using var aliasResponse = await client.PostAsJsonAsync(
            $"/v1/tags/{id}/aliases",
            new { alias = "alpha-alias" });
        Assert.Equal(HttpStatusCode.OK, aliasResponse.StatusCode);
        var aliased = await ReadJsonAsync(aliasResponse);
        Assert.Contains(
            "alpha-alias",
            aliased.GetProperty("data").GetProperty("aliases").EnumerateArray().Select(e => e.GetString()));
        Assert.True(aliased.GetProperty("data").TryGetProperty("dataVersion", out _));

        using var deleteResponse = await client.DeleteAsync($"/v1/tags/{id}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);
        var deleted = await ReadJsonAsync(deleteResponse);
        Assert.Equal("ok", deleted.GetProperty("status").GetString());
        Assert.True(deleted.GetProperty("data").TryGetProperty("dataVersion", out _));

        using var missing = await client.GetAsync($"/v1/tags/{id}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Resource_tagging_add_remove_idempotent_and_create_missing()
    {
        await using var fx = ServiceProcessFixture.CreateIsolated();
        fx.WriteConfigForFreePort();
        await fx.StartAsync();
        using var client = fx.CreateClient(fx.Config!.Token);

        const string uri = "file:///C:/http-tagging.txt";

        using var createMissing = await client.PostAsJsonAsync(
            "/v1/resources/tags",
            new
            {
                uri,
                kind = "file",
                add = new object[] { "brand-new-tag" },
                createMissing = true,
            });
        Assert.Equal(HttpStatusCode.OK, createMissing.StatusCode);
        var first = await ReadJsonAsync(createMissing);
        Assert.Equal("ok", first.GetProperty("status").GetString());
        Assert.True(first.GetProperty("data").TryGetProperty("dataVersion", out _));
        Assert.Single(first.GetProperty("data").GetProperty("createdTags").EnumerateArray());
        Assert.Single(first.GetProperty("data").GetProperty("added").EnumerateArray());
        var tagId = first.GetProperty("data").GetProperty("added")[0].GetGuid();

        using var addAgain = await client.PostAsJsonAsync(
            "/v1/resources/tags",
            new
            {
                uri,
                kind = "file",
                add = new object[] { "brand-new-tag" },
                createMissing = false,
            });
        Assert.Equal(HttpStatusCode.OK, addAgain.StatusCode);
        var second = await ReadJsonAsync(addAgain);
        Assert.Empty(second.GetProperty("data").GetProperty("added").EnumerateArray());

        using var remove = await client.PostAsJsonAsync(
            "/v1/resources/tags",
            new
            {
                uri,
                kind = "file",
                remove = new object[] { new { id = tagId } },
            });
        Assert.Equal(HttpStatusCode.OK, remove.StatusCode);
        var removed = await ReadJsonAsync(remove);
        Assert.Equal(tagId, removed.GetProperty("data").GetProperty("removed")[0].GetGuid());

        using var removeAgain = await client.PostAsJsonAsync(
            "/v1/resources/tags",
            new
            {
                uri,
                kind = "file",
                remove = new object[] { new { id = tagId } },
            });
        Assert.Equal(HttpStatusCode.OK, removeAgain.StatusCode);
        Assert.Empty((await ReadJsonAsync(removeAgain)).GetProperty("data").GetProperty("removed").EnumerateArray());
    }

    [Fact]
    public async Task Query_and_preview_support_and_or_negation_unknown_and_no_preview_total()
    {
        await using var fx = ServiceProcessFixture.CreateIsolated();
        fx.WriteConfigForFreePort();
        await fx.StartAsync();
        using var client = fx.CreateClient(fx.Config!.Token);

        await CreateTagAsync(client, "work");
        await CreateTagAsync(client, "home");
        await CreateTagAsync(client, "junk");

        await ApplyTagsAsync(client, "file:///C:/q-and.txt", ["work", "home"]);
        await ApplyTagsAsync(client, "file:///C:/q-or.txt", ["home"]);
        await ApplyTagsAsync(client, "file:///C:/q-neg.txt", ["work", "junk"]);

        using var andResponse = await client.GetAsync("/v1/query?q=" + Uri.EscapeDataString("work home"));
        Assert.Equal(HttpStatusCode.OK, andResponse.StatusCode);
        var andJson = await ReadJsonAsync(andResponse);
        Assert.Equal("ok", andJson.GetProperty("status").GetString());
        var andItems = andJson.GetProperty("data").GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(
            "file:///C:/q-and.txt",
            andItems.Select(e => e.GetProperty("uri").GetString()));
        Assert.All(andItems, e => Assert.False(e.TryGetProperty("tags", out _)));
        Assert.True(andJson.GetProperty("data").TryGetProperty("total", out _));

        using var orResponse = await client.GetAsync("/v1/query?q=" + Uri.EscapeDataString("work or home"));
        Assert.Equal(HttpStatusCode.OK, orResponse.StatusCode);
        var orUris = (await ReadJsonAsync(orResponse)).GetProperty("data").GetProperty("items")
            .EnumerateArray().Select(e => e.GetProperty("uri").GetString()).ToHashSet();
        Assert.Contains("file:///C:/q-and.txt", orUris);
        Assert.Contains("file:///C:/q-or.txt", orUris);
        Assert.Contains("file:///C:/q-neg.txt", orUris);

        using var negResponse = await client.GetAsync("/v1/query?q=" + Uri.EscapeDataString("work -junk"));
        Assert.Equal(HttpStatusCode.OK, negResponse.StatusCode);
        var negUris = (await ReadJsonAsync(negResponse)).GetProperty("data").GetProperty("items")
            .EnumerateArray().Select(e => e.GetProperty("uri").GetString()).ToHashSet();
        Assert.Contains("file:///C:/q-and.txt", negUris);
        Assert.DoesNotContain("file:///C:/q-neg.txt", negUris);

        using var unknown = await client.GetAsync("/v1/query?q=" + Uri.EscapeDataString("nosuchtag"));
        Assert.Equal(HttpStatusCode.OK, unknown.StatusCode);
        var unknownJson = await ReadJsonAsync(unknown);
        Assert.Equal("ok", unknownJson.GetProperty("status").GetString());
        Assert.Contains("Unknown positive term", unknownJson.GetProperty("message").GetString());
        Assert.Empty(unknownJson.GetProperty("data").GetProperty("items").EnumerateArray());

        using var preview = await client.GetAsync("/v1/query/preview?q=" + Uri.EscapeDataString("work"));
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var previewJson = await ReadJsonAsync(preview);
        Assert.Equal("ok", previewJson.GetProperty("status").GetString());
        Assert.False(previewJson.GetProperty("data").TryGetProperty("total", out _));
        var previewItems = previewJson.GetProperty("data").GetProperty("items").EnumerateArray().ToList();
        Assert.NotEmpty(previewItems);
        var andPreview = previewItems.Single(e => e.GetProperty("uri").GetString() == "file:///C:/q-and.txt");
        var previewTags = andPreview.GetProperty("tags").EnumerateArray().Select(e => e.GetString()).ToHashSet();
        Assert.Contains("work", previewTags);
        Assert.Contains("home", previewTags);
    }

    [Fact]
    public async Task Query_expand_child_hits_resource_tagged_only_with_child()
    {
        await using var fx = ServiceProcessFixture.CreateIsolated();
        fx.WriteConfigForFreePort();
        await fx.StartAsync();
        using var client = fx.CreateClient(fx.Config!.Token);

        var parentId = await CreateTagAsync(client, "parent");
        var childId = await CreateTagAsync(client, "child");

        using var relation = await client.PostAsJsonAsync(
            "/v1/relations",
            new { fromTagId = parentId, toTagId = childId, kind = "child", seq = (int?)null });
        Assert.Equal(HttpStatusCode.OK, relation.StatusCode);

        await ApplyTagsAsync(client, "file:///C:/expand-child.txt", ["child"]);

        using var withoutExpand = await client.GetAsync("/v1/query?q=" + Uri.EscapeDataString("parent"));
        Assert.Equal(HttpStatusCode.OK, withoutExpand.StatusCode);
        Assert.Empty((await ReadJsonAsync(withoutExpand)).GetProperty("data").GetProperty("items").EnumerateArray());

        using var withExpand = await client.GetAsync(
            "/v1/query?q=" + Uri.EscapeDataString("parent") + "&expand=child");
        Assert.Equal(HttpStatusCode.OK, withExpand.StatusCode);
        var hits = (await ReadJsonAsync(withExpand)).GetProperty("data").GetProperty("items")
            .EnumerateArray().Select(e => e.GetProperty("uri").GetString()).ToList();
        Assert.Contains("file:///C:/expand-child.txt", hits);
    }

    [Fact]
    public async Task Relations_batch_success_shape_is_items_and_dataVersion()
    {
        await using var fx = ServiceProcessFixture.CreateIsolated();
        fx.WriteConfigForFreePort();
        await fx.StartAsync();
        using var client = fx.CreateClient(fx.Config!.Token);

        var a = await CreateTagAsync(client, "rel-a");
        var b = await CreateTagAsync(client, "rel-b");

        using var batch = await client.PostAsJsonAsync(
            "/v1/relations/batch",
            new
            {
                items = new[]
                {
                    new { fromTagId = a, toTagId = b, kind = "related", seq = (int?)null },
                },
            });
        Assert.Equal(HttpStatusCode.OK, batch.StatusCode);
        var json = await ReadJsonAsync(batch);
        Assert.Equal("ok", json.GetProperty("status").GetString());
        var data = json.GetProperty("data");
        Assert.True(data.TryGetProperty("items", out var items));
        Assert.Single(items.EnumerateArray());
        Assert.True(data.TryGetProperty("dataVersion", out var version));
        Assert.True(version.GetInt32() >= 1);
        Assert.Equal(2, data.EnumerateObject().Count());
    }

    [Fact]
    public async Task Performance_boundaries_and_receipt_shapes()
    {
        await using var fx = ServiceProcessFixture.CreateIsolated();
        fx.WriteConfigForFreePort();
        await fx.StartAsync();
        using var client = fx.CreateClient(fx.Config!.Token);

        var oversize = Enumerable.Range(0, 101).Select(i => (object)$"t{i}").ToArray();
        using var addRemove101 = await client.PostAsJsonAsync(
            "/v1/resources/tags",
            new
            {
                uri = "file:///C:/batch-limit.txt",
                kind = "file",
                add = oversize,
                createMissing = false,
            });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, addRemove101.StatusCode);
        AssertErrorCode(await ReadJsonAsync(addRemove101), "batch_limit_exceeded");

        using var relationsEmpty = await client.PostAsJsonAsync(
            "/v1/relations/batch",
            new { items = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, relationsEmpty.StatusCode);
        AssertErrorCode(await ReadJsonAsync(relationsEmpty), "batch_limit_exceeded");

        // Limit is enforced before tag existence checks — synthetic Guids are enough.
        var from = Guid.NewGuid();
        var tooMany = Enumerable.Range(0, 101)
            .Select(_ => new { fromTagId = from, toTagId = Guid.NewGuid(), kind = "related", seq = (int?)null })
            .ToArray();
        using var relations101 = await client.PostAsJsonAsync(
            "/v1/relations/batch",
            new { items = tooMany });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, relations101.StatusCode);
        AssertErrorCode(await ReadJsonAsync(relations101), "batch_limit_exceeded");

        using var relationsNoFilter = await client.GetAsync("/v1/relations");
        Assert.Equal(HttpStatusCode.BadRequest, relationsNoFilter.StatusCode);
        AssertErrorCode(await ReadJsonAsync(relationsNoFilter), "relations_filter_required");

        using var suggestBlank = await client.GetAsync("/v1/tags/suggest?q=");
        Assert.Equal(HttpStatusCode.BadRequest, suggestBlank.StatusCode);
        AssertErrorCode(await ReadJsonAsync(suggestBlank), "bad_request");

        using var tagsBlank = await client.GetAsync("/v1/tags?q=");
        Assert.Equal(HttpStatusCode.BadRequest, tagsBlank.StatusCode);
        AssertErrorCode(await ReadJsonAsync(tagsBlank), "bad_request");

        using var suggestOffset = await client.GetAsync(
            "/v1/tags/suggest?q=" + Uri.EscapeDataString("工作") + "&offset=1");
        Assert.Equal(HttpStatusCode.BadRequest, suggestOffset.StatusCode);
        AssertErrorCode(await ReadJsonAsync(suggestOffset), "bad_request");

        using var limitZero = await client.GetAsync("/v1/tags?limit=0");
        Assert.Equal(HttpStatusCode.BadRequest, limitZero.StatusCode);
        AssertErrorCode(await ReadJsonAsync(limitZero), "bad_request");

        using var limitOver = await client.GetAsync("/v1/tags?limit=1025");
        Assert.Equal(HttpStatusCode.BadRequest, limitOver.StatusCode);
        AssertErrorCode(await ReadJsonAsync(limitOver), "bad_request");

        using var write = await client.PostAsJsonAsync(
            "/v1/tags",
            new { name = "receipt-shape" });
        Assert.Equal(HttpStatusCode.OK, write.StatusCode);
        var writeJson = await ReadJsonAsync(write);
        Assert.True(writeJson.GetProperty("data").TryGetProperty("dataVersion", out _));

        await ApplyTagsAsync(client, "file:///C:/receipt-list.txt", ["receipt-shape"]);

        using var preview = await client.GetAsync("/v1/query/preview?q=" + Uri.EscapeDataString("receipt-shape"));
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.False((await ReadJsonAsync(preview)).GetProperty("data").TryGetProperty("total", out _));

        using var resources = await client.GetAsync("/v1/resources");
        Assert.Equal(HttpStatusCode.OK, resources.StatusCode);
        var list = await ReadJsonAsync(resources);
        var items = list.GetProperty("data").GetProperty("items").EnumerateArray().ToList();
        Assert.NotEmpty(items);
        foreach (var item in items)
            Assert.False(item.TryGetProperty("tags", out _));
    }

    private static async Task<Guid> CreateTagAsync(HttpClient client, string name)
    {
        using var response = await client.PostAsJsonAsync(
            "/v1/tags",
            new { name, aliases = new[] { name } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await ReadJsonAsync(response);
        return json.GetProperty("data").GetProperty("id").GetGuid();
    }

    private static async Task ApplyTagsAsync(HttpClient client, string uri, string[] aliases)
    {
        using var response = await client.PostAsJsonAsync(
            "/v1/resources/tags",
            new
            {
                uri,
                kind = "file",
                add = aliases.Cast<object>().ToArray(),
                createMissing = false,
            });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static void AssertErrorCode(JsonElement json, string code)
    {
        Assert.Equal("error", json.GetProperty("status").GetString());
        Assert.Equal(code, json.GetProperty("data").GetProperty("code").GetString());
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonSerializer.DeserializeAsync<JsonElement>(stream);
    }
}
