using System.Runtime.InteropServices;
using EverythingLabeled.ExplorerCommand.Interop;

namespace EverythingLabeled.ExplorerCommand;

[ComVisible(true)]
[Guid("6F3C8A21-9B4E-4D71-A2C8-1E5F0D8B7A03")]
[ClassInterface(ClassInterfaceType.None)]
public sealed class TagsCommand : ExplorerCommandBase
{
    protected override string Title => "看标签";

    protected override string Mode => "tags";

    protected override EXPCMDSTATE ResolveState(IShellItemArray? psiItemArray)
    {
        var count = ShellItemPaths.GetCount(psiItemArray);
        return count == 1 ? EXPCMDSTATE.ECS_ENABLED : EXPCMDSTATE.ECS_DISABLED;
    }
}
