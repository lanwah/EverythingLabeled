namespace EverythingLabeled.Service.Data.Entities;

public sealed class ResourceTag
{
    public Guid ResourceId { get; set; }
    public Guid TagId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Resource Resource { get; set; } = null!;
    public Tag Tag { get; set; } = null!;
}
