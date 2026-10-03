using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Ascent.Content.Loading;
using Ascent.Content.Model;
using static System.FormattableString;

namespace Ascent.Content.Rules;

/// <summary>
/// QBK rules. Public checks read Sealed Bundle headers only; structural checks run on plaintext in the private
/// sealed repository (<c>ascent lint --sealed-sources</c>). Messages never echo stems or options.
/// </summary>
public static partial class QuestionBankRules
{
    /// <summary>Minimum practice questions per Objective.</summary>
    public const int PracticePerObjective = 15;

    /// <summary>Diagnostic pool size.</summary>
    public const int DiagnosticSize = 40;

    /// <summary>Simulation pool size (two 125-question Simulations).</summary>
    public const int SimulationSize = 250;

    private const double JudgmentShare = 0.30;
    private const int HardPerObjective = 3;

    /// <summary>All QBK rules.</summary>
    public static IReadOnlyList<IContentRule> All { get; } =
    [
        new Rule("QBK-01", "Each Objective has at least 15 practice questions", PracticeCount),
        new Rule("QBK-02", "Question structure matches its type", Structure),
        new Rule("QBK-03", "Every option has a rationale and every question a citation", Rationales),
        new Rule("QBK-04", "Difficulty and judgment framing are balanced", Balance),
        new Rule("QBK-05", "Each AI-guidance topic has at least one question", AiTopicQuestions),
        new Rule("QBK-06", "Questions are attested original (never exam-recalled)", Attestation),
        new Rule("QBK-07", "Diagnostic pool: 40 items weighted by Domain", context => PoolDistribution(context, "QBK-07", "diagnostic", DiagnosticSize, "orientation")),
        new Rule("QBK-08", "Simulation pool: 250 items weighted by Domain", context => PoolDistribution(context, "QBK-08", "simulation", SimulationSize, "capstone")),
    ];

