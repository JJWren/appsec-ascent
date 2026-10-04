using System.Globalization;
using System.Text;
using Ascent.Core.Errors;
using Ascent.Core.Platform;

namespace Ascent.Integrations;

/// <summary>A Deliverable the Learner opted into the portfolio (PORT-03).</summary>
/// <param name="Id">The Deliverable.</param>
/// <param name="Title">Its title.</param>
public sealed record PortfolioDeliverable(string Id, string Title);

/// <summary>
/// What the portfolio shows (PORT-01). It deliberately has no room for Teach-backs, per-question data, scores beyond
/// pass/fail badges, Sealed content or reference answers (PORT-02).
/// </summary>
/// <param name="Rank">The Rank's name.</param>
/// <param name="Badges">Badge names.</param>
/// <param name="DomainsCleared">Domains cleared.</param>
/// <param name="ExamReady">True when both Simulation forms passed on the first attempt.</param>
/// <param name="Deliverables">Deliverables the Learner included.</param>
/// <param name="Generated">The date written into the file.</param>
public sealed record PortfolioFacts(string Rank, IReadOnlyList<string> Badges, IReadOnlyList<string> DomainsCleared, bool ExamReady, IReadOnlyList<PortfolioDeliverable> Deliverables, DateOnly Generated);

/// <summary>
/// Writes <c>portfolio/PORTFOLIO.md</c> and copies opted-in Deliverables into <c>portfolio/deliverables/</c>, for the
/// Learner to commit to their fork (PORT-01..03). This assembly can't reference Sealing, so it can't read Sealed content.
/// </summary>
public sealed class PortfolioExporter
{
    private readonly string repoRoot;

    /// <summary>Creates the exporter for a repository.</summary>
    public PortfolioExporter(string repoRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        this.repoRoot = repoRoot;
    }

    /// <summary><c>portfolio/</c>.</summary>
    public string Folder => Path.Join(repoRoot, "portfolio");

    /// <summary>The portfolio's path for an included Deliverable.</summary>
    public string DeliverablePath(string deliverableId) => SafePath.Resolve(Folder, "deliverables/" + deliverableId + ".yaml");

    /// <summary>Copies the Learner's own Deliverable from the workspace into the portfolio (PORT-03).</summary>
    public string Include(string deliverableId, string workPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workPath);
        if (!File.Exists(workPath))
        {
            throw new UsageException("There's no work for '" + SafeText.Sanitize(deliverableId, 60) + "' yet.", "Write it with 'ascent deliver " + deliverableId + "'.");
        }

        var target = DeliverablePath(deliverableId);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        SafePath.EnsureNoLinks(Folder, target);
        File.Copy(workPath, target, overwrite: true);
        return target;
    }

    /// <summary>Writes <c>PORTFOLIO.md</c> and returns its path (PORT-01).</summary>
    public string Write(PortfolioFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        static string List(IReadOnlyList<string> items) => items.Count == 0 ? "none yet" : string.Join(", ", items.Select(i => SafeText.Sanitize(i, 100)));

        var markdown = new StringBuilder()
            .Append("# AppSec Ascent portfolio\n\n")
            .Append("_Written by the AppSec Ascent engine on ").Append(facts.Generated.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append("._\n\n")
            .Append("- **Rank:** ").Append(SafeText.Sanitize(facts.Rank, 100)).Append('\n')
            .Append("- **Exam Ready:** ").Append(facts.ExamReady ? "yes" : "no").Append('\n')
            .Append("- **Domains cleared:** ").Append(List(facts.DomainsCleared)).Append('\n')
            .Append("- **Badges:** ").Append(List(facts.Badges)).Append('\n');
        if (facts.Deliverables.Count > 0)
        {
            markdown.Append("\n## Deliverables\n\n");
            foreach (var deliverable in facts.Deliverables.OrderBy(d => d.Id, StringComparer.Ordinal))
            {
                markdown.Append("- [").Append(SafeText.Sanitize(deliverable.Title, 200).Replace("[", "\\[", StringComparison.Ordinal).Replace("]", "\\]", StringComparison.Ordinal)).Append("](deliverables/")
                    .Append(Uri.EscapeDataString(deliverable.Id)).Append(".yaml)\n");
            }
        }

        markdown.Append("\n---\n\nAppSec Ascent is an unofficial study companion for the ISC2 CSSLP® exam. ")
            .Append("CSSLP is a registered certification mark of ISC2, Inc. This portfolio isn't affiliated with or endorsed by ISC2.\n");
        Directory.CreateDirectory(Folder);
        var path = Path.Join(Folder, "PORTFOLIO.md");
        SafePath.EnsureNoLinks(repoRoot, path);
        File.WriteAllText(path, markdown.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }
}
