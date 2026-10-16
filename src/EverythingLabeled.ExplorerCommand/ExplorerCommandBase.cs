using EverythingLabeled.ExplorerCommand.Interop;
using EverythingLabeled.ExplorerShared;

namespace EverythingLabeled.ExplorerCommand;

public abstract class ExplorerCommandBase : IExplorerCommand
{
    protected abstract string Title { get; }

    protected abstract string Mode { get; }

    /// <summary>When true, Invoke writes a paths-file from the selection.</summary>
    protected virtual bool WritePathsFile => true;

    protected virtual EXPCMDSTATE ResolveState(IShellItemArray? psiItemArray) => EXPCMDSTATE.ECS_ENABLED;

    public int GetTitle(IShellItemArray? psiItemArray, out string? ppszName)
    {
        ppszName = Title;
        return HResults.S_OK;
    }

    public int GetIcon(IShellItemArray? psiItemArray, out string? ppszIcon)
    {
        try
        {
            // 必须是绝对路径：包走 ExternalLocation，相对路径在壳层 surrogate 里无从解析。
            ppszIcon = ExplorerIcons.Resolve(CommandDirectory.Resolve(), Mode);
        }
        catch
        {
            // 拿不到就当没图标 —— 图标这里出岔子不能让壳层把整项菜单丢掉
            ppszIcon = null;
        }

        return ppszIcon is null ? HResults.E_NOTIMPL : HResults.S_OK;
    }

    public int GetToolTip(IShellItemArray? psiItemArray, out string? ppszInfotip)
    {
        ppszInfotip = null;
        return HResults.E_NOTIMPL;
    }

    public int GetCanonicalName(out Guid pguidCommandName)
    {
        pguidCommandName = Guid.Empty;
        return HResults.E_NOTIMPL;
    }

    public int GetState(IShellItemArray? psiItemArray, bool fOkToBeSlow, out EXPCMDSTATE pCmdState)
    {
        pCmdState = ResolveState(psiItemArray);
        return HResults.S_OK;
    }

    public int Invoke(IShellItemArray? psiItemArray, object? pbc)
    {
        try
        {
            string? pathsFile = null;
            if (WritePathsFile)
            {
                var paths = ShellItemPaths.GetFilePaths(psiItemArray);
                if (paths.Count > 0)
                {
                    pathsFile = PathsFile.Write(paths);
                }
            }

            HostLauncher.StartFireAndForget(Mode, pathsFile);
            return HResults.S_OK;
        }
        catch
        {
            return HResults.E_FAIL;
        }
    }

    public int GetFlags(out EXPCMDFLAGS pFlags)
    {
        pFlags = EXPCMDFLAGS.ECF_DEFAULT;
        return HResults.S_OK;
    }

    public int EnumSubCommands(out IEnumExplorerCommand? ppEnum)
    {
        ppEnum = null;
        return HResults.E_NOTIMPL;
    }
}
