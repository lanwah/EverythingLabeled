using System.Runtime.InteropServices;

namespace EverythingLabeled.ExplorerCommand;

[ComVisible(true)]
[Guid("6F3C8A21-9B4E-4D71-A2C8-1E5F0D8B7A01")]
[ClassInterface(ClassInterfaceType.None)]
public sealed class TagCommand : ExplorerCommandBase
{
    protected override string Title => "打标";

    protected override string Mode => "tag";
}
