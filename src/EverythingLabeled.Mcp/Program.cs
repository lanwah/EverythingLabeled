using EverythingLabeled.Mcp;

return await McpApp.RunAsync(new McpInvocation
{
    Args = args,
    Stdout = Console.Out,
    Stderr = Console.Error,
});
