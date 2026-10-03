using Ascent.Cli.Rendering;
using Ascent.Core.Errors;
using Ascent.Storage;
using Spectre.Console.Cli;

namespace Ascent.Cli.Hosting;

/// <summary>
/// Turns exceptions into a message, a next step and a fixed exit code (UX-U2-02, P23): Learner-actionable errors use
/// their own code, usage errors exit 2, and anything unexpected exits 1 with its type and stack trace in the local log.
/// </summary>
internal static class ErrorHandler
{
    public static int Handle(Exception exception, EngineHost host)
    {
        var renderer = host.Renderer;
        int exitCode;
        var unexpected = false;
        switch (exception)
        {
            case AscentException ascent:
                renderer.Status(Outcome.Fail, ascent.Message);
                if (ascent.NextStep is { } next)
                {
                    renderer.Line("Next: " + next);
                }

                exitCode = ascent.ExitCode;
                break;

            case CommandAppException usage:
                if (renderer is RichRenderer rich && usage.Pretty is { } pretty)
                {
                    rich.Write(pretty);
                }
                else
                {
                    renderer.Status(Outcome.Fail, usage.Message);
                }

                renderer.Line("Next: Run 'ascent --help'.");
                exitCode = ExitCodes.Usage;
                break;

            case OperationCanceledException:
                renderer.Status(Outcome.Warn, "Cancelled.");
                exitCode = ExitCodes.CheckFailed;
                break;

            default:
                var corrupt = StorageErrors.IsCorruption(exception);
                renderer.Status(
                    Outcome.Fail,
                    corrupt
                        ? new CorruptDatabaseException(host.Paths, exception).Message
                        : "Unexpected error (" + exception.GetType().Name + ").");
                renderer.Line(corrupt
                    ? "Next: " + new CorruptDatabaseException().NextStep
                    : "Next: Details are in .ascent/logs/. If it keeps happening, please report it.");
                exitCode = ExitCodes.CheckFailed;
                unexpected = !corrupt;
                break;
        }

        host.EndCommand(exitCode, exception, unexpected);
        return exitCode;
    }
}
