using EverythingLabeled.Service.Data.Entities;

namespace EverythingLabeled.Service.Services.Query;

public static class QueryHitTagLabels
{
    public static string PrimaryAlias(Tag tag)
    {
        var alias = tag.Aliases
            .OrderBy(a => a.AliasNormalized, StringComparer.Ordinal)
            .Select(a => a.Alias)
            .FirstOrDefault();
        return string.IsNullOrEmpty(alias) ? tag.Name : alias;
    }
}
