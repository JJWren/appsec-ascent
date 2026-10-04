using System.Text.Json;
using System.Text.Json.Nodes;
using Ascent.Core.Platform;

namespace Ascent.Assessment.Questions;

/// <summary>One option of a question, with the rationale shown after answering (SU-03).</summary>
/// <param name="Key">A, B, C…</param>
/// <param name="Text">The option text.</param>
/// <param name="Rationale">Why it's right or wrong.</param>
/// <param name="Match">For matching questions: the label it can be matched to.</param>
public sealed record QuestionOption(string Key, string Text, string Rationale, string? Match);

/// <summary>A source a question cites.</summary>
/// <param name="Title">The title.</param>
/// <param name="Publisher">The publisher.</param>
/// <param name="Url">A link, if any.</param>
public sealed record QuestionCitation(string Title, string Publisher, string? Url);

/// <summary>The result of checking an answer.</summary>
/// <param name="Valid">False when the input isn't a well-formed answer for this question type.</param>
/// <param name="Normalized">The canonical form that is stored (P15).</param>
/// <param name="Correct">True when the answer is right.</param>
/// <param name="Error">For invalid input: what's wrong.</param>
public sealed record GradeResult(bool Valid, string Normalized, bool Correct, string? Error);

/// <summary>
/// A decrypted Question Bank item (question schema). Every text field is sanitized on parse (P11), and the answer is
/// only used for grading, never printed before the Learner answers.
/// </summary>
public sealed class Question
{
    private static readonly char[] Separators = [',', ' ', ';', '\t'];

    private Question(string id, string objectiveId, string type, string stem, IReadOnlyList<QuestionOption> options, IReadOnlyList<string> answer, IReadOnlyList<QuestionCitation> citations, string pool)
    {
        Id = id;
        ObjectiveId = objectiveId;
        Type = type;
        Stem = stem;
        Options = options;
        Answer = answer;
        Citations = citations;
        Pool = pool;
    }

    /// <summary>The question ID.</summary>
    public string Id { get; }

    /// <summary>The Objective.</summary>
    public string ObjectiveId { get; }

    /// <summary>single, multi, ordering or matching.</summary>
    public string Type { get; }

    /// <summary>The stem.</summary>
    public string Stem { get; }

    /// <summary>The options, in their authored order.</summary>
    public IReadOnlyList<QuestionOption> Options { get; }

    /// <summary>The correct answer as canonical tokens.</summary>
    public IReadOnlyList<string> Answer { get; }

    /// <summary>The citations.</summary>
    public IReadOnlyList<QuestionCitation> Citations { get; }

    /// <summary>practice, diagnostic or simulation.</summary>
    public string Pool { get; }

    /// <summary>How to answer, for the prompt.</summary>
    public string Instructions => Type switch
    {
        "multi" => "Choose all that apply, separated by commas (for example A,C).",
        "ordering" => "Put every option in order, separated by commas (for example C,A,B,D).",
        "matching" => "Match each option, separated by commas (for example A=2,B=1).",
        _ => "Choose one option (for example B).",
    };

    /// <summary>Parses decrypted question JSON.</summary>
    public static Question Parse(string json)
    {
        var root = JsonNode.Parse(json) as JsonObject ?? throw new JsonException("A question must be a JSON object.");
        var options = (root["options"] as JsonArray ?? [])
            .OfType<JsonObject>()
            .Select(o => new QuestionOption(
                Text(o["key"], 4).ToUpperInvariant(),
                Text(o["text"]),
                Text(o["rationale"]),
                o["match"] is null ? null : Text(o["match"], 200)))
            .ToList();
        var answer = root["answer"] switch
        {
            JsonArray array => array.Select(item => Text(item, 400)).ToList(),
            JsonNode single => [Text(single, 400)],
            _ => [],
        };
        var citations = (root["citations"] as JsonArray ?? [])
            .OfType<JsonObject>()
            .Select(c => new QuestionCitation(Text(c["title"], 400), Text(c["publisher"], 200), c["url"] is null ? null : Text(c["url"], 400)))
            .ToList();
        var question = new Question(
            Text(root["id"], 128),
            Text(root["objectiveId"], 16),
            Text(root["type"], 16) is { Length: > 0 } type ? type : "single",
            Text(root["stem"]),
            options,
            answer,
            citations,
            Text(root["pool"], 16));
        return question.Options.Count > 0 && question.Answer.Count > 0
            ? question
            : throw new JsonException("A question needs options and an answer.");
    }

    /// <summary>Checks an answer. Invalid input is reported so the Learner can try again; nothing is stored for it.</summary>
    public GradeResult Grade(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var keys = Options.Select(o => o.Key).ToHashSet(StringComparer.Ordinal);
        var tokens = input.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => token.ToUpperInvariant())
            .ToList();
        switch (Type)
        {
            case "multi":
                {
                    var chosen = tokens.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
                    if (chosen.Count == 0 || chosen.Any(k => !keys.Contains(k)))
                    {
                        return Invalid("Use option letters, separated by commas.");
                    }

                    return Graded(string.Join(',', chosen), chosen.SequenceEqual(Answer.Select(a => a.ToUpperInvariant()).Order(StringComparer.Ordinal)));
                }

            case "ordering":
                if (tokens.Count != keys.Count || tokens.Distinct(StringComparer.Ordinal).Count() != keys.Count || tokens.Any(k => !keys.Contains(k)))
                {
                    return Invalid("List every option letter exactly once, in order.");
                }

                return Graded(string.Join(',', tokens), tokens.SequenceEqual(Answer.Select(a => a.ToUpperInvariant())));

            case "matching":
                {
                    var pairs = input.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(Pair)
                        .ToList();
                    if (pairs.Count == 0 || pairs.Any(p => p is null || !keys.Contains(p.Value.Key)) || pairs.Select(p => p!.Value.Key).Distinct(StringComparer.Ordinal).Count() != pairs.Count)
                    {
                        return Invalid("Use pairs like A=2, one per option letter.");
                    }

                    var normalized = pairs.Select(p => p!.Value.Key + "=" + p.Value.Value).Order(StringComparer.Ordinal).ToList();
                    var expected = Answer.Select(Pair).OfType<(string Key, string Value)>().Select(p => p.Key + "=" + p.Value).Order(StringComparer.Ordinal);
                    return Graded(string.Join(',', normalized), normalized.SequenceEqual(expected));
                }

            default:
                if (tokens.Count != 1 || !keys.Contains(tokens[0]))
                {
                    return Invalid("Answer with one option letter.");
                }

                return Graded(tokens[0], string.Equals(tokens[0], Answer[0], StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>Grades a stored, already-normalized answer.</summary>
    public bool IsCorrect(string normalized) => Grade(normalized) is { Valid: true, Correct: true };

    private static GradeResult Invalid(string error) => new(false, string.Empty, false, error);

    private static GradeResult Graded(string normalized, bool correct) => new(true, normalized, correct, null);

    private static (string Key, string Value)? Pair(string text)
    {
        var at = text.IndexOf('=', StringComparison.Ordinal);
        return at <= 0 || at == text.Length - 1
            ? null
            : (text[..at].Trim().ToUpperInvariant(), text[(at + 1)..].Trim().ToUpperInvariant());
    }

    private static string Text(JsonNode? node, int maxLength = 4000) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? SafeText.Sanitize(text, maxLength) : string.Empty;
}
