using EverythingLabeled.Service.Data.Entities;

namespace EverythingLabeled.Service.Services.Tags;

internal static class TagListOrder
{
    // SQLite provider cannot translate DateTimeOffset in ORDER BY; apply §3.1 after materialize.
    public static IOrderedEnumerable<Tag> Apply(IEnumerable<Tag> tags) =>
        tags
            .OrderBy(t => t.Seq is null)
            .ThenBy(t => t.Seq)
            .ThenBy(t => t.CreatedAt)
            .ThenBy(t => t.Id);
}
