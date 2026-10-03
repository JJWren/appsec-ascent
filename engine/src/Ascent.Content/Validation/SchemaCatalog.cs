using System.Text.Json;
using System.Text.Json.Nodes;
using Ascent.Content.Model;
using LateApexEarlySpeed.Json.Schema;
using LateApexEarlySpeed.Json.Schema.Common;

namespace Ascent.Content.Validation;

/// <summary>Loads the JSON Schemas in <c>schemas/</c> and validates content against them (Draft 2020-12).</summary>
public sealed class SchemaCatalog
{
    private static readonly JsonSchemaOptions ValidationOptions = new()
    {
        OutputFormat = OutputFormat.List,
        ValidateFormat = true,
    };

    private readonly Dictionary<string, JsonValidator> _validators;

    private SchemaCatalog(Dictionary<string, JsonValidator> validators) => _validators = validators;

    /// <summary>Loads every <c>*.schema.json</c> file. Unreadable schemas are reported as findings.</summary>
    public static SchemaCatalog Load(string schemasDirectory, ICollection<Finding> findings)
    {
        var validators = new Dictionary<string, JsonValidator>(StringComparer.Ordinal);
        if (!Directory.Exists(schemasDirectory))
        {
            findings.Add(Finding.Of("SCH-01", Severity.Error, "schemas/", "The schemas/ directory is missing, so content cannot be validated."));
            return new SchemaCatalog(validators);
        }

        foreach (var file in Directory.EnumerateFiles(schemasDirectory, "*.schema.json").Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(file);
            try
            {
                validators[name] = new JsonValidator(File.ReadAllText(file));
            }
            catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException or NotSupportedException)
            {
                findings.Add(Finding.Of("SCH-01", Severity.Error, "schemas/" + name, "The schema could not be loaded: " + exception.Message));
            }
        }

        return new SchemaCatalog(validators);
    }

    /// <summary>True when a schema with this file name was loaded.</summary>
    public bool Contains(string schemaFileName) => _validators.ContainsKey(schemaFileName);

    /// <summary>Validates a node and returns one message per issue ("pointer: message"); empty when valid.</summary>
    public IReadOnlyList<string> Validate(string schemaFileName, JsonNode? instance)
    {
        if (!_validators.TryGetValue(schemaFileName, out var validator))
        {
            return ["schema '" + schemaFileName + "' is not available"];
        }

        using var document = JsonDocument.Parse(instance?.ToJsonString() ?? "null");
        var result = validator.Validate(document.RootElement, ValidationOptions);
        if (result.IsValid)
        {
            return [];
        }

        return [.. result.ValidationErrors
            .Select(error => Describe(error.InstanceLocation?.ToString(), error.ErrorMessage))
            .Distinct(StringComparer.Ordinal)];
    }

    private static string Describe(string? location, string? message) =>
        (string.IsNullOrEmpty(location) ? "/" : location) + ": " + (message ?? "invalid");
}
