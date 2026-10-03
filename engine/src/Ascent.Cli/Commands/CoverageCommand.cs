using System.ComponentModel;
using System.Globalization;
using Ascent.Content.Coverage;
using Ascent.Content.Loading;
using Ascent.Content.Reporting;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Ascent.Cli.Commands;

/// <summary>Coverage options.</summary>
public sealed class CoverageSettings : RepoSettings
{
    /// <summary>Outline version to report against.</summary>
    [CommandOption("--outline <VERSION>")]
    [Description("Outline version (defaults to the season's outline).")]
    public string? Outline { get; init; }

    /// <summary>Pack whose exit criteria must be met (exit code 1 otherwise).</summary>
    [CommandOption("--gate <PACK>")]
    [Description("Fail unless the pack (d1…d8) meets its exit criteria.")]
    public string? Gate { get; init; }
}

/// <summary><c>ascent coverage</c>: counts per Domain and Objective against the outline. Never prints content.</summary>
public sealed class CoverageCommand : Command<CoverageSettings>
{
    /// <inheritdoc />
    public override int Execute(CommandContext context, CoverageSettings settings, CancellationToken cancellationToken)
    {
        var console = Output.Create(settings.Plain);
        var report = CoverageCalculator.Calculate(ContentLoader.Load(Output.ResolveRoot(settings.Root)), settings.Outline);
        if (report is null)
        {
            console.MarkupLine("[red]No valid outline was found.[/] Run 'ascent lint' for details.");
            return 1;
        }

        var gaps = settings.Gate is null ? [] : CoverageCalculator.GateGaps(report, settings.Gate);
        if (settings.Json)
        {
            Console.Out.WriteLine(JsonOutput.Serialize(new { report, gate = settings.Gate, gaps }));
        }
        else
        {
            Render(console, report, settings.Plain);
            foreach (var gap in gaps)
            {
                console.MarkupLine("[red]gate:[/] " + Markup.Escape(gap));
            }
        }

        return gaps.Count > 0 ? 1 : 0;
    }

    private static void Render(IAnsiConsole console, CoverageReport report, bool plain)
    {
        var table = new Table().Title("Coverage against outline " + report.OutlineVersion);
        table.AddColumns("Domain", "Weight", "Objectives complete", "AI topics", "Diagnostic", "Simulation");
        foreach (var domain in report.Domains)
        {
            table.AddRow(
                Markup.Escape(domain.DomainId + " " + domain.Name),
                domain.Weight.ToString(CultureInfo.InvariantCulture) + "%",
                Fraction(domain.ObjectivesComplete, domain.Objectives),
                Fraction(domain.AiTopicsCovered, domain.AiTopics),
                domain.DiagnosticActual.ToString(CultureInfo.InvariantCulture) + " / " + domain.DiagnosticTarget.ToString("0.#", CultureInfo.InvariantCulture),
                domain.SimulationActual.ToString(CultureInfo.InvariantCulture) + " / " + domain.SimulationTarget.ToString("0.#", CultureInfo.InvariantCulture));
        }

        if (plain)
        {
            table.Border(TableBorder.Ascii);
        }

        console.Write(table);
        console.WriteLine(FormattableString.Invariant(
            $"Objectives complete: {report.ObjectivesComplete}/{report.ObjectivesTotal}. AI topics covered: {report.AiTopicsCovered}/{report.AiTopicsTotal}."));
    }

    private static string Fraction(int done, int total) =>
        done.ToString(CultureInfo.InvariantCulture) + " / " + total.ToString(CultureInfo.InvariantCulture);
}
