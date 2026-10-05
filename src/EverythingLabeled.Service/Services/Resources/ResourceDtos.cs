using System.Text.Json;
using System.Text.Json.Serialization;
using EverythingLabeled.Service.Services.Tags;

namespace EverythingLabeled.Service.Services.Resources;

public sealed record ResourceListDto(
    Guid Id,
    string Uri,
    string Kind,
    string? DisplayName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long Revision);

public sealed record ResourceDetailDto(
    Guid Id,
    string Uri,
    string Kind,
    string? DisplayName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long Revision,
    IReadOnlyList<TagDto> Tags);

public sealed record ResourceWriteDto(
    Guid Id,
    string Uri,
    string Kind,
    string? DisplayName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long Revision,
    IReadOnlyList<TagDto> Tags,
    int DataVersion);

public sealed record TagApplyResultDto(
    ResourceDetailDto Resource,
    IReadOnlyList<TagDto> CreatedTags,
    IReadOnlyList<Guid> Added,
    IReadOnlyList<Guid> Removed,
    int DataVersion);

public sealed record CreateResourceRequest(string? Uri, string? Kind, string? DisplayName);

public sealed record PatchResourceRequest(string? DisplayName, string? Kind);

[JsonConverter(typeof(TagApplyItemJsonConverter))]
public sealed record TagApplyItem(Guid? Id, string? Alias)
{
    public static TagApplyItem FromAlias(string alias) => new(null, alias);

    public static TagApplyItem FromId(Guid id) => new(id, null);
}

public sealed record TagApplyRequest(
    Guid? ResourceId,
    string? Uri,
    string? Kind,
    string? DisplayName,
    IReadOnlyList<TagApplyItem>? Add,
    IReadOnlyList<TagApplyItem>? Remove,
    bool CreateMissing);

public sealed class TagApplyItemJsonConverter : JsonConverter<TagApplyItem>
{
    public override TagApplyItem Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return TagApplyItem.FromAlias(reader.GetString() ?? "");

        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Tag apply item must be an alias string or { \"id\": \"<guid>\" }.");

        using var doc = JsonDocument.ParseValue(ref reader);
        if (!doc.RootElement.TryGetProperty("id", out var idProp))
            throw new JsonException("Tag apply item object must have id.");

        var raw = idProp.ValueKind == JsonValueKind.String ? idProp.GetString() : idProp.GetRawText();
        if (!Guid.TryParse(raw, out var id))
            throw new JsonException("Tag apply item id must be a GUID.");

        return TagApplyItem.FromId(id);
    }

    public override void Write(Utf8JsonWriter writer, TagApplyItem value, JsonSerializerOptions options)
    {
        if (value.Id is { } id)
        {
            writer.WriteStartObject();
            writer.WriteString("id", id);
            writer.WriteEndObject();
            return;
        }

        writer.WriteStringValue(value.Alias);
    }
}
