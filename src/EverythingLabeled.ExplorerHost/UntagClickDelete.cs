namespace EverythingLabeled.ExplorerHost;

/// <summary>
/// Spec §5.3: untag single-select may click a tags-list alias to delete immediately.
/// Multi-select is hand-entry only.
/// </summary>
public static class UntagClickDelete
{
    public static bool IsEnabled(int includedPathCount) => includedPathCount == 1;
}
