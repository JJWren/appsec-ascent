using Ascent.Core.Platform;

namespace Ascent.Tests.Shared;

/// <summary>
/// A process runner for tests: it records every command and answers from <see cref="Respond"/>, and only the tools in
/// <see cref="Installed"/> exist. Tests never start real tools unless they opt in with the real runner.
/// </summary>
internal sealed class RecordingProcessRunner : IProcessRunner
{
    /// <summary>The tools that exist.</summary>
    public HashSet<ExternalTool> Installed { get; } = [];

    /// <summary>Every command run, in order.</summary>
    public List<ToolCommand> Commands { get; } = [];

    /// <summary>Answers a command; null means success with no output.</summary>
    public Func<ToolCommand, ProcessResult?>? Respond { get; set; }

    /// <summary>A successful result with some output.</summary>
    public static ProcessResult Ok(string output = "") => new(0, output, string.Empty, false, false);

    /// <summary>A failed result.</summary>
    public static ProcessResult Fail(string error = "failed", int exitCode = 1) => new(exitCode, string.Empty, error, false, false);

    /// <summary>The commands for a tool, as argument lines.</summary>
    public IReadOnlyList<string> Lines(ExternalTool tool) => Commands.Where(c => c.Tool == tool).Select(c => string.Join(' ', c.Arguments)).ToList();

    /// <inheritdoc />
    public bool IsAvailable(ExternalTool tool) => Installed.Contains(tool);

    /// <inheritdoc />
    public Task<ProcessResult> RunAsync(ToolCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Commands.Add(command);
        if (!Installed.Contains(command.Tool))
        {
            throw new ToolNotFoundException(command.Tool);
        }

        return Task.FromResult(Respond?.Invoke(command) ?? Ok());
    }
}