    private static IEnumerable<Finding> PracticeCount(RuleContext context)
    {
        if (context.Index.Outline is not { } outline)
        {
            yield break;
        }

        var counts = ContentFacts.QuestionHeaders(context.Index)
            .Where(h => JsonRead.Str(h, "pool") == "practice")
            .GroupBy(h => JsonRead.Str(h, "objectiveId") ?? string.Empty, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        foreach (var domain in outline.Domains.Where(d => context.PackStarted(d.PackKey)))
        {
            foreach (var objective in domain.Objectives)
            {
                var count = counts.GetValueOrDefault(objective.Id);
                if (count < PracticePerObjective)
                {
                    yield return Finding.Of("QBK-01", context.AtPackExit(domain.PackKey), outline.RelativePath,
                        Invariant($"Objective {objective.Id} has {count} of {PracticePerObjective} practice questions."));
                }
            }
        }
    }

    private static IEnumerable<ContentDocument> Plaintext(RuleContext context) =>
        context.Index.OfKind(DocumentKind.SealedQuestion).Where(q => q.IsSchemaValid);

    private static IEnumerable<Finding> Structure(RuleContext context)
    {
        foreach (var question in Plaintext(context))
        {
            var problem = StructureProblem(question.Data!);
            if (problem is not null)
            {
                yield return Finding.Of("QBK-02", Severity.Error, question.RelativePath, question.Id + ": " + problem);
            }
        }
    }

    private static string? StructureProblem(JsonObject data)
    {
        var options = JsonRead.Arr(data, "options")?.OfType<JsonObject>().ToList() ?? [];
        var keys = options.Select(o => JsonRead.Str(o, "key")).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var answer = data["answer"];
        var answers = answer is JsonArray list ? JsonRead.Strings(list) : JsonRead.Str(data, "answer") is { } single ? [single] : [];

        return JsonRead.Str(data, "type") switch
        {
            "single" when options.Count != 4 => "single-answer questions need exactly 4 options.",
            "single" when answer is not JsonValue || !keys.Contains(answers[0]) => "single-answer questions need exactly one valid answer key.",
            "multi" when options.Count is < 5 or > 6 => "multi-select questions need 5 or 6 options.",
            "multi" when answers.Count is < 2 or > 3 || answers.Any(a => !keys.Contains(a)) => "multi-select questions need 2 or 3 valid answer keys.",
            "ordering" when options.Count is < 4 or > 6 => "ordering questions need 4 to 6 items.",
            "ordering" when answers.Count != options.Count || !answers.ToHashSet(StringComparer.Ordinal).SetEquals(keys) => "the ordering answer must list every item key once.",
            "matching" when options.Count is < 3 or > 5 => "matching questions need 3 to 5 pairs.",
            "matching" when answers.Count != options.Count => "the matching answer must pair every option.",
            _ => null,
        };
    }

    private static IEnumerable<Finding> Rationales(RuleContext context)
    {
        foreach (var question in Plaintext(context))
        {
            var options = JsonRead.Arr(question.Data, "options")?.OfType<JsonObject>() ?? [];
            if (options.Any(o => string.IsNullOrWhiteSpace(JsonRead.Str(o, "rationale"))))
            {
                yield return Finding.Of("QBK-03", Severity.Error, question.RelativePath, question.Id + ": every option needs a rationale.");
            }

            if ((JsonRead.Arr(question.Data, "citations")?.Count ?? 0) == 0)
            {
                yield return Finding.Of("QBK-03", Severity.Error, question.RelativePath, question.Id + ": at least one citation is required.");
            }
        }
    }

    private static IEnumerable<Finding> Balance(RuleContext context)
    {
        var byObjective = Plaintext(context)
            .Where(q => JsonRead.Str(q.Data, "pool") == "practice")
            .GroupBy(q => JsonRead.Str(q.Data, "objectiveId") ?? string.Empty, StringComparer.Ordinal);

        foreach (var group in byObjective.Where(g => g.Count() >= PracticePerObjective))
        {
            var hard = group.Count(q => JsonRead.WholeNumber(q.Data, "difficulty") == 3);
            if (hard < HardPerObjective)
            {
                yield return Finding.Of("QBK-04", Severity.Warning, "sealed-sources:questions/" + group.Key,
                    Invariant($"Objective {group.Key} has {hard} difficulty-3 questions; aim for at least {HardPerObjective}."));
            }

            var singles = group.Where(q => JsonRead.Str(q.Data, "type") == "single").ToList();
            var judged = singles.Count(q => JudgmentWord().IsMatch(JsonRead.Str(q.Data, "stem") ?? string.Empty));
            if (singles.Count > 0 && judged < JudgmentShare * singles.Count)
            {
                yield return Finding.Of("QBK-04", Severity.Warning, "sealed-sources:questions/" + group.Key,
                    Invariant($"Objective {group.Key}: {judged} of {singles.Count} single-answer questions use BEST/FIRST/MOST framing; aim for 30% or more."));
            }
        }
    }

    private static IEnumerable<Finding> AiTopicQuestions(RuleContext context)
    {
        if (context.Index.Outline is not { } outline)
        {
            yield break;
        }

        var topics = ContentFacts.QuestionHeaders(context.Index)
            .Select(h => JsonRead.Str(h, "aiTopic")).OfType<string>().ToHashSet(StringComparer.Ordinal);

        foreach (var domain in outline.Domains.Where(d => context.PackStarted(d.PackKey)))
        {
            foreach (var topic in domain.AiGuidanceTopics.Where(t => !topics.Contains(t.Key)))
            {
                yield return Finding.Of("QBK-05", context.AtPackExit(domain.PackKey), outline.RelativePath,
                    "No question covers " + domain.Id + " AI-guidance topic '" + topic.Key + "'.");
            }
        }
    }

    private static IEnumerable<Finding> Attestation(RuleContext context) =>
        context.Index.OfKind(DocumentKind.SealedQuestion)
            .Where(q => q.Data is not null && JsonRead.Str(q.Data, "attestation") != "original-not-exam-recalled")
            .Select(q => Finding.Of("QBK-06", Severity.Error, q.RelativePath,
                (q.Id ?? q.FileName) + ": missing the originality attestation.", hint: "Set attestation: original-not-exam-recalled."));

    private static IEnumerable<Finding> PoolDistribution(RuleContext context, string ruleId, string pool, int size, string packKey)
    {
        if (context.Index.Outline is not { } outline)
        {
            yield break;
        }

        var items = ContentFacts.QuestionHeaders(context.Index).Where(h => JsonRead.Str(h, "pool") == pool).ToList();
        if (items.Count == 0 && !context.Index.Season.IsPackComplete(packKey))
        {
            yield break;
        }

        var severity = context.AtPackExit(packKey);
        if (items.Count != size)
        {
            yield return Finding.Of(ruleId, severity, outline.RelativePath, Invariant($"The {pool} pool has {items.Count} items; it needs exactly {size}."));
        }

        foreach (var domain in outline.Domains)
        {
            var actual = items.Count(h => JsonRead.Str(h, "examDomain") == domain.Id);
            var ideal = ContentFacts.Ideal(domain, size);
            if (Math.Abs(actual - ideal) > 1)
            {
                yield return Finding.Of(ruleId, severity, outline.RelativePath,
                    Invariant($"The {pool} pool has {actual} {domain.Id} items; the weighted target is {ideal:0.#} (±1)."));
            }
        }
    }

    [GeneratedRegex(@"\b(BEST|FIRST|MOST|PRIMARY|GREATEST|LEAST|NEXT)\b", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex JudgmentWord();
}
