using System.Text.Json;
using System.Text.Json.Serialization;
using Ascent.Content.Model;

namespace Ascent.Content.Reporting;

/// <summary>Lint results for machine consumption.</summary>
public sealed record FindingsReport(IReadOnlyList<Finding> Findings)
{
    /// <summary>Number of errors.</summary>
    public int Errors => Findings.Count(f => f.Severity == Severity.Error);

    /// <summary>Number of warnings.</summary>
    public int Warnings => Findings.Count(f => f.Severity == Severity.Warning);
}

/// <summary>Stable JSON serialization for reports (camelCase, string enums, indented).</summary>
public static class JsonOutput
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Serializes a report object.</summary>
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
}
