using System.Runtime.InteropServices;
using Ascent.Cli.Hosting;
using Ascent.Core.Errors;

using var host = new EngineHost();
using var cancellation = new CancellationTokenSource();

// The first Ctrl+C asks the running command to stop and clean up (unsealed scopes, child processes);
// a second one ends the process immediately.
Console.CancelKeyPress += (_, e) =>
{
    if (!cancellation.IsCancellationRequested)
    {
        e.Cancel = true;
        cancellation.Cancel();
    }
};

PosixSignalRegistration? terminate = null;
try
{
    terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
    {
        context.Cancel = true;
        cancellation.Cancel();
    });
}
catch (PlatformNotSupportedException)
{
    // SIGTERM isn't available here; Ctrl+C handling still applies.
}

using (terminate)
{
    var exitCode = await EngineApp.Create(host).RunAsync(args, cancellation.Token);

    // Spectre reports some usage errors as negative codes; the Engine always uses 2 (UX-U2-02).
    return exitCode < 0 ? ExitCodes.Usage : exitCode;
}
