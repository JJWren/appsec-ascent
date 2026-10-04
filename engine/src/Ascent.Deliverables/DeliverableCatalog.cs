using System.Text.Json.Nodes;
using Ascent.Content.Loading;
using Ascent.Core.Errors;
using Ascent.Core.Platform;

namespace Ascent.Deliverables;

/// <summary>A field of a Deliverable section (deliverable schema).</summary>
/// <param name="Key">The key.</param>
/// <param name="Type">text, markdown, list, enum, number, date or reference.</param>
/// <param name="Required">True when it must be filled in.</param>
/// <param name="Options">For enum fields: the allowed values.</param>
public sealed record TemplateField(string Key, string Type, bool Required, IReadOnlyList<string> Options);

/// <summary>A section of a Deliverable template: a list of entries with the same fields.</summary>
/// <param name="Key">The key.</param>
/// <param name="Title">The title.</param>
/// <param name="Required">True when the section must have entries.</param>
/// <param name="MinItems">The fewest entries allowed.</param>
/// <param name="Fields">The fields of each entry.</param>
public sealed record TemplateSection(string Key, string Title, bool Required, int MinItems, IReadOnlyList<TemplateField> Fields);

/// <summary>A Deliverable template (deliverable schema).</summary>
/// <param name="Id">The Deliverable ID.</param>
/// <param name="Title">The title.</param>
/// <param name="ObjectiveIds">Its Objectives.</param>
/// <param name="Sections">Its sections.</param>
/// <param name="RubricId">Its rubric.</param>
/// <param name="ReferenceRef">The Sealed reference answer (tier <c>submitted</c>).</param>
/// <param name="PortfolioEligible">True when it may go into the portfolio (PORT-03).</param>
public sealed record DeliverableTemplate(
    string Id,
    string Title,
    IReadOnlyList<string> ObjectiveIds,
    IReadOnlyList<TemplateSection> Sections,
    string RubricId,
    string ReferenceRef,
    bool PortfolioEligible);

/// <summary>One level of a rubric criterion.</summary>
/// <param name="Score">0–4.</param>
/// <param name="Descriptor">What the level looks like.</param>
public sealed record RubricLevel(int Score, string Descriptor);

/// <summary>A rubric criterion.</summary>
/// <param name="Key">The key.</param>
/// <param name="Description">What it judges.</param>
/// <param name="Weight">Its weight; a rubric's weights sum to 100.</param>
/// <param name="Levels">Its levels.</param>
public sealed record RubricCriterion(string Key, string Description, int Weight, IReadOnlyList<RubricLevel> Levels);

/// <summary>A rubric (rubric schema).</summary>
/// <param name="Id">The rubric.</param>
/// <param name="PassThreshold">The pass mark, in percent.</param>
/// <param name="Criteria">Its criteria.</param>
public sealed record Rubric(string Id, int PassThreshold, IReadOnlyList<RubricCriterion> Criteria);

/// <summary>A drill (drill schema).</summary>
/// <param name="Id">The drill.</param>
/// <param name="ObjectiveId">Its Objective.</param>
/// <param name="Prompt">What to do.</param>
/// <param name="AnswerRef">The Sealed answer key (tier <c>submitted</c>).</param>
public sealed record DrillInfo(string Id, string ObjectiveId, string Prompt, string AnswerRef);

/// <summary>Deliverable templates, rubrics and drills from the Curriculum's schema-valid content.</summary>
public sealed class DeliverableCatalog
{
    private readonly Dictionary<string, DeliverableTemplate> templates;
    private readonly Dictionary<string, Rubric> rubrics;
    private readonly Dictionary<string, DrillInfo> drills;

    private DeliverableCatalog(Dictionary<string, DeliverableTemplate> templates, Dictionary<string, Rubric> rubrics, Dictionary<string, DrillInfo> drills)
    {
        this.templates = templates;
        this.rubrics = rubrics;
        this.drills = drills;
    }

