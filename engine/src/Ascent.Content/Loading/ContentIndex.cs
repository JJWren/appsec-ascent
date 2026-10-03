using Ascent.Content.Model;

namespace Ascent.Content.Loading;

/// <summary>An immutable snapshot of everything the framework loaded from a repository.</summary>
public sealed class ContentIndex
{
    private readonly ILookup<DocumentKind, ContentDocument> _byKind;

    /// <summary>Creates the index.</summary>
    public ContentIndex(
        string root,
        IReadOnlyList<ContentDocument> documents,
        IReadOnlyList<Outline> outlines,
        SeasonState season,
        IReadOnlyList<string> repoFiles,
        IReadOnlyList<Finding> loadFindings)
    {
        Root = root;
        Documents = documents;
        Outlines = outlines;
        Season = season;
        RepoFiles = repoFiles;
        LoadFindings = loadFindings;
        _byKind = documents.ToLookup(document => document.Kind);
        Outline = outlines.FirstOrDefault(o => string.Equals(o.Version, season.OutlineVersion, StringComparison.Ordinal))
            ?? outlines.OrderBy(o => o.Version, StringComparer.Ordinal).LastOrDefault();
    }

    /// <summary>Absolute repository root.</summary>
    public string Root { get; }

    /// <summary>Every classified document.</summary>
    public IReadOnlyList<ContentDocument> Documents { get; }

    /// <summary>All schema-valid outlines.</summary>
    public IReadOnlyList<Outline> Outlines { get; }

    /// <summary>The active outline: the season's version, else the latest.</summary>
    public Outline? Outline { get; }

    /// <summary>Season build state.</summary>
    public SeasonState Season { get; }

    /// <summary>Every repository file (relative, forward slashes), excluding ignored directories.</summary>
    public IReadOnlyList<string> RepoFiles { get; }

    /// <summary>Parse and schema findings produced while loading.</summary>
    public IReadOnlyList<Finding> LoadFindings { get; }

    /// <summary>Documents of one kind.</summary>
    public IEnumerable<ContentDocument> OfKind(DocumentKind kind) => _byKind[kind];

    /// <summary>Bundle headers of schema-valid bundles (never the encrypted payload).</summary>
    public IEnumerable<System.Text.Json.Nodes.JsonObject> BundleHeaders =>
        OfKind(DocumentKind.Bundle).Where(b => b.IsSchemaValid).Select(b => JsonRead.Obj(b.Data, "header")).OfType<System.Text.Json.Nodes.JsonObject>();

    /// <summary>True when a repository file exists at the relative path.</summary>
    public bool FileExists(string relativePath) => File.Exists(Path.Combine(Root, relativePath));

    /// <summary>Reads a repository text file, or null when missing.</summary>
    public string? ReadText(string relativePath)
    {
        var full = Path.Combine(Root, relativePath);
        return File.Exists(full) ? File.ReadAllText(full) : null;
    }
}
