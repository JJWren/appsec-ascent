using System.Globalization;

namespace Ascent.Content.Model;

/// <summary>An AI-security topic that ISC2's AI exam guidance maps to a Domain.</summary>
public sealed record AiTopic(string Key, string Summary);

/// <summary>A numbered exam Objective, such as 4.4.</summary>
public sealed record ObjectiveDef(string Id, string DomainId, string Title);

/// <summary>One of the eight exam Domains.</summary>
public sealed record ExamDomainDef(
    string Id,
    string Name,
    int Weight,
    IReadOnlyList<ObjectiveDef> Objectives,
    IReadOnlyList<AiTopic> AiGuidanceTopics)
{
    /// <summary>Domain number, 1–8.</summary>
    public int Number => int.Parse(Id.AsSpan(1), NumberStyles.Integer, CultureInfo.InvariantCulture);

    /// <summary>The pack key used in <c>curriculum/season.yaml</c>, for example <c>d4</c>.</summary>
    public string PackKey => "d" + Number.ToString(CultureInfo.InvariantCulture);
}

/// <summary>A loaded, schema-valid exam outline.</summary>
public sealed record Outline(
    string Version,
    string Source,
    string AiGuidanceSource,
    IReadOnlyList<ExamDomainDef> Domains,
    string RelativePath)
{
    /// <summary>All Objectives across Domains, in outline order.</summary>
    public IEnumerable<ObjectiveDef> Objectives => Domains.SelectMany(domain => domain.Objectives);

    /// <summary>Finds a Domain by ID (D1–D8).</summary>
    public ExamDomainDef? FindDomain(string domainId) =>
        Domains.FirstOrDefault(domain => string.Equals(domain.Id, domainId, StringComparison.Ordinal));

    /// <summary>Finds an Objective by ID.</summary>
    public ObjectiveDef? FindObjective(string objectiveId) =>
        Objectives.FirstOrDefault(objective => string.Equals(objective.Id, objectiveId, StringComparison.Ordinal));
}

/// <summary>Build state of a Season, read from <c>curriculum/season.yaml</c>.</summary>
public sealed record SeasonState(string? OutlineVersion, bool SeasonComplete, IReadOnlyDictionary<string, bool> Packs)
{
    /// <summary>State used when no valid season file exists.</summary>
    public static SeasonState Empty { get; } = new(null, false, new Dictionary<string, bool>(StringComparer.Ordinal));

    /// <summary>True when the pack is marked complete, which promotes its pack-exit rules to errors.</summary>
    public bool IsPackComplete(string packKey) => Packs.TryGetValue(packKey, out var complete) && complete;
}
