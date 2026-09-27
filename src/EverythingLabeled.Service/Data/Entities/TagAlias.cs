namespace EverythingLabeled.Service.Data.Entities;

public sealed class TagAlias
{
    public Guid TagId { get; set; }
    public required string Alias { get; set; }
    public required string AliasNormalized { get; set; }

    public Tag Tag { get; set; } = null!;
}
