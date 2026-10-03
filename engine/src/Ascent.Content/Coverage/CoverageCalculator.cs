using Ascent.Content.Loading;
using Ascent.Content.Model;
using Ascent.Content.Rules;

namespace Ascent.Content.Coverage;

/// <summary>Coverage of one Objective. Counts only, never content.</summary>
public sealed record ObjectiveCoverage(
    string ObjectiveId,
    string DomainId,
    int Quests,
    int Labs,
    int Drills,
    int Deliverables,
    int Practice,
    int Diagnostic,
    int Simulation)
{
    /// <summary>True when the Objective meets the pack-exit bar: one Quest, an activity, and enough practice questions.</summary>
    public bool IsComplete => Quests == 1 && Labs + Drills + Deliverables > 0 && Practice >= QuestionBankRules.PracticePerObjective;
}

/// <summary>Coverage of one Domain.</summary>
public sealed record DomainCoverage(
    string DomainId,
    string PackKey,
    string Name,
    int Weight,
    int Objectives,
    int ObjectivesComplete,
    int AiTopics,
    int AiTopicsCovered,
    double DiagnosticTarget,
    int DiagnosticActual,
    double SimulationTarget,
    int SimulationActual);

/// <summary>Coverage of a whole outline version.</summary>
public sealed record CoverageReport(string OutlineVersion, IReadOnlyList<ObjectiveCoverage> Objectives, IReadOnlyList<DomainCoverage> Domains)
{
    /// <summary>Total Objectives in the outline.</summary>
    public int ObjectivesTotal => Objectives.Count;

    /// <summary>Objectives meeting the pack-exit bar.</summary>
    public int ObjectivesComplete => Objectives.Count(o => o.IsComplete);

    /// <summary>Total AI-guidance topics.</summary>
    public int AiTopicsTotal => Domains.Sum(d => d.AiTopics);

    /// <summary>AI-guidance topics covered by both an AI Lens section and at least one question.</summary>
    public int AiTopicsCovered => Domains.Sum(d => d.AiTopicsCovered);
}

/// <summary>Computes coverage from Quests and Sealed Bundle headers (never decrypts anything).</summary>
public static class CoverageCalculator
{
    /// <summary>Calculates coverage for the given outline version, or the active outline.</summary>
    public static CoverageReport? Calculate(ContentIndex index, string? outlineVersion = null)
    {
        var outline = outlineVersion is null
            ? index.Outline
            : index.Outlines.FirstOrDefault(o => string.Equals(o.Version, outlineVersion, StringComparison.Ordinal));
        if (outline is null)
        {
            return null;
        }

        var quests = index.OfKind(DocumentKind.Quest)
            .Where(q => q.IsSchemaValid && JsonRead.Str(q.Data, "outlineVersion") == outline.Version)
            .ToList();
        var questions = ContentFacts.QuestionHeaders(index).ToList();
        var known = new Dictionary<string, ISet<string>>(StringComparer.Ordinal)
        {
            ["labs"] = ContentFacts.IdsOf(index, DocumentKind.LabManifest),
            ["drills"] = ContentFacts.IdsOf(index, DocumentKind.Drill),
            ["deliverables"] = ContentFacts.IdsOf(index, DocumentKind.DeliverableTemplate),
        };

        var objectives = outline.Objectives.Select(objective => ForObjective(objective, quests, questions, known)).ToList();
        var domains = outline.Domains.Select(domain => ForDomain(domain, objectives, quests, questions)).ToList();
        return new CoverageReport(outline.Version, objectives, domains);
    }

    /// <summary>Unmet pack-exit items for a pack ("d1"…"d8"); empty when the pack is ready.</summary>
    public static IReadOnlyList<string> GateGaps(CoverageReport report, string packKey)
    {
        var domain = report.Domains.FirstOrDefault(d => string.Equals(d.PackKey, packKey, StringComparison.Ordinal));
        if (domain is null)
        {
            return ["Unknown pack '" + packKey + "'. Use d1…d8."];
        }

        var gaps = report.Objectives
            .Where(o => o.DomainId == domain.DomainId && !o.IsComplete)
            .Select(o => "Objective " + o.ObjectiveId + " is not complete.")
            .ToList();
        if (domain.AiTopicsCovered < domain.AiTopics)
        {
            gaps.Add(FormattableString.Invariant($"{domain.AiTopics - domain.AiTopicsCovered} AI-guidance topic(s) are not covered."));
        }

        return gaps;
    }

    private static ObjectiveCoverage ForObjective(
        ObjectiveDef objective, List<ContentDocument> quests, List<System.Text.Json.Nodes.JsonObject> questions, Dictionary<string, ISet<string>> known)
    {
        var mine = quests.Where(q => JsonRead.Str(q.Data, "objectiveId") == objective.Id).ToList();
        int Count(string kind) => mine
            .SelectMany(q => JsonRead.Strings(JsonRead.Arr(JsonRead.Obj(q.Data, "activities"), kind)))
            .Distinct(StringComparer.Ordinal)
            .Count(known[kind].Contains);
        int Pool(string pool) => questions.Count(h => JsonRead.Str(h, "objectiveId") == objective.Id && JsonRead.Str(h, "pool") == pool);

        return new ObjectiveCoverage(objective.Id, objective.DomainId, mine.Count, Count("labs"), Count("drills"), Count("deliverables"),
            Pool("practice"), Pool("diagnostic"), Pool("simulation"));
    }

    private static DomainCoverage ForDomain(
        ExamDomainDef domain, List<ObjectiveCoverage> objectives, List<ContentDocument> quests, List<System.Text.Json.Nodes.JsonObject> questions)
    {
        var lensTopics = quests
            .Where(q => JsonRead.Str(q.Data, "examDomain") == domain.Id)
            .SelectMany(q => JsonRead.Strings(JsonRead.Arr(JsonRead.Obj(q.Data, "aiLens"), "topics")))
            .ToHashSet(StringComparer.Ordinal);
        var questionTopics = questions.Select(h => JsonRead.Str(h, "aiTopic")).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var mine = objectives.Where(o => o.DomainId == domain.Id).ToList();

        return new DomainCoverage(
            domain.Id,
            domain.PackKey,
            domain.Name,
            domain.Weight,
            mine.Count,
            mine.Count(o => o.IsComplete),
            domain.AiGuidanceTopics.Count,
            domain.AiGuidanceTopics.Count(t => lensTopics.Contains(t.Key) && questionTopics.Contains(t.Key)),
            ContentFacts.Ideal(domain, QuestionBankRules.DiagnosticSize),
            questions.Count(h => JsonRead.Str(h, "pool") == "diagnostic" && JsonRead.Str(h, "examDomain") == domain.Id),
            ContentFacts.Ideal(domain, QuestionBankRules.SimulationSize),
            questions.Count(h => JsonRead.Str(h, "pool") == "simulation" && JsonRead.Str(h, "examDomain") == domain.Id));
    }
}
