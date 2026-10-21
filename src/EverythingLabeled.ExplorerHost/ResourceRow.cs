using EverythingLabeled.Client;

namespace EverythingLabeled.ExplorerHost;

internal sealed class ResourceRow
{
    public ResourceRow(QueryHit hit)
    {
        Hit = hit;
        TagLabels = FindResultPresentation.TagLabels(hit.Tags);
    }

    public QueryHit Hit { get; }

    public string Kind => Hit.Kind;

    public string Title => FindResultPresentation.Title(Hit.Uri, Hit.DisplayName);

    public string Subtitle => ReplaceKindGuess.Label(Hit.Kind) + " · " + Hit.Uri;

    public IReadOnlyList<string> TagLabels { get; }
}
