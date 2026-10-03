using System.Text.Json.Nodes;

namespace Ascent.Content.Loading;

/// <summary>The kinds of content files the framework understands.</summary>
public enum DocumentKind
{
    /// <summary><c>curriculum/outline/*.yaml</c>.</summary>
    Outline,

    /// <summary><c>curriculum/outline/mappings/*.yaml</c>.</summary>
    OutlineMapping,

    /// <summary><c>curriculum/season.yaml</c>.</summary>
    Season,

    /// <summary><c>curriculum/**/q-*.md</c>.</summary>
    Quest,

    /// <summary><c>curriculum/drills/*.yaml</c>.</summary>
    Drill,

    /// <summary><c>labs/&lt;id&gt;/lab.yaml</c>.</summary>
    LabManifest,

    /// <summary><c>labs/&lt;id&gt;/BRIEF.md</c>.</summary>
    LabBrief,

    /// <summary><c>deliverables/templates/*.yaml</c>.</summary>
    DeliverableTemplate,

    /// <summary><c>deliverables/rubrics/*.yaml</c>.</summary>
    Rubric,

    /// <summary><c>sealed/**/*.bundle.json</c>.</summary>
    Bundle,

    /// <summary><c>security/exceptions.yaml</c>.</summary>
    ExceptionRegister,

    /// <summary>Plaintext Question in the private sealed repository (lint with <c>--sealed-sources</c>).</summary>
    SealedQuestion,
}

/// <summary>A loaded content file: its structured data (YAML, front matter or bundle JSON) and, for Markdown, its body.</summary>
public sealed class ContentDocument
{
    /// <summary>What kind of content this is.</summary>
    public required DocumentKind Kind { get; init; }

    /// <summary>Path relative to the repository root (or sealed sources root), with forward slashes.</summary>
    public required string RelativePath { get; init; }

    /// <summary>Structured data, or null when the file could not be parsed.</summary>
    public JsonObject? Data { get; init; }

    /// <summary>Markdown body for Quests and Lab Briefs.</summary>
    public MarkdownDocument? Markdown { get; init; }

    /// <summary>True when the data validated against its JSON Schema.</summary>
    public bool IsSchemaValid { get; init; }

    /// <summary>The item ID this document declares, if any.</summary>
    public string? Id => Kind switch
    {
        DocumentKind.Bundle => JsonRead.Str(JsonRead.Obj(Data, "header"), "itemId"),
        DocumentKind.LabBrief => JsonRead.Str(Data, "labId"),
        DocumentKind.Quest or DocumentKind.Drill or DocumentKind.LabManifest or DocumentKind.DeliverableTemplate
            or DocumentKind.Rubric or DocumentKind.SealedQuestion => JsonRead.Str(Data, "id"),
        _ => null,
    };

    /// <summary>File name without directories.</summary>
    public string FileName => RelativePath[(RelativePath.LastIndexOf('/') + 1)..];
}
