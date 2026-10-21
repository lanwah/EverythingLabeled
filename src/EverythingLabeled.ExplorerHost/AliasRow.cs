namespace EverythingLabeled.ExplorerHost;

public sealed record AliasRow(string Alias, string Display, string NameInitials)
{
    public override string ToString() => Display;
}
