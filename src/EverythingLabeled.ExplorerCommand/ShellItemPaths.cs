using System.Runtime.InteropServices;
using EverythingLabeled.ExplorerCommand.Interop;

namespace EverythingLabeled.ExplorerCommand;

internal static class ShellItemPaths
{
    public static IReadOnlyList<string> GetFilePaths(IShellItemArray? array)
    {
        var paths = new List<string>();
        if (array is null)
        {
            return paths;
        }

        if (array.GetCount(out var count) != HResults.S_OK || count == 0)
        {
            return paths;
        }

        for (uint i = 0; i < count; i++)
        {
            if (array.GetItemAt(i, out var item) != HResults.S_OK || item is null)
            {
                continue;
            }

            try
            {
                if (item.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, out var path) == HResults.S_OK
                    && !string.IsNullOrEmpty(path))
                {
                    paths.Add(path);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(item);
            }
        }

        return paths;
    }

    public static uint GetCount(IShellItemArray? array)
    {
        if (array is null)
        {
            return 0;
        }

        return array.GetCount(out var count) == HResults.S_OK ? count : 0;
    }
}
