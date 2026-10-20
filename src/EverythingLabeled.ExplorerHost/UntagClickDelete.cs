namespace EverythingLabeled.ExplorerHost;

/// <summary>
/// Tag and untag, when exactly one path is included, show current-tag chips.
/// Clicking × removes that alias. Untag has no alias box; multi-select does not show chips.
/// </summary>
public static class UntagClickDelete
{
    public static bool IsEnabled(int includedPathCount) => includedPathCount == 1;

    public static bool ShowsPanel(HostMode mode, int includedPathCount) =>
        mode is HostMode.Tag or HostMode.Untag && IsEnabled(includedPathCount);
}
