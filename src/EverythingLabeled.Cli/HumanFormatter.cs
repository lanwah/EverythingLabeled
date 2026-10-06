using EverythingLabeled.Client;

namespace EverythingLabeled.Cli;

internal static class HumanFormatter
{
    public static void WriteTagApply(
        TextWriter stdout,
        IReadOnlyList<string> requestedAliases,
        TagApplyData data,
        bool untag)
    {
        var changedIds = new HashSet<Guid>(untag ? data.Removed : data.Added);
        var changed = new List<string>();
        foreach (var alias in requestedAliases)
        {
            if (TryFindTagId(data, alias, out var id) && changedIds.Contains(id))
                changed.Add(alias);
        }

        if (changed.Count == 0)
            stdout.WriteLine("unchanged");
        else
            stdout.WriteLine((untag ? "removed: " : "added: ") + string.Join(", ", changed));

        if (data.CreatedTags.Count > 0)
            stdout.WriteLine("created: " + string.Join(", ", data.CreatedTags.Select(t => t.Name)));
    }

    public static void WriteTags(TextWriter stdout, TagsPage page)
    {
        foreach (var tag in page.Items)
        {
            foreach (var alias in tag.Aliases)
                stdout.WriteLine(alias);
        }
    }

    private static bool TryFindTagId(TagApplyData data, string alias, out Guid id)
    {
        foreach (var tag in data.Resource.Tags)
        {
            if (HasAlias(tag, alias))
            {
                id = tag.Id;
                return true;
            }
        }

        foreach (var tag in data.CreatedTags)
        {
            if (HasAlias(tag, alias))
            {
                id = tag.Id;
                return true;
            }
        }

        id = default;
        return false;
    }

    private static bool HasAlias(TagInfo tag, string alias)
    {
        foreach (var item in tag.Aliases)
        {
            if (string.Equals(item, alias, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
