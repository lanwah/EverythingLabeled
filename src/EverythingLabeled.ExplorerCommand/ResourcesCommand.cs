using System.Runtime.InteropServices;

namespace EverythingLabeled.ExplorerCommand;

[ComVisible(true)]
[Guid("6F3C8A21-9B4E-4D71-A2C8-1E5F0D8B7A06")]
[ClassInterface(ClassInterfaceType.None)]
public sealed class ResourcesCommand : ExplorerCommandBase
{
    protected override string Title => "管理资源";

    protected override string Mode => "resources";

    protected override bool WritePathsFile => false;
}
