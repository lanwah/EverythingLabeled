using System.Runtime.InteropServices;

namespace EverythingLabeled.ExplorerCommand;

[ComVisible(true)]
[Guid("6F3C8A21-9B4E-4D71-A2C8-1E5F0D8B7A04")]
[ClassInterface(ClassInterfaceType.None)]
public sealed class FindCommand : ExplorerCommandBase
{
    protected override string Title => "按标签查找";

    protected override string Mode => "find";

    protected override bool WritePathsFile => false;
}
