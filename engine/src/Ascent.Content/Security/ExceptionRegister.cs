using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Ascent.Content.Loading;
using Ascent.Content.Model;

namespace Ascent.Content.Security;

/// <summary>A documented, time-limited acceptance of a scanner finding.</summary>
public sealed record RiskAcceptance(string Id, string Scanner, string FindingId, string? Package, string Owner, DateOnly Expires);

/// <summary>
/// Reads <c>security/exceptions.yaml</c>, rejects expired or over-long acceptances, and generates scanner
/// suppression files so there is never a second, hand-maintained list (SEC-U1-03).
/// </summary>
public static class ExceptionRegister
{
    /// <summary>The longest an acceptance may run.</summary>
    public const int MaxDaysAhead = 90;

    /// <summary>Repository path of the register.</summary>
    public const string RegisterPath = "security/exceptions.yaml";

    /// <summary>Schema-valid entries of the register.</summary>
    public static IReadOnlyList<RiskAcceptance> Read(ContentIndex index)
    {
        var document = index.OfKind(DocumentKind.ExceptionRegister).FirstOrDefault(d => d.IsSchemaValid);
        if (document is null)
        {
            return [];
        }

        return
        [
            .. JsonRead.Arr(document.Data, "exceptions")!.OfType<JsonObject>().Select(entry => new RiskAcceptance(
                JsonRead.Str(entry, "id")!,
                JsonRead.Str(entry, "scanner")!,
                JsonRead.Str(entry, "finding")!,
                JsonRead.Str(entry, "package"),
                JsonRead.Str(entry, "owner")!,
                DateOnly.ParseExact(JsonRead.Str(entry, "expires")!, "yyyy-MM-dd", CultureInfo.InvariantCulture))),
        ];
    }

    /// <summary>Findings for a missing, invalid, expired or over-long register entry.</summary>
    public static IReadOnlyList<Finding> Check(ContentIndex index, DateOnly today)
    {
        var findings = index.LoadFindings.Where(f => f.Path == RegisterPath).ToList();
        if (!index.FileExists(RegisterPath))
        {
            findings.Add(Finding.Of("EXC-00", Severity.Error, RegisterPath, "The exception register is missing; create it with 'exceptions: []'."));
            return findings;
        }

        foreach (var entry in Read(index))
        {
            if (entry.Expires < today)
            {
                findings.Add(Finding.Of("EXC-01", Severity.Error, RegisterPath,
                    entry.Id + " expired on " + entry.Expires.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "; fix the finding or renew the acceptance."));
            }
            else if (entry.Expires > today.AddDays(MaxDaysAhead))
            {
                findings.Add(Finding.Of("EXC-02", Severity.Error, RegisterPath,
                    FormattableString.Invariant($"{entry.Id} expires more than {MaxDaysAhead} days from today.")));
            }
        }

        return findings;
    }

    /// <summary>Contents of a <c>.trivyignore</c> file for active Trivy acceptances.</summary>
    public static string TrivyIgnore(IEnumerable<RiskAcceptance> entries, DateOnly today)
    {
        var builder = new StringBuilder("# Generated from security/exceptions.yaml. Do not edit.\n");
        foreach (var entry in Active(entries, today, "trivy"))
        {
            builder.Append("# ").Append(entry.Id).Append(", expires ").Append(entry.Expires.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('\n');
            builder.Append(entry.FindingId).Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>An MSBuild props file with <c>NuGetAuditSuppress</c> items for active NuGet acceptances.</summary>
    public static string NuGetSuppressions(IEnumerable<RiskAcceptance> entries, DateOnly today)
    {
        var items = Active(entries, today, "nuget").Select(entry => new XElement("NuGetAuditSuppress", new XAttribute("Include", AdvisoryUrl(entry.FindingId))));
        var project = new XElement("Project",
            new XComment(" Generated from security/exceptions.yaml. Do not edit. "),
            new XElement("ItemGroup", items));
        return project.ToString() + "\n";
    }

    private static IEnumerable<RiskAcceptance> Active(IEnumerable<RiskAcceptance> entries, DateOnly today, string scanner) =>
        entries.Where(e => e.Scanner == scanner && e.Expires >= today).OrderBy(e => e.Id, StringComparer.Ordinal);

    private static string AdvisoryUrl(string finding) =>
        finding.StartsWith("GHSA-", StringComparison.Ordinal) ? "https://github.com/advisories/" + finding : finding;
}
