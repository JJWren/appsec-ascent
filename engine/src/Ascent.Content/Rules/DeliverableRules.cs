using System.Text.Json.Nodes;
using Ascent.Content.Loading;
using Ascent.Content.Model;
using static System.FormattableString;

namespace Ascent.Content.Rules;

/// <summary>DLV rules: Deliverable templates and rubrics.</summary>
public static class DeliverableRules
{
    private static readonly string[] PersonalDataKeys =
    [
        "name", "full-name", "first-name", "last-name", "patient-name", "employee-id", "username",
    ];

    private static readonly string[] PersonalDataFragments =
    [
        "email", "phone", "address", "dob", "birth", "ssn", "mrn", "passport", "license-number",
    ];

    /// <summary>All DLV rules.</summary>
    public static IReadOnlyList<IContentRule> All { get; } =
    [
        new Rule("DLV-01", "Required template sections define their fields", Sections),
        new Rule("DLV-02", "Rubric weights sum to 100", RubricWeights),
        new Rule("DLV-03", "Rubric and Sealed reference resolve", References),
        new Rule("DLV-04", "Portfolio-eligible templates collect no personal data", NoPersonalData),
    ];

    private static IEnumerable<ContentDocument> Templates(RuleContext context) =>
        context.Index.OfKind(DocumentKind.DeliverableTemplate).Where(t => t.IsSchemaValid);

    private static IEnumerable<JsonObject> AllSections(ContentDocument template) =>
        JsonRead.Arr(template.Data, "sections")?.OfType<JsonObject>() ?? [];

    private static IEnumerable<Finding> Sections(RuleContext context)
    {
        foreach (var template in Templates(context))
        {
            foreach (var section in AllSections(template))
            {
                var fields = JsonRead.Arr(section, "fields")?.OfType<JsonObject>().ToList() ?? [];
                if (JsonRead.Bool(section, "required") == true && fields.Count == 0)
                {
                    yield return Finding.Of("DLV-01", Severity.Error, template.RelativePath, "Required section '" + JsonRead.Str(section, "key") + "' defines no fields.");
                }

                foreach (var field in fields.Where(f => JsonRead.Str(f, "type") == "enum" && (JsonRead.Arr(f, "options")?.Count ?? 0) == 0))
                {
                    yield return Finding.Of("DLV-01", Severity.Error, template.RelativePath, "Enum field '" + JsonRead.Str(field, "key") + "' lists no options.");
                }
            }
        }
    }

    private static IEnumerable<Finding> RubricWeights(RuleContext context) =>
        context.Index.OfKind(DocumentKind.Rubric)
            .Where(r => r.IsSchemaValid)
            .Select(r => (Rubric: r, Total: JsonRead.Arr(r.Data, "criteria")!.OfType<JsonObject>().Sum(c => JsonRead.WholeNumber(c, "weight") ?? 0)))
            .Where(x => x.Total != 100)
            .Select(x => Finding.Of("DLV-02", Severity.Error, x.Rubric.RelativePath, Invariant($"Criterion weights sum to {x.Total}, not 100.")));

    private static IEnumerable<Finding> References(RuleContext context)
    {
        var rubrics = ContentFacts.IdsOf(context.Index, DocumentKind.Rubric);
        var headers = ContentFacts.HeadersById(context.Index);
        foreach (var template in Templates(context))
        {
            var rubricId = JsonRead.Str(template.Data, "rubricId");
            if (rubricId is null || !rubrics.Contains(rubricId))
            {
                yield return Finding.Of("DLV-03", Severity.Error, template.RelativePath, "Rubric '" + rubricId + "' does not exist.");
            }

            var reference = JsonRead.Str(template.Data, "referenceRef");
            var expectedPrefix = template.Id + ".";
            if (reference is null || !reference.StartsWith(expectedPrefix, StringComparison.Ordinal))
            {
                yield return Finding.Of("DLV-03", Severity.Error, template.RelativePath, "referenceRef must start with '" + expectedPrefix + "'.");
            }
            else if (!headers.TryGetValue(reference, out var header)
                     || JsonRead.Str(header, "itemType") != "reference" || JsonRead.Str(header, "tier") != "submitted")
            {
                yield return Finding.Of("DLV-03", Severity.Error, template.RelativePath,
                    "referenceRef '" + reference + "' must resolve to a Sealed Bundle of itemType reference, tier submitted.");
            }
        }
    }

    private static IEnumerable<Finding> NoPersonalData(RuleContext context)
    {
        foreach (var template in Templates(context).Where(t => JsonRead.Bool(t.Data, "portfolioEligible") == true))
        {
            var keys = AllSections(template)
                .SelectMany(s => JsonRead.Arr(s, "fields")?.OfType<JsonObject>() ?? [])
                .Select(f => JsonRead.Str(f, "key"))
                .OfType<string>();

            foreach (var key in keys.Where(IsPersonalData))
            {
                yield return Finding.Of("DLV-04", Severity.Error, template.RelativePath,
                    "Portfolio-eligible template collects '" + key + "', which looks like personal data.");
            }
        }
    }

    private static bool IsPersonalData(string key) =>
        PersonalDataKeys.Contains(key, StringComparer.Ordinal) || PersonalDataFragments.Any(f => key.Contains(f, StringComparison.Ordinal));
}
