namespace EverythingLabeled.ExplorerHost;

/// <summary>Pure helpers for Find list keyboard navigation from the query box.</summary>
public static class FindListNavigation
{
    /// <summary>
    /// Down from the query box: select first if none, otherwise advance one (stay on last).
    /// Returns -1 when the list is empty.
    /// </summary>
    public static int NextDownIndex(int selectedIndex, int count)
    {
        if (count <= 0)
            return -1;

        if (selectedIndex < 0)
            return 0;

        return selectedIndex < count - 1 ? selectedIndex + 1 : selectedIndex;
    }

    /// <summary>
    /// Up from the query box: select last if none, otherwise move up one (stay on first).
    /// Returns -1 when the list is empty.
    /// </summary>
    public static int NextUpIndex(int selectedIndex, int count)
    {
        if (count <= 0)
            return -1;

        if (selectedIndex < 0)
            return count - 1;

        return selectedIndex > 0 ? selectedIndex - 1 : selectedIndex;
    }
}
