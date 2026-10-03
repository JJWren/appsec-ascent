using System.Text.RegularExpressions;
using Ascent.Content.Loading;
using Ascent.Content.Model;

namespace Ascent.Content.Rules;

/// <summary>ID rules: identifier formats, uniqueness, spoiler-safety and trademark safety.</summary>
public static partial class IdRules
{
    private static readonly HashSet<string> SpoilerWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "sqli", "sql", "injection", "inject", "xss", "csrf", "ssrf", "idor", "xxe", "rce", "lfi", "rfi", "traversal",
        "deserialization", "deserialize", "overflow", "bypass", "backdoor", "leak", "leaky", "exfil", "exfiltration",
        "hardcoded", "plaintext", "misconfig", "misconfigured", "insecure", "vuln", "vulnerable", "exploit", "poison",
        "poisoned", "poisoning", "jailbreak", "secret", "secrets", "password", "unauthenticated", "unvalidated",
    };

    private static readonly string[] ScannedRoots = ["curriculum/", "labs/", "deliverables/", "sealed/"];

    /// <summary>All ID rules.</summary>
    public static IReadOnlyList<IContentRule> All { get; } =
    [
        new Rule("ID-01", "IDs follow the naming patterns and match their file names", Patterns),
        new Rule("ID-02", "IDs are unique across the repository", Unique),
        new Rule("ID-03", "IDs and file names never describe a vulnerability", SpoilerSafe),
        new Rule("ID-04", "No identifier, file, project or package name contains the exam mark", TrademarkSafe),
    ];

    private static IEnumerable<Finding> Patterns(RuleContext context)
    {
        foreach (var document in context.Index.Documents.Where(d => d.Id is not null))
        {
            var id = document.Id!;
            var (pattern, expectedFile) = document.Kind switch
            {
                DocumentKind.Quest => (QuestId(), id + ".md"),
                DocumentKind.Drill => (DrillId(), id + ".yaml"),
                DocumentKind.LabManifest or DocumentKind.LabBrief => (LabId(), null),
                DocumentKind.DeliverableTemplate => (DeliverableId(), id + ".yaml"),
                DocumentKind.Rubric => (RubricId(), id + ".yaml"),
                DocumentKind.SealedQuestion => (QuestionId(), id + ".yaml"),
                DocumentKind.Bundle => (BundleItemId(), (string?)null),
                _ => ((Regex?)null, (string?)null),
            };

            if (pattern is not null && !pattern.IsMatch(id))
            {
                yield return Finding.Of("ID-01", Severity.Error, document.RelativePath, "ID '" + id + "' does not follow the " + document.Kind + " naming pattern.");
            }

            if (expectedFile is not null && !string.Equals(document.FileName, expectedFile, StringComparison.Ordinal))
            {
                yield return Finding.Of("ID-01", Severity.Error, document.RelativePath, "The file should be named " + expectedFile + " to match its ID.");
            }

            if (document.Kind is DocumentKind.LabManifest or DocumentKind.LabBrief)
            {
                var folder = document.RelativePath.Split('/')[1];
                if (!string.Equals(folder, id, StringComparison.Ordinal))
                {
                    yield return Finding.Of("ID-01", Severity.Error, document.RelativePath, "The Lab folder should be named " + id + " to match its ID.");
                }
            }
        }
    }

    private static IEnumerable<Finding> Unique(RuleContext context)
    {
        var declarations = context.Index.Documents
            .Where(d => d.Id is not null && d.Kind != DocumentKind.LabBrief)
            .GroupBy(d => d.Id!, StringComparer.Ordinal)
            .Where(group => group.Count() > 1);

        foreach (var group in declarations)
        {
            foreach (var document in group)
            {
                yield return Finding.Of("ID-02", Severity.Error, document.RelativePath, "ID '" + group.Key + "' is declared more than once.");
            }
        }
    }

    private static IEnumerable<Finding> SpoilerSafe(RuleContext context)
    {
        foreach (var path in context.Index.RepoFiles.Where(p => ScannedRoots.Any(root => p.StartsWith(root, StringComparison.Ordinal))))
        {
            var word = Tokens(path).FirstOrDefault(SpoilerWords.Contains);
            if (word is not null)
            {
                yield return Finding.Of("ID-03", Severity.Error, path, "The path contains the word '" + word + "', which could reveal a Lab's vulnerability or answer.",
                    hint: "Use catalog IDs only (for example lab-d5-01).");
            }
        }
    }

    private static IEnumerable<Finding> TrademarkSafe(RuleContext context)
    {
        foreach (var path in context.Index.RepoFiles.Where(p => p.Contains("csslp", StringComparison.OrdinalIgnoreCase)))
        {
            yield return Finding.Of("ID-04", Severity.Error, path, "File and folder names must not contain the exam mark.");
        }

        foreach (var project in context.Index.RepoFiles.Where(p => p.EndsWith(".csproj", StringComparison.Ordinal)))
        {
            var text = context.Index.ReadText(project) ?? string.Empty;
            foreach (Match match in ProjectNameProperty().Matches(text))
            {
                if (match.Groups["value"].Value.Contains("csslp", StringComparison.OrdinalIgnoreCase))
                {
                    yield return Finding.Of("ID-04", Severity.Error, project, match.Groups["name"].Value + " must not contain the exam mark.");
                }
            }
        }

        foreach (var document in context.Index.Documents.Where(d => d.Id?.Contains("csslp", StringComparison.OrdinalIgnoreCase) == true))
        {
            yield return Finding.Of("ID-04", Severity.Error, document.RelativePath, "IDs must not contain the exam mark.");
        }
    }

    private static string[] Tokens(string path) =>
        path.Split(['/', '-', '_', '.'], StringSplitOptions.RemoveEmptyEntries);

    [GeneratedRegex(@"^q-([1-8]\.\d{1,2}|ori-\d+|cap-\d+)$", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex QuestId();

    [GeneratedRegex(@"^lab-(d[1-8]|ori|cap)-\d{2}$", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex LabId();

    [GeneratedRegex(@"^drl-(d[1-8]|ori|cap)-\d{2}$", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex DrillId();

    [GeneratedRegex(@"^dlv-(d[1-8]|ori|cap)-\d{2}$", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex DeliverableId();

    [GeneratedRegex(@"^rub-(d[1-8]|ori|cap)-\d{2}$", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex RubricId();

    [GeneratedRegex(@"^qb-[1-8]\.\d{1,2}-\d{3}$", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex QuestionId();

    [GeneratedRegex(
        @"^(qb-[1-8]\.\d{1,2}-\d{3}|lab-(d[1-8]|ori|cap)-\d{2}\.(module|tests|fix|plant)|dlv-(d[1-8]|ori|cap)-\d{2}\.reference|drl-(d[1-8]|ori|cap)-\d{2}\.answer|dd-[1-9]\.solution|release-([1-8]|capstone))$",
        RegexOptions.CultureInvariant, 1000)]
    private static partial Regex BundleItemId();

    [GeneratedRegex(@"<(?<name>AssemblyName|PackageId|RootNamespace|ToolCommandName)>(?<value>[^<]*)</", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex ProjectNameProperty();
}
