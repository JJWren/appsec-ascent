using System.ComponentModel;
using System.Globalization;
using Ascent.Content.Loading;
using Ascent.Content.Model;
using Ascent.Content.Reporting;
using Ascent.Content.Security;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Ascent.Cli.Commands;

/// <summary>Options for exception-register commands.</summary>
public class ExceptionSettings : RepoSettings
{
    /// <summary>Date to evaluate expiry against (yyyy-MM-dd); defaults to today (UTC).</summary>
    [CommandOption("--today <DATE>")]
    [Description("Evaluate expiry as of this date (yyyy-MM-dd). Defaults to today (UTC).")]
    public string? Today { get; init; }

    /// <summary>Parses <see cref="Today"/>.</summary>
    public DateOnly ResolveToday() =>
        Today is null
            ? DateOnly.FromDateTime(DateTime.UtcNow)
            : DateOnly.ParseExact(Today, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary><c>ascent exceptions check</c>.</summary>
public sealed class ExceptionsCheckCommand : Command<ExceptionSettings>
{
    /// <inheritdoc />
    public override int Execute(CommandContext context, ExceptionSettings settings, CancellationToken cancellationToken)
    {
        var findings = ExceptionRegister.Check(ContentLoader.Load(Output.ResolveRoot(settings.Root)), settings.ResolveToday());
        if (settings.Json)
        {
            Console.Out.WriteLine(JsonOutput.Serialize(new FindingsReport(findings)));
        }
        else
        {
            Output.PrintFindings(Output.Create(settings.Plain), findings);
        }

        return findings.Any(f => f.Severity == Severity.Error) ? 1 : 0;
    }
}

/// <summary>Options for <c>ascent exceptions emit</c>.</summary>
public sealed class ExceptionsEmitSettings : ExceptionSettings
{
    /// <summary>Where to write the Trivy ignore file.</summary>
    [CommandOption("--trivy <PATH>")]
    [Description("Write a .trivyignore file here.")]
    public string? Trivy { get; init; }

    /// <summary>Where to write the NuGet suppression props file.</summary>
    [CommandOption("--nuget <PATH>")]
    [Description("Write an MSBuild props file with NuGetAuditSuppress items here.")]
    public string? NuGet { get; init; }

    /// <inheritdoc />
    public override ValidationResult Validate() =>
        Trivy is null && NuGet is null ? ValidationResult.Error("Specify --trivy and/or --nuget.") : ValidationResult.Success();
}

/// <summary><c>ascent exceptions emit</c>: writes scanner suppression files generated from the register.</summary>
public sealed class ExceptionsEmitCommand : Command<ExceptionsEmitSettings>
{
    /// <inheritdoc />
    public override int Execute(CommandContext context, ExceptionsEmitSettings settings, CancellationToken cancellationToken)
    {
        var root = Output.ResolveRoot(settings.Root);
        var today = settings.ResolveToday();
        var entries = ExceptionRegister.Read(ContentLoader.Load(root));
        var console = Output.Create(settings.Plain);

        if (settings.Trivy is not null)
        {
            File.WriteAllText(Path.Combine(root, settings.Trivy), ExceptionRegister.TrivyIgnore(entries, today));
            console.MarkupLine("Wrote " + Markup.Escape(settings.Trivy));
        }

        if (settings.NuGet is not null)
        {
            File.WriteAllText(Path.Combine(root, settings.NuGet), ExceptionRegister.NuGetSuppressions(entries, today));
            console.MarkupLine("Wrote " + Markup.Escape(settings.NuGet));
        }

        return 0;
    }
}
