using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Ascent.Content.Loading;
using Ascent.Content.Model;
using static System.FormattableString;

namespace Ascent.Content.Rules;

/// <summary>QST rules: Quests (one Markdown lesson per Objective).</summary>
public static partial class QuestRules
{
    private const int MinMinutes = 15;
    private const int MaxMinutes = 180;

    private static readonly HashSet<string> UncitedSections = new(StringComparer.OrdinalIgnoreCase)
    {
        "Activities", "Teach-back", "Sources", "Next steps",
    };

    /// <summary>All QST rules.</summary>
    public static IReadOnlyList<IContentRule> All { get; } =
    [
        new Rule("QST-01", "Each Objective has exactly one Quest", OnePerObjective),
        new Rule("QST-02", "Every lesson section cites a source, and every citation marker resolves", SectionsCited),
        new Rule("QST-03", "Primary citations have a URL and access date; paid sources are see-also only", CitationQuality),
        new Rule("QST-04", "Each Quest has at least one existing activity", Activities),
        new Rule("QST-05", "Each Domain's AI-guidance topics are covered by its AI Lens", AiLensCoverage),
        new Rule("QST-06", "Estimated minutes are realistic", Minutes),
        new Rule("QST-07", "Domain, release and folder agree with the Objective", Placement),
    ];

    private static IEnumerable<ContentDocument> Quests(RuleContext context) =>
        context.Index.OfKind(DocumentKind.Quest).Where(q => q.IsSchemaValid);

