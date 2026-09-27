namespace EverythingLabeled.Service.Data.Entities;

public sealed class Resource
{
    public Guid Id { get; set; }
    public required string Uri { get; set; }
    public required string Kind { get; set; }
    public string? DisplayName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Revision { get; set; }

    public ICollection<ResourceTag> ResourceTags { get; set; } = new List<ResourceTag>();
}
