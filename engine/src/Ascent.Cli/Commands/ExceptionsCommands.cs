using System.ComponentModel;
using System.Globalization;
using Ascent.Cli.Hosting;
using Ascent.Content.Model;
using Ascent.Content.Reporting;
using Ascent.Content.Security;
using Ascent.Core.Platform;
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
    public DateOnly ResolveToday(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        return Today is null
            ? DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime)
            : DateOnly.ParseExact(Today, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public override ValidationResult Validate() =>
        Today is null || DateOnly.TryParseExact(Today, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            ? ValidationResult.Success()
            : ValidationResult.Error("--today must be a date like 2026-10-03.");
}

/// <summary><c>ascent exceptions check</c>.</summary>
public sealed class ExceptionsCheckCommand(EngineHost host) : Command<ExceptionSettings>
{
    /// <inheritdoc />
    public override int Execute(CommandContext context, ExceptionSettings settings, CancellationToken cancellationToken)
    {
        var findings = ExceptionRegister.Check(host.Content, settings.ResolveToday(host.Time));
        if (settings.Json)
        {
            host.Out.WriteLine(JsonOutput.Serialize(new FindingsReport(findings)));
        }
        else
        {
            Output.PrintFindings(host.OutputConsole(), findings);
        }

        return findings.Any(f => f.Severity == Severity.Error) ? 1 : 0;
    }
}

/// <summary>Options for <c>ascent exceptions emit</c>.</summary>
public sealed class ExceptionsEmitSettings : ExceptionSettings
{
    /// <summary>Where to write the Trivy ignore file.</summary>
    [CommandOption("--trivy <PATH>")]
    [Description("Write a .trivyignore file here (relative to the repository root).")]
    public string? Trivy { get; init; }

    /// <summary>Where to write the NuGet suppression props file.</summary>
    [CommandOption("--nuget <PATH>")]
    [Description("Write an MSBuild props file with NuGetAuditSuppress items here (relative to the repository root).")]
    public string? NuGet { get; init; }

    /// <inheritdoc />
    public override ValidationResult Validate() =>
        Trivy is null && NuGet is null ? ValidationResult.Error("Specify --trivy and/or --nuget.") : base.Validate();
}

/// <summary><c>ascent exceptions emit</c>: writes scanner suppression files generated from the register.</summary>
public sealed class ExceptionsEmitCommand(EngineHost host) : Command<ExceptionsEmitSettings>
{
    /// <inheritdoc />
    public override int Execute(CommandContext context, ExceptionsEmitSettings settings, CancellationToken cancellationToken)
    {
        var root = host.Paths.RepoRoot;
        var today = settings.ResolveToday(host.Time);
        var entries = ExceptionRegister.Read(host.Content);
        var console = host.OutputConsole();

        if (settings.Trivy is not null)
        {
            File.WriteAllText(SafePath.Resolve(root, settings.Trivy), ExceptionRegister.TrivyIgnore(entries, today));
            console.MarkupLine("Wrote " + Markup.Escape(settings.Trivy));
        }

        if (settings.NuGet is not null)
        {
            File.WriteAllText(SafePath.Resolve(root, settings.NuGet), ExceptionRegister.NuGetSuppressions(entries, today));
            console.MarkupLine("Wrote " + Markup.Escape(settings.NuGet));
        }

        return 0;
    }
}
