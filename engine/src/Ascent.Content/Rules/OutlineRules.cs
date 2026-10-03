using Ascent.Content.Model;
using static System.FormattableString;

namespace Ascent.Content.Rules;

/// <summary>OUT rules: the exam outline itself.</summary>
public static class OutlineRules
{
    private const int MaxTitleLength = 70;

    private static readonly Dictionary<string, int> ExpectedObjectives20230915 = new(StringComparer.Ordinal)
    {
        ["D1"] = 2,
        ["D2"] = 9,
        ["D3"] = 8,
        ["D4"] = 7,
        ["D5"] = 6,
        ["D6"] = 8,
        ["D7"] = 13,
        ["D8"] = 5,
    };

    /// <summary>All OUT rules.</summary>
    public static IReadOnlyList<IContentRule> All { get; } =
    [
        new Rule("OUT-01", "Domain weights sum to 100", WeightsSumTo100),
        new Rule("OUT-02", "Objective structure matches the outline", ObjectiveStructure),
        new Rule("OUT-03", "Objective titles are short paraphrases with a source", ShortTitles),
        new Rule("OUT-04", "Every Domain lists AI-guidance topics with a source", AiTopics),
    ];

    private static IEnumerable<Finding> WeightsSumTo100(RuleContext context)
    {
        foreach (var outline in context.Index.Outlines)
        {
            var total = outline.Domains.Sum(domain => domain.Weight);
            if (total != 100)
            {
                yield return Finding.Of("OUT-01", Severity.Error, outline.RelativePath, Invariant($"Domain weights sum to {total}, not 100."));
            }
        }
    }

    private static IEnumerable<Finding> ObjectiveStructure(RuleContext context)
    {
        foreach (var outline in context.Index.Outlines)
        {
            foreach (var finding in CheckDomainPrefixes(outline))
            {
                yield return finding;
            }

            var duplicates = outline.Objectives.GroupBy(o => o.Id, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key);
            foreach (var duplicate in duplicates)
            {
                yield return Finding.Of("OUT-02", Severity.Error, outline.RelativePath, "Objective " + duplicate + " appears more than once.");
            }

            if (outline.Version == "2023-09-15")
            {
                foreach (var finding in CheckKnownCounts(outline))
                {
                    yield return finding;
                }
            }
        }
    }

    private static IEnumerable<Finding> CheckDomainPrefixes(Outline outline)
    {
        foreach (var domain in outline.Domains)
        {
            var prefix = domain.Number.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".";
            foreach (var objective in domain.Objectives.Where(o => !o.Id.StartsWith(prefix, StringComparison.Ordinal)))
            {
                yield return Finding.Of("OUT-02", Severity.Error, outline.RelativePath,
                    "Objective " + objective.Id + " is listed under " + domain.Id + " but does not start with " + prefix);
            }
        }
    }

    private static IEnumerable<Finding> CheckKnownCounts(Outline outline)
    {
        if (outline.Domains.Count != 8)
        {
            yield return Finding.Of("OUT-02", Severity.Error, outline.RelativePath, Invariant($"Expected 8 Domains, found {outline.Domains.Count}."));
        }

        foreach (var (domainId, expected) in ExpectedObjectives20230915)
        {
            var actual = outline.FindDomain(domainId)?.Objectives.Count ?? 0;
            if (actual != expected)
            {
                yield return Finding.Of("OUT-02", Severity.Error, outline.RelativePath,
                    Invariant($"{domainId} should have {expected} Objectives in the 2023-09-15 outline, found {actual}."));
            }
        }
    }

    private static IEnumerable<Finding> ShortTitles(RuleContext context) =>
        context.Index.Outlines.SelectMany(outline => outline.Objectives
            .Where(objective => objective.Title.Length > MaxTitleLength)
            .Select(objective => Finding.Of("OUT-03", Severity.Warning, outline.RelativePath,
                "Objective " + objective.Id + " has a long title; keep titles to short paraphrased headings.",
                hint: Invariant($"Shorten to {MaxTitleLength} characters or fewer."))));

    private static IEnumerable<Finding> AiTopics(RuleContext context)
    {
        foreach (var outline in context.Index.Outlines)
        {
            foreach (var domain in outline.Domains.Where(d => d.AiGuidanceTopics.Count == 0))
            {
                yield return Finding.Of("OUT-04", Severity.Error, outline.RelativePath, domain.Id + " lists no AI-guidance topics.");
            }

            var duplicateKeys = outline.Domains.SelectMany(d => d.AiGuidanceTopics)
                .GroupBy(t => t.Key, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key);
            foreach (var key in duplicateKeys)
            {
                yield return Finding.Of("OUT-04", Severity.Error, outline.RelativePath, "AI-guidance topic key '" + key + "' is used more than once.");
            }
        }
    }
}
