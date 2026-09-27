namespace EverythingLabeled.Service.Data.Entities;

public sealed class Tag
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string NameInitials { get; set; }
    public required string NameNormalized { get; set; }
    public string? Namespace { get; set; }
    public int? Seq { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Revision { get; set; }

    public ICollection<TagAlias> Aliases { get; set; } = new List<TagAlias>();
    public ICollection<ResourceTag> ResourceTags { get; set; } = new List<ResourceTag>();
    public ICollection<TagRelation> RelationsFrom { get; set; } = new List<TagRelation>();
    public ICollection<TagRelation> RelationsTo { get; set; } = new List<TagRelation>();
}
