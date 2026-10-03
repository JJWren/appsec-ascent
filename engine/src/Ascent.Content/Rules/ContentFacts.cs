using System.Text.Json.Nodes;
using Ascent.Content.Loading;
using Ascent.Content.Model;

namespace Ascent.Content.Rules;

/// <summary>Shared lookups over loaded content.</summary>
public static class ContentFacts
{
    /// <summary>The pack key ("orientation", "d1"…"d8", "capstone") for an exam-domain value from a Quest.</summary>
    public static string? PackForExamDomain(string? examDomain) => examDomain switch
    {
        "ORI" => "orientation",
        "CAP" => "capstone",
        { Length: 2 } d when d[0] == 'D' && d[1] is >= '1' and <= '8' => "d" + d[1],
        _ => null,
    };

    /// <summary>The pack key a Quest's directory implies (from <c>curriculum/&lt;dir&gt;/q-*.md</c>).</summary>
    public static string? PackForQuestPath(string relativePath)
    {
        var segments = relativePath.Split('/');
        return segments.Length >= 3 ? RepoLayout.PackForDirectory(segments[1]) : null;
    }

    /// <summary>Schema-valid Quests whose directory belongs to the pack.</summary>
    public static IEnumerable<ContentDocument> QuestsInPack(ContentIndex index, string packKey) =>
        index.OfKind(DocumentKind.Quest).Where(q => q.IsSchemaValid && PackForQuestPath(q.RelativePath) == packKey);

    /// <summary>All declared IDs of a kind.</summary>
    public static ISet<string> IdsOf(ContentIndex index, DocumentKind kind) =>
        index.OfKind(kind).Select(d => d.Id).OfType<string>().ToHashSet(StringComparer.Ordinal);

    /// <summary>Schema-valid bundle headers keyed by item ID (first wins; duplicates are reported by ID-02).</summary>
    public static IReadOnlyDictionary<string, JsonObject> HeadersById(ContentIndex index)
    {
        var headers = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var header in index.BundleHeaders)
        {
            if (JsonRead.Str(header, "itemId") is { } id)
            {
                headers.TryAdd(id, header);
            }
        }

        return headers;
    }

    /// <summary>Question headers (any pool) from schema-valid bundles.</summary>
    public static IEnumerable<JsonObject> QuestionHeaders(ContentIndex index) =>
        index.BundleHeaders.Where(h => JsonRead.Str(h, "itemType") == "question");

    /// <summary>Ideal item count for a Domain given its weight and a pool size.</summary>
    public static double Ideal(ExamDomainDef domain, int poolSize) => domain.Weight * poolSize / 100.0;
}
