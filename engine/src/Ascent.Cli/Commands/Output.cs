using System.Globalization;
using Ascent.Content.Model;
using Spectre.Console;

namespace Ascent.Cli.Commands;

/// <summary>Console creation and shared rendering helpers.</summary>
internal static class Output
{
    /// <summary>Creates a console; plain mode disables ANSI and color entirely.</summary>
    public static IAnsiConsole Create(bool plain) =>
        plain
            ? AnsiConsole.Create(new AnsiConsoleSettings { Ansi = AnsiSupport.No, ColorSystem = ColorSystemSupport.NoColors })
            : AnsiConsole.Console;

    /// <summary>Resolves the repository root.</summary>
    public static string ResolveRoot(string? explicitRoot)
    {
        if (!string.IsNullOrWhiteSpace(explicitRoot))
        {
            return Path.GetFullPath(explicitRoot);
        }

        for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AppSecAscent.slnx")))
            {
                return directory.FullName;
            }
        }

        return Directory.GetCurrentDirectory();
    }

    /// <summary>Prints findings as <c>path:line: SEVERITY RULE: message</c> lines plus a summary.</summary>
    public static void PrintFindings(IAnsiConsole console, IReadOnlyList<Finding> findings)
    {
        foreach (var finding in findings)
        {
            var location = finding.Line is { } line ? finding.Path + ":" + line.ToString(CultureInfo.InvariantCulture) : finding.Path;
            var severity = finding.Severity == Severity.Error ? "[red]ERROR[/]" : "[yellow]WARN[/]";
            console.MarkupLine(Markup.Escape(location) + ": " + severity + " " + Markup.Escape(finding.RuleId) + ": " + Markup.Escape(finding.Message));
            if (finding.Hint is not null)
            {
                console.MarkupLine("    [grey]hint:[/] " + Markup.Escape(finding.Hint));
            }
        }

        var errors = findings.Count(f => f.Severity == Severity.Error);
        var warnings = findings.Count - errors;
        console.MarkupLine(FormattableString.Invariant(
            $"{(errors > 0 ? "[red]" : "[green]")}{errors} error(s)[/], {warnings} warning(s)."));
    }
}
