using Ascent.Content.Loading;
using Ascent.Content.Model;
using Ascent.Content.Reporting;
using Ascent.Content.Rules;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Ascent.Cli.Commands;

/// <summary>
/// <c>ascent verify-bundles</c>: checks Sealed Bundle metadata. Signature verification (BND-01) is added by the
/// Engine's sealing module; until then no Sealed Bundles are published.
/// </summary>
public sealed class VerifyBundlesCommand : Command<RepoSettings>
{
    /// <inheritdoc />
    public override int Execute(CommandContext context, RepoSettings settings, CancellationToken cancellationToken)
    {
        var index = ContentLoader.Load(Output.ResolveRoot(settings.Root));
        var bundlePaths = index.OfKind(DocumentKind.Bundle).Select(b => b.RelativePath).ToHashSet(StringComparer.Ordinal);
        var findings = new RuleEngine(BundleRules.All).Run(index)
            .Where(f => bundlePaths.Contains(f.Path))
            .ToList();

        if (settings.Json)
        {
            Console.Out.WriteLine(JsonOutput.Serialize(new FindingsReport(findings)));
        }
        else
        {
            var console = Output.Create(settings.Plain);
            console.MarkupLine(FormattableString.Invariant($"Checked {bundlePaths.Count} Sealed Bundle(s)."));
            Output.PrintFindings(console, findings);
        }

        return findings.Any(f => f.Severity == Severity.Error) ? 1 : 0;
    }
}
