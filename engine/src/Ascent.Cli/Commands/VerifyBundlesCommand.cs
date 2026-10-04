using Ascent.Cli.Hosting;
using Ascent.Content.Loading;
using Ascent.Content.Model;
using Ascent.Content.Reporting;
using Ascent.Content.Rules;
using Ascent.Sealing.Bundles;
using Ascent.Sealing.Crypto;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Ascent.Cli.Commands;

/// <summary>
/// <c>ascent verify-bundles</c>: checks Sealed Bundle metadata (BND-02, BND-03) and that every bundle carries a valid
/// maintainer signature (BND-01, P1, P8). It never decrypts anything.
/// </summary>
public sealed class VerifyBundlesCommand(EngineHost host) : Command<RepoSettings>
{
    /// <inheritdoc />
    public override int Execute(CommandContext context, RepoSettings settings, CancellationToken cancellationToken)
    {
        var index = host.Content;
        var bundles = index.OfKind(DocumentKind.Bundle).ToList();
        var bundlePaths = bundles.Select(b => b.RelativePath).ToHashSet(StringComparer.Ordinal);
        var findings = new RuleEngine(BundleRules.All).Run(index)
            .Where(f => bundlePaths.Contains(f.Path))
            .ToList();

        string? fingerprint = null;
        if (SignatureVerifier.HasEmbeddedKey)
        {
            using var verifier = SignatureVerifier.Embedded();
            fingerprint = verifier.Fingerprint;
            findings.AddRange(bundles.SelectMany(bundle => SignatureFindings(verifier, bundle.RelativePath)));
        }
        else if (bundles.Count > 0)
        {
            findings.Add(Finding.Of("BND-01", Severity.Error, "sealed/", "This Engine build has no maintainer key, so no bundle can be verified."));
        }

        findings = findings.OrderBy(f => f.Path, StringComparer.Ordinal).ThenBy(f => f.RuleId, StringComparer.Ordinal).ToList();
        if (settings.Json)
        {
            host.Out.WriteLine(JsonOutput.Serialize(new FindingsReport(findings)));
        }
        else
        {
            host.Renderer.Line(FormattableString.Invariant($"Checked {bundles.Count} Sealed Bundle(s)."));
            host.Renderer.Line("Trusted key (SHA-256): " + (fingerprint ?? "none embedded"));
            Output.PrintFindings(host.OutputConsole(), findings);
        }

        return findings.Any(f => f.Severity == Severity.Error) ? 1 : 0;
    }

    private IEnumerable<Finding> SignatureFindings(SignatureVerifier verifier, string relativePath)
    {
        SealedBundle bundle;
        try
        {
            bundle = SealedBundle.Read(Path.Join(host.Paths.RepoRoot, relativePath));
        }
        catch (BundleFormatException ex)
        {
            return [Finding.Of("BND-01", Severity.Error, relativePath, "The bundle can't be verified: " + ex.Message + ".")];
        }

        if (!bundle.IsSigned)
        {
            return [Finding.Of("BND-01", Severity.Error, relativePath, "The bundle is unsigned.", hint: "Run 'ascent-maint sign' on the maintainer's machine.")];
        }

        return verifier.Verify(bundle.SigningInput(), bundle.Signature)
            ? []
            : [Finding.Of("BND-01", Severity.Error, relativePath, "The signature doesn't verify with the trusted key.")];
    }
}
