using System.Text.Json;
using System.Text.Json.Nodes;
using Ascent.Content.Model;
using Ascent.Content.Validation;
using YamlDotNet.Core;

namespace Ascent.Content.Loading;

/// <summary>Discovers, parses and schema-validates content, producing a <see cref="ContentIndex"/>.</summary>
public static class ContentLoader
{
    /// <summary>
    /// Loads a repository. When <paramref name="sealedSourcesRoot"/> is given (private sealed repository only),
    /// plaintext Questions under its <c>questions/</c> folder are loaded too.
    /// </summary>
    public static ContentIndex Load(string root, string? sealedSourcesRoot = null)
    {
        var findings = new List<Finding>();
        var schemas = SchemaCatalog.Load(Path.Combine(root, "schemas"), findings);
        var repoFiles = EnumerateFiles(root);
        var documents = new List<ContentDocument>();

        foreach (var path in repoFiles)
        {
            if (RepoLayout.Classify(path) is { } kind)
            {
                documents.Add(LoadDocument(root, path, kind, schemas, findings));
            }
        }

        if (sealedSourcesRoot is not null)
        {
            var questionsRoot = Path.Combine(sealedSourcesRoot, "questions");
            foreach (var path in EnumerateFiles(questionsRoot).Where(p => p.EndsWith(".yaml", StringComparison.Ordinal)))
            {
                documents.Add(LoadDocument(questionsRoot, path, DocumentKind.SealedQuestion, schemas, findings, "sealed-sources:questions/"));
            }
        }

        var outlines = documents
            .Where(d => d.Kind == DocumentKind.Outline && d.IsSchemaValid)
            .Select(ReadOutline)
            .ToList();

        var season = documents.FirstOrDefault(d => d.Kind == DocumentKind.Season && d.IsSchemaValid) is { } seasonDocument
            ? ReadSeason(seasonDocument)
            : SeasonState.Empty;

        return new ContentIndex(root, documents, outlines, season, repoFiles, findings);
    }

    /// <summary>All files under a root (relative, forward slashes, sorted), skipping excluded directories.</summary>
    public static IReadOnlyList<string> EnumerateFiles(string root)
    {
        var results = new List<string>();
        if (!Directory.Exists(root))
        {
            return results;
        }

        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var child in Directory.EnumerateDirectories(directory))
            {
                if (!RepoLayout.ExcludedDirectories.Contains(Path.GetFileName(child)))
                {
                    pending.Push(child);
                }
            }

            results.AddRange(Directory.EnumerateFiles(directory).Select(file => Path.GetRelativePath(root, file).Replace('\\', '/')));
        }

        results.Sort(StringComparer.Ordinal);
        return results;
    }

    private static ContentDocument LoadDocument(
        string root, string path, DocumentKind kind, SchemaCatalog schemas, List<Finding> findings, string displayPrefix = "")
    {
        var displayPath = displayPrefix + path;
        var text = File.ReadAllText(Path.Combine(root, path));
        JsonObject? data = null;
        MarkdownDocument? markdown = null;

        try
        {
            switch (kind)
            {
                case DocumentKind.Quest or DocumentKind.LabBrief:
                    markdown = MarkdownDocument.Parse(text);
                    if (markdown.FrontMatter is null)
                    {
                        findings.Add(Finding.Of("SCH-02", Severity.Error, displayPath, "The file has no YAML front matter.", 1));
                    }
                    else
                    {
                        data = YamlJson.Parse(markdown.FrontMatter) as JsonObject;
                    }

                    break;
                case DocumentKind.Bundle:
                    data = JsonNode.Parse(text) as JsonObject;
                    break;
                default:
                    data = YamlJson.Parse(text) as JsonObject;
                    break;
            }
        }
        // YamlDotNet reports some malformed input as InvalidOperationException or ArgumentException rather than YamlException.
        catch (Exception exception) when (exception is YamlException or JsonException or InvalidCastException or InvalidOperationException or ArgumentException)
        {
            findings.Add(Finding.Of("SCH-02", Severity.Error, displayPath, "The file could not be parsed: " + FirstLine(exception.Message)));
            return new ContentDocument { Kind = kind, RelativePath = displayPath, Markdown = markdown };
        }

        var schemaFile = RepoLayout.SchemaFor(kind);
        var issues = schemas.Validate(schemaFile, data);
        foreach (var issue in issues)
        {
            findings.Add(Finding.Of("SCH-01", Severity.Error, displayPath, "Does not match " + schemaFile + " at " + issue));
        }

        return new ContentDocument
        {
            Kind = kind,
            RelativePath = displayPath,
            Data = data,
            Markdown = markdown,
            IsSchemaValid = data is not null && issues.Count == 0,
        };
    }

    private static Outline ReadOutline(ContentDocument document)
    {
        var data = document.Data!;
        var domains = new List<ExamDomainDef>();
        foreach (var domainNode in JsonRead.Arr(data, "domains")!.OfType<JsonObject>())
        {
            var domainId = JsonRead.Str(domainNode, "id")!;
            var objectives = JsonRead.Arr(domainNode, "objectives")!.OfType<JsonObject>()
                .Select(o => new ObjectiveDef(JsonRead.Str(o, "id")!, domainId, JsonRead.Str(o, "title")!))
                .ToList();
            var topics = JsonRead.Arr(domainNode, "aiGuidanceTopics")!.OfType<JsonObject>()
                .Select(t => new AiTopic(JsonRead.Str(t, "key")!, JsonRead.Str(t, "summary")!))
                .ToList();
            domains.Add(new ExamDomainDef(domainId, JsonRead.Str(domainNode, "name")!, JsonRead.WholeNumber(domainNode, "weight") ?? 0, objectives, topics));
        }

        return new Outline(
            JsonRead.Str(data, "version")!,
            JsonRead.Str(data, "source")!,
            JsonRead.Str(data, "aiGuidanceSource")!,
            domains,
            document.RelativePath);
    }

    private static SeasonState ReadSeason(ContentDocument document)
    {
        var packs = new Dictionary<string, bool>(StringComparer.Ordinal);
        if (JsonRead.Obj(document.Data, "packs") is { } packNode)
        {
            foreach (var (key, value) in packNode)
            {
                packs[key] = value is JsonValue state && state.TryGetValue<string>(out var text) && text == "complete";
            }
        }

        return new SeasonState(
            JsonRead.Str(document.Data, "outlineVersion"),
            JsonRead.Str(document.Data, "status") == "complete",
            packs);
    }

    private static string FirstLine(string message)
    {
        var newline = message.IndexOf('\n', StringComparison.Ordinal);
        return newline < 0 ? message : message[..newline];
    }
}