    /// <summary>Reads the catalog from a content index.</summary>
    public static DeliverableCatalog From(ContentIndex index)
    {
        ArgumentNullException.ThrowIfNull(index);
        static IEnumerable<JsonObject> Valid(ContentIndex index, DocumentKind kind) =>
            index.OfKind(kind).Where(d => d.IsSchemaValid && d.Data is not null).Select(d => d.Data!);

        return new DeliverableCatalog(
            Valid(index, DocumentKind.DeliverableTemplate).Select(ReadTemplate).GroupBy(t => t.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal),
            Valid(index, DocumentKind.Rubric).Select(ReadRubric).GroupBy(r => r.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal),
            Valid(index, DocumentKind.Drill).Select(ReadDrill).GroupBy(d => d.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal));
    }

    /// <summary>The Deliverable templates.</summary>
    public IReadOnlyCollection<DeliverableTemplate> Templates => templates.Values;

    /// <summary>True when <paramref name="id"/> names a drill.</summary>
    public bool IsDrill(string id) => drills.ContainsKey(id);

    /// <summary>A template, or a usage error naming the ID.</summary>
    public DeliverableTemplate Template(string id) =>
        templates.GetValueOrDefault(id) ?? throw new UsageException("There is no Deliverable or drill called '" + SafeText.Sanitize(id, 60) + "'.", "Open a Quest to see its activities: ascent next");

    /// <summary>A template's rubric.</summary>
    public Rubric RubricFor(DeliverableTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        return rubrics.GetValueOrDefault(template.RubricId)
            ?? throw new AscentException("The rubric '" + template.RubricId + "' is missing.", "Run 'ascent lint' for details, or report it with 'ascent bug " + template.Id + "'.");
    }

    /// <summary>A drill, or a usage error naming the ID.</summary>
    public DrillInfo Drill(string id) =>
        drills.GetValueOrDefault(id) ?? throw new UsageException("There is no drill called '" + SafeText.Sanitize(id, 60) + "'.", "Open a Quest to see its activities: ascent next");

    private static DeliverableTemplate ReadTemplate(JsonObject data) => new(
        JsonRead.Str(data, "id")!,
        SafeText.Sanitize(JsonRead.Str(data, "title") ?? string.Empty, 200),
        JsonRead.Strings(JsonRead.Arr(data, "objectiveIds")),
        (JsonRead.Arr(data, "sections") ?? []).OfType<JsonObject>().Select(section =>
        {
            var required = JsonRead.Bool(section, "required") == true;
            return new TemplateSection(
                JsonRead.Str(section, "key") ?? string.Empty,
                SafeText.Sanitize(JsonRead.Str(section, "title") ?? string.Empty, 200),
                required,
                JsonRead.WholeNumber(section, "minItems") ?? (required ? 1 : 0),
                (JsonRead.Arr(section, "fields") ?? []).OfType<JsonObject>().Select(field => new TemplateField(
                    JsonRead.Str(field, "key") ?? string.Empty,
                    JsonRead.Str(field, "type") ?? "text",
                    JsonRead.Bool(field, "required") == true,
                    JsonRead.Strings(JsonRead.Arr(field, "options")))).ToList());
        }).ToList(),
        JsonRead.Str(data, "rubricId") ?? string.Empty,
        JsonRead.Str(data, "referenceRef") ?? string.Empty,
        JsonRead.Bool(data, "portfolioEligible") == true);

    private static Rubric ReadRubric(JsonObject data) => new(
        JsonRead.Str(data, "id")!,
        JsonRead.WholeNumber(data, "passThreshold") ?? 70,
        (JsonRead.Arr(data, "criteria") ?? []).OfType<JsonObject>().Select(criterion => new RubricCriterion(
            JsonRead.Str(criterion, "key") ?? string.Empty,
            SafeText.Sanitize(JsonRead.Str(criterion, "description") ?? string.Empty, 400),
            JsonRead.WholeNumber(criterion, "weight") ?? 0,
            (JsonRead.Arr(criterion, "levels") ?? []).OfType<JsonObject>()
                .Select(level => new RubricLevel(JsonRead.WholeNumber(level, "score") ?? 0, SafeText.Sanitize(JsonRead.Str(level, "descriptor") ?? string.Empty, 400)))
                .OrderBy(level => level.Score)
                .ToList())).ToList());

    private static DrillInfo ReadDrill(JsonObject data) => new(
        JsonRead.Str(data, "id")!,
        JsonRead.Str(data, "objectiveId") ?? string.Empty,
        SafeText.Sanitize(JsonRead.Str(data, "prompt") ?? string.Empty, 4000),
        JsonRead.Str(data, "answerRef") ?? string.Empty);
}
