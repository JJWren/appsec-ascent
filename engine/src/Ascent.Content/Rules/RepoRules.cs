using System.Text.RegularExpressions;
using Ascent.Content.Model;

namespace Ascent.Content.Rules;

/// <summary>REPO rules: repository hygiene and supply-chain safety of workflows.</summary>
public static partial class RepoRules
{
    private static readonly string[] RequiredIgnores = ["aidlc-docs/", ".ascent/", "journal/"];

    /// <summary>All REPO rules.</summary>
    public static IReadOnlyList<IContentRule> All { get; } =
    [
        new Rule("REPO-01", ".gitignore excludes private records and learner state", GitIgnore),
        new Rule("REPO-02", "SECURITY.md defines disclosure and scopes out the Throughline System", SecurityPolicy),
        new Rule("REPO-03", "The Content Bug issue template asks for item and citation IDs", IssueTemplate),
        new Rule("REPO-04", "Workflow actions are pinned by full commit SHA", PinnedActions),
    ];

    private static IEnumerable<Finding> GitIgnore(RuleContext context)
    {
        var lines = (context.Index.ReadText(".gitignore") ?? string.Empty)
            .Split('\n').Select(line => line.Trim()).ToHashSet(StringComparer.Ordinal);
        foreach (var entry in RequiredIgnores.Where(entry => !lines.Contains(entry)))
        {
            yield return Finding.Of("REPO-01", Severity.Error, ".gitignore", "'" + entry + "' must be ignored.");
        }
    }

    private static IEnumerable<Finding> SecurityPolicy(RuleContext context)
    {
        var text = context.Index.ReadText("SECURITY.md");
        if (text is null || !text.Contains("Throughline", StringComparison.Ordinal) || !text.Contains("out of scope", StringComparison.OrdinalIgnoreCase))
        {
            yield return Finding.Of("REPO-02", Severity.Error, "SECURITY.md",
                "SECURITY.md must explain how to report vulnerabilities and state that the deliberately vulnerable Throughline System is out of scope.");
        }
    }

    private static IEnumerable<Finding> IssueTemplate(RuleContext context)
    {
        const string path = ".github/ISSUE_TEMPLATE/content-bug.yml";
        var text = context.Index.ReadText(path);
        var required = new[] { "item", "citation", "exam" };
        if (text is null || required.Any(word => !text.Contains(word, StringComparison.OrdinalIgnoreCase)))
        {
            yield return Finding.Of("REPO-03", Severity.Error, path,
                "The Content Bug template must ask for the item ID and citation and warn against exam-recalled content.");
        }
    }

    private static IEnumerable<Finding> PinnedActions(RuleContext context)
    {
        var workflows = context.Index.RepoFiles.Where(p => p.StartsWith(".github/workflows/", StringComparison.Ordinal)
            && (p.EndsWith(".yml", StringComparison.Ordinal) || p.EndsWith(".yaml", StringComparison.Ordinal)));

        foreach (var workflow in workflows)
        {
            var lines = (context.Index.ReadText(workflow) ?? string.Empty).Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var match = UsesLine().Match(lines[i]);
                if (match.Success && !IsPinned(match.Groups["ref"].Value))
                {
                    yield return Finding.Of("REPO-04", Severity.Error, workflow, "'" + match.Groups["ref"].Value + "' is not pinned by a full commit SHA.", i + 1,
                        "Pin as owner/repo@<40-hex-sha> # vX.Y.Z, or docker://image@sha256:<digest>.");
                }
            }
        }
    }

    private static bool IsPinned(string reference) =>
        reference.StartsWith("./", StringComparison.Ordinal)
        || (reference.StartsWith("docker://", StringComparison.Ordinal) && reference.Contains("@sha256:", StringComparison.Ordinal))
        || ShaPinned().IsMatch(reference);

    [GeneratedRegex(@"^\s*(-\s*)?uses:\s*['""]?(?<ref>[^'""\s#]+)", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex UsesLine();

    [GeneratedRegex(@"^[^@\s]+@[0-9a-f]{40}$", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex ShaPinned();
}
