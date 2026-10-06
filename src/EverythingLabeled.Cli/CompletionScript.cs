namespace EverythingLabeled.Cli;

internal static class CompletionScript
{
    public const string PowerShell = """
Register-ArgumentCompleter -Native -CommandName el -ScriptBlock {
    param($wordToComplete, $commandAst, $cursorPosition)
    $prefix = "$wordToComplete"
    try {
        $output = & el suggest --json -- $prefix 2>$null
        if ($LASTEXITCODE -ne 0) { return }
        $page = $output | ConvertFrom-Json
        foreach ($item in @($page.items)) {
            $alias = $item.alias
            if ($null -ne $alias -and $alias -ne '') {
                [System.Management.Automation.CompletionResult]::new($alias, $alias, 'ParameterValue', $alias)
            }
        }
    } catch {
    }
}
""";
}
