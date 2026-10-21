using EverythingLabeled.Client;

namespace EverythingLabeled.ExplorerHost;

public static class TagLabelFormat
{
    public static string Format(string alias, string? ns, string name)
    {
        var head = string.IsNullOrEmpty(ns) ? name : ns + "/" + name;
        return alias == name ? head : $"{alias}  ({head})";
    }

    public static AliasRow FromSuggest(SuggestItem item) =>
        new(item.Alias, Format(item.Alias, item.Namespace, item.Name), item.NameInitials);
}
