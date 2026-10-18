namespace EverythingLabeled.ExplorerHost;

/// <summary>
/// Spec §5.3: tag and untag, when exactly one path is included, show current-tag chips.
/// Clicking × removes that alias. Multi-select stays hand-entry only.
/// </summary>
public static class UntagClickDelete
{
    public static bool IsEnabled(int includedPathCount) => includedPathCount == 1;

    public static bool ShowsPanel(HostMode mode, int includedPathCount) =>
        mode is HostMode.Tag or HostMode.Untag && IsEnabled(includedPathCount);
}
