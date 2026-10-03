using System.ComponentModel;
using Spectre.Console.Cli;

namespace Ascent.Cli.Commands;

/// <summary>Options every command accepts.</summary>
public class EngineSettings : CommandSettings
{
    /// <summary>Repository root; defaults to the nearest parent folder containing AppSecAscent.slnx.</summary>
    [CommandOption("--root <PATH>")]
    [Description("Repository root. Defaults to the nearest parent folder containing AppSecAscent.slnx.")]
    public string? Root { get; init; }

    /// <summary>Plain output without color, tables or live widgets.</summary>
    [CommandOption("--plain")]
    [Description("Plain text output without color, tables or live widgets (NO_COLOR and TERM=dumb are honoured too).")]
    public bool Plain { get; init; }

    /// <summary>Prints how long each phase took.</summary>
    [CommandOption("--timings")]
    [Description("Print how long each phase of the command took.")]
    public bool Timings { get; init; }
}

/// <summary>Options shared by commands that report on a repository.</summary>
public class RepoSettings : EngineSettings
{
    /// <summary>Machine-readable JSON output.</summary>
    [CommandOption("--json")]
    [Description("Machine-readable JSON output.")]
    public bool Json { get; init; }
}
