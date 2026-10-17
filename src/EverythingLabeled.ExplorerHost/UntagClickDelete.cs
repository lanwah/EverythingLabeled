namespace EverythingLabeled.ExplorerHost;

/// <summary>
/// Spec §5.3: untag single-select may click × on a current-tag chip to delete immediately.
/// Multi-select is hand-entry only.
/// </summary>
public static class UntagClickDelete
{
    public static bool IsEnabled(int includedPathCount) => includedPathCount == 1;
}
