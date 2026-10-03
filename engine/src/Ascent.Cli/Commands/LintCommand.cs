using System.ComponentModel;
using Ascent.Content.Loading;
using Ascent.Content.Model;
using Ascent.Content.Reporting;
using Ascent.Content.Rules;
using Spectre.Console.Cli;

namespace Ascent.Cli.Commands;

/// <summary>Lint options.</summary>
public sealed class LintSettings : RepoSettings
{
    /// <summary>Private sealed repository root, to lint plaintext Questions (maintainers only).</summary>
    [CommandOption("--sealed-sources <PATH>")]
    [Description("Private sealed repository root; also lints plaintext Questions. Output never includes their content.")]
    public string? SealedSources { get; init; }
}

/// <summary><c>ascent lint</c>: checks content against every framework rule.</summary>
public sealed class LintCommand : Command<LintSettings>
{
    /// <inheritdoc />
    public override int Execute(CommandContext context, LintSettings settings, CancellationToken cancellationToken)
    {
        var root = Output.ResolveRoot(settings.Root);
        var sealedSources = settings.SealedSources is null ? null : Path.GetFullPath(settings.SealedSources);
        var findings = new RuleEngine().Run(ContentLoader.Load(root, sealedSources));

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
