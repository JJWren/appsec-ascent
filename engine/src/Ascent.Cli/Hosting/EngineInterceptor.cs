using Ascent.Cli.Commands;
using Spectre.Console.Cli;

namespace Ascent.Cli.Hosting;

/// <summary>Applies global options before each command and records the outcome after it.</summary>
internal sealed class EngineInterceptor(EngineHost host) : ICommandInterceptor
{
    public void Intercept(CommandContext context, CommandSettings settings) =>
        host.BeginCommand(context.Name, settings as EngineSettings);

    public void InterceptResult(CommandContext context, CommandSettings settings, ref int result) =>
        host.EndCommand(result, exception: null);
}