    private static IEnumerable<Finding> OnePerObjective(RuleContext context)
    {
        if (context.Index.Outline is not { } outline)
        {
            yield break;
        }

        var byObjective = Quests(context)
            .Where(q => JsonRead.Str(q.Data, "outlineVersion") == outline.Version)
            .GroupBy(q => JsonRead.Str(q.Data, "objectiveId") ?? string.Empty, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        foreach (var domain in outline.Domains)
        {
            foreach (var objective in domain.Objectives)
            {
                if (!byObjective.TryGetValue(objective.Id, out var quests))
                {
                    if (context.PackStarted(domain.PackKey))
                    {
                        yield return Finding.Of("QST-01", context.AtPackExit(domain.PackKey), outline.RelativePath, "Objective " + objective.Id + " has no Quest yet.");
                    }
                }
                else if (quests.Count > 1)
                {
                    foreach (var quest in quests)
                    {
                        yield return Finding.Of("QST-01", Severity.Error, quest.RelativePath, "Objective " + objective.Id + " has more than one Quest.");
                    }
                }
            }
        }
    }

    private static IEnumerable<Finding> SectionsCited(RuleContext context)
    {
        foreach (var quest in Quests(context))
        {
            var known = JsonRead.Arr(quest.Data, "citations")?.OfType<JsonObject>()
                .Select(c => JsonRead.Str(c, "id")).OfType<string>().ToHashSet(StringComparer.Ordinal) ?? [];

            foreach (var section in quest.Markdown!.Sections())
            {
                var markers = CitationMarker().Matches(section.Text).Select(m => m.Groups["id"].Value).ToList();
                if (markers.Count == 0 && !UncitedSections.Contains(section.Heading))
                {
                    yield return Finding.Of("QST-02", Severity.Error, quest.RelativePath, "Section '" + section.Heading + "' has no citation marker such as [^c1].", section.Line);
                }

                foreach (var marker in markers.Where(m => !known.Contains(m)).Distinct(StringComparer.Ordinal))
                {
                    yield return Finding.Of("QST-02", Severity.Error, quest.RelativePath, "Citation marker [^" + marker + "] has no matching citation in the front matter.", section.Line);
                }
            }
        }
    }

    private static IEnumerable<Finding> CitationQuality(RuleContext context)
    {
        foreach (var quest in Quests(context))
        {
            foreach (var citation in JsonRead.Arr(quest.Data, "citations")?.OfType<JsonObject>() ?? [])
            {
                var id = JsonRead.Str(citation, "id");
                var role = JsonRead.Str(citation, "role");
                if (role == "primary" && (JsonRead.Str(citation, "url") is null || JsonRead.Str(citation, "accessed") is null))
                {
                    yield return Finding.Of("QST-03", Severity.Error, quest.RelativePath, "Primary citation " + id + " needs both a url and an accessed date.");
                }

                if (JsonRead.Str(citation, "kind") == "book" && role != "seeAlso")
                {
                    yield return Finding.Of("QST-03", Severity.Error, quest.RelativePath, "Citation " + id + " is a book; paid sources may only be seeAlso.",
                        hint: "Cite a free primary source for the claim and keep the book as seeAlso.");
                }
            }
        }
    }

    private static IEnumerable<Finding> Activities(RuleContext context)
    {
        var labs = ContentFacts.IdsOf(context.Index, DocumentKind.LabManifest);
        var drills = ContentFacts.IdsOf(context.Index, DocumentKind.Drill);
        var deliverables = ContentFacts.IdsOf(context.Index, DocumentKind.DeliverableTemplate);

        foreach (var quest in Quests(context))
        {
            var activities = JsonRead.Obj(quest.Data, "activities");
            var references = new (string Kind, IReadOnlyList<string> Ids, ISet<string> Known)[]
            {
                ("Lab", JsonRead.Strings(JsonRead.Arr(activities, "labs")), labs),
                ("Drill", JsonRead.Strings(JsonRead.Arr(activities, "drills")), drills),
                ("Deliverable", JsonRead.Strings(JsonRead.Arr(activities, "deliverables")), deliverables),
            };

            if (references.Sum(r => r.Ids.Count) == 0)
            {
                yield return Finding.Of("QST-04", Severity.Error, quest.RelativePath, "The Quest has no Lab, Drill or Deliverable.");
            }

            foreach (var (kind, ids, known) in references)
            {
                foreach (var id in ids.Where(id => !known.Contains(id)))
                {
                    yield return Finding.Of("QST-04", Severity.Error, quest.RelativePath, kind + " '" + id + "' does not exist.");
                }
            }
        }
    }

    private static IEnumerable<Finding> AiLensCoverage(RuleContext context)
    {
        if (context.Index.Outline is not { } outline)
        {
            yield break;
        }

        foreach (var domain in outline.Domains)
        {
            var quests = ContentFacts.QuestsInPack(context.Index, domain.PackKey).ToList();
            var referenced = quests
                .SelectMany(q => JsonRead.Strings(JsonRead.Arr(JsonRead.Obj(q.Data, "aiLens"), "topics")).Select(topic => (Quest: q, Topic: topic)))
                .ToList();
            var known = domain.AiGuidanceTopics.Select(t => t.Key).ToHashSet(StringComparer.Ordinal);

            foreach (var (quest, topic) in referenced.Where(r => !known.Contains(r.Topic)))
            {
                yield return Finding.Of("QST-05", Severity.Error, quest.RelativePath, "AI Lens topic '" + topic + "' is not an AI-guidance topic of " + domain.Id + ".");
            }

            if (!context.PackStarted(domain.PackKey))
            {
                continue;
            }

            var covered = referenced.Select(r => r.Topic).ToHashSet(StringComparer.Ordinal);
            foreach (var topic in domain.AiGuidanceTopics.Where(t => !covered.Contains(t.Key)))
            {
                yield return Finding.Of("QST-05", context.AtPackExit(domain.PackKey), outline.RelativePath,
                    "No " + domain.Id + " Quest covers AI-guidance topic '" + topic.Key + "' in its AI Lens.");
            }
        }
    }

    private static IEnumerable<Finding> Minutes(RuleContext context) =>
        Quests(context)
            .Where(q => JsonRead.WholeNumber(q.Data, "estimatedMinutes") is { } minutes && (minutes < MinMinutes || minutes > MaxMinutes))
            .Select(q => Finding.Of("QST-06", Severity.Warning, q.RelativePath,
                Invariant($"estimatedMinutes should be between {MinMinutes} and {MaxMinutes}.")));

    private static IEnumerable<Finding> Placement(RuleContext context)
    {
        foreach (var quest in Quests(context))
        {
            var objectiveId = JsonRead.Str(quest.Data, "objectiveId") ?? string.Empty;
            var examDomain = JsonRead.Str(quest.Data, "examDomain");
            var release = JsonRead.WholeNumber(quest.Data, "release");
            var expectedDomain = ExpectedDomain(objectiveId);
            var folderPack = ContentFacts.PackForQuestPath(quest.RelativePath);

            if (examDomain != expectedDomain)
            {
                yield return Finding.Of("QST-07", Severity.Error, quest.RelativePath, "examDomain should be " + expectedDomain + " for Objective " + objectiveId + ".");
            }

            if (release != ExpectedRelease(expectedDomain))
            {
                yield return Finding.Of("QST-07", Severity.Error, quest.RelativePath,
                    Invariant($"release should be {ExpectedRelease(expectedDomain)} for {expectedDomain} Quests."));
            }

            if (folderPack != ContentFacts.PackForExamDomain(expectedDomain))
            {
                yield return Finding.Of("QST-07", Severity.Error, quest.RelativePath, "The Quest is not in its pack's folder (expected curriculum/" +
                    (ContentFacts.PackForExamDomain(expectedDomain) ?? "?") + "...).");
            }

            if (context.Index.Outline is { } outline && expectedDomain.StartsWith('D') && outline.FindObjective(objectiveId) is null)
            {
                yield return Finding.Of("QST-07", Severity.Error, quest.RelativePath, "Objective " + objectiveId + " is not in outline " + outline.Version + ".");
            }
        }
    }

    private static string ExpectedDomain(string objectiveId) =>
        objectiveId.StartsWith("ORI-", StringComparison.Ordinal) ? "ORI"
        : objectiveId.StartsWith("CAP-", StringComparison.Ordinal) ? "CAP"
        : "D" + objectiveId[..1];

    private static int ExpectedRelease(string examDomain) => examDomain switch
    {
        "ORI" => 0,
        "CAP" => 9,
        _ => examDomain[1] - '0',
    };

    [GeneratedRegex(@"\[\^(?<id>c\d+)\]", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex CitationMarker();
}
