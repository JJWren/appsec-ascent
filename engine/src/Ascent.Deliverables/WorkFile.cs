using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ascent.Content.Loading;
using Ascent.Core.Platform;
using YamlDotNet.Core;

namespace Ascent.Deliverables;

/// <summary>
/// A Learner's Deliverable work, <c>my-work/deliverables/&lt;id&gt;.yaml</c> (DLE-01, DLE-02): each template section
/// is a list of entries, and each entry has the section's fields.
/// </summary>
public static class WorkFile
{
    /// <summary>The largest work file read.</summary>
    public const int MaxBytes = 256 * 1024;

    /// <summary>A starting file: every section with its minimum number of empty entries, described in comments.</summary>
    public static string Scaffold(DeliverableTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        var yaml = new StringBuilder()
            .Append("# ").Append(Line(template.Title)).Append(" (").Append(template.Id).Append(")\n")
            .Append("# Objectives: ").Append(string.Join(", ", template.ObjectiveIds)).Append('\n')
            .Append("# Fill in each section, then run 'ascent deliver ").Append(template.Id).Append("' again.\n")
            .Append("# Each section is a list: add a '- ' entry for every item you record.\n");
        foreach (var section in template.Sections)
        {
            yaml.Append('\n')
                .Append("# ").Append(Line(section.Title))
                .Append(section.Required ? " (required" : " (optional")
                .Append(section.MinItems > 0 ? string.Create(CultureInfo.InvariantCulture, $", at least {section.MinItems} entries") : string.Empty)
                .Append(")\n")
                .Append(section.Key).Append(":\n");
            for (var entry = 0; entry < Math.Max(1, section.MinItems); entry++)
            {
                var first = true;
                foreach (var field in section.Fields)
                {
                    yaml.Append(first ? "  - " : "    ").Append(field.Key).Append(": ").Append(Placeholder(field))
                        .Append("  # ").Append(Describe(field)).Append('\n');
                    first = false;
                }
            }
        }

        return yaml.ToString();
    }

    /// <summary>Checks a work file against its template; every problem is listed (DLE-02). Empty means valid.</summary>
    public static IReadOnlyList<string> Validate(DeliverableTemplate template, string yaml)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(yaml);
        JsonNode? root;
        try
        {
            root = YamlJson.Parse(yaml);
        }
        catch (Exception ex) when (ex is YamlException or ArgumentException)
        {
            return ["The file isn't valid YAML: " + SafeText.Sanitize(ex.Message, 200)];
        }

        if (root is not JsonObject sections)
        {
            return ["The file must list the template's sections by key."];
        }

        var problems = new List<string>();
        var known = template.Sections.Select(s => s.Key).ToHashSet(StringComparer.Ordinal);
        problems.AddRange(sections.Select(p => p.Key).Where(key => !known.Contains(key)).Select(key => "Unknown section '" + SafeText.Sanitize(key, 60) + "'."));
        foreach (var section in template.Sections)
        {
            CheckSection(section, sections[section.Key], problems);
        }

        return problems;
    }

    private static void CheckSection(TemplateSection section, JsonNode? value, List<string> problems)
    {
        if (value is null)
        {
            if (section.Required)
            {
                problems.Add("Section '" + section.Key + "' (" + section.Title + ") is missing.");
            }

            return;
        }

        if (value is not JsonArray entries)
        {
            problems.Add("Section '" + section.Key + "' must be a list of entries, each starting with '- '.");
            return;
        }

        if (entries.Count < section.MinItems)
        {
            problems.Add(string.Create(CultureInfo.InvariantCulture, $"Section '{section.Key}' needs at least {section.MinItems} entries; it has {entries.Count}."));
        }

        var fields = section.Fields.Select(f => f.Key).ToHashSet(StringComparer.Ordinal);
        for (var index = 0; index < entries.Count; index++)
        {
            var where = string.Create(CultureInfo.InvariantCulture, $"{section.Key}[{index + 1}]");
            if (entries[index] is not JsonObject entry)
            {
                problems.Add(where + " must be an entry with fields.");
                continue;
            }

            problems.AddRange(entry.Select(p => p.Key).Where(key => !fields.Contains(key)).Select(key => where + " has an unknown field '" + SafeText.Sanitize(key, 60) + "'."));
            foreach (var field in section.Fields)
            {
                if (FieldProblem(field, entry[field.Key]) is { } problem)
                {
                    problems.Add(where + "." + field.Key + " " + problem);
                }
            }
        }
    }

    private static string? FieldProblem(TemplateField field, JsonNode? value)
    {
        if (IsEmpty(value))
        {
            return field.Required ? "is required." : null;
        }

        var kind = value!.GetValueKind();
        return field.Type switch
        {
            "list" => value is JsonArray items && items.All(i => !IsEmpty(i) && i!.GetValueKind() is JsonValueKind.String or JsonValueKind.Number)
                ? null
                : "must be a list of text items.",
            "enum" => kind == JsonValueKind.String && field.Options.Contains(value.GetValue<string>(), StringComparer.Ordinal)
                ? null
                : "must be one of: " + string.Join(", ", field.Options) + ".",
            "number" => kind == JsonValueKind.Number ? null : "must be a number.",
            "date" => kind == JsonValueKind.String && DateOnly.TryParseExact(value.GetValue<string>(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                ? null
                : "must be a date like 2026-10-05.",
            _ => kind is JsonValueKind.String or JsonValueKind.Number ? null : "must be text.",
        };
    }

    private static bool IsEmpty(JsonNode? value) => value switch
    {
        null => true,
        JsonValue text when text.GetValueKind() == JsonValueKind.String => string.IsNullOrWhiteSpace(text.GetValue<string>()),
        JsonArray items => items.Count == 0,
        _ => false,
    };

    private static string Placeholder(TemplateField field) => field.Type switch
    {
        "list" => "[]",
        "number" => "null",
        _ => "\"\"",
    };

    private static string Describe(TemplateField field)
    {
        var description = field.Type switch
        {
            "enum" => "one of: " + string.Join(" | ", field.Options.Select(Line)),
            "date" => "a date like 2026-10-05",
            "list" => "a list, such as [first, second]",
            "reference" => "an ID, such as a Lab or Quest ID",
            "markdown" => "text; Markdown is fine",
            _ => field.Type,
        };
        return description + (field.Required ? " (required)" : " (optional)");
    }

    // Comments are single lines; nothing from the template may end one early.
    private static string Line(string text) => SafeText.Sanitize(text, 200).ReplaceLineEndings(" ");
}
