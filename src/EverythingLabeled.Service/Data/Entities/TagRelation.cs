namespace EverythingLabeled.Service.Data.Entities;

public sealed class TagRelation
{
    public Guid Id { get; set; }
    public Guid FromTagId { get; set; }
    public Guid ToTagId { get; set; }
    public required string Kind { get; set; }
    public int? Seq { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Revision { get; set; }

    public Tag FromTag { get; set; } = null!;
    public Tag ToTag { get; set; } = null!;
}
