using System.Text;
using EverythingLabeled.Cli;

Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = Encoding.UTF8;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

return await ElApp.RunAsync(new ElInvocation
{
    Args = args,
    Stdout = Console.Out,
    Stderr = Console.Error,
    Stdin = Console.In,
    IsTty = !Console.IsInputRedirected,
    CancellationToken = cts.Token,
});
