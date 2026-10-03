using System.ComponentModel;
using Spectre.Console.Cli;

namespace Ascent.Cli.Commands;

/// <summary>Options shared by commands that read a repository.</summary>
public class RepoSettings : CommandSettings
{
    /// <summary>Repository root; defaults to the nearest parent folder containing AppSecAscent.slnx.</summary>
    [CommandOption("--root <PATH>")]
    [Description("Repository root. Defaults to the nearest parent folder containing AppSecAscent.slnx.")]
    public string? Root { get; init; }

    /// <summary>Plain output without color or tables.</summary>
    [CommandOption("--plain")]
    [Description("Plain text output without color or tables (NO_COLOR is honoured too).")]
    public bool Plain { get; init; }

    /// <summary>Machine-readable JSON output.</summary>
    [CommandOption("--json")]
    [Description("Machine-readable JSON output.")]
    public bool Json { get; init; }
}
