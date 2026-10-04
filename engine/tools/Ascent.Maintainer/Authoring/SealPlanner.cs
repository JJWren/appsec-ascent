using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ascent.Content.Loading;
using Ascent.Core.Domain;
using Ascent.Sealing.Bundles;
using Ascent.Sealing.Unsealing;

namespace Ascent.Maintainer.Authoring;

/// <summary>One item to seal, read from the private sealed repository's <c>sources/</c>.</summary>
/// <param name="ItemId">The catalog ID; also the bundle's file name.</param>
/// <param name="ItemType">The bundle item type.</param>
/// <param name="Tier">The release tier.</param>
/// <param name="ContentType">The plaintext's media type.</param>
/// <param name="BundlePath">The bundle's path, relative to the public repository root.</param>
/// <param name="Plaintext">Produces the plaintext bytes.</param>
public sealed record SealItem(string ItemId, string ItemType, SealTier Tier, string ContentType, string BundlePath, Func<byte[]> Plaintext)
{
    /// <summary>The Objective, when known.</summary>
    public string? ObjectiveId { get; init; }

    /// <summary>The exam Domain, when known.</summary>
    public string? ExamDomain { get; init; }

    /// <summary>The question pool, for questions.</summary>
    public string? Pool { get; init; }

    /// <summary>The AI-guidance topic, when the item covers one.</summary>
    public string? AiTopic { get; init; }
}

/// <summary>
/// Maps the private <c>sources/</c> layout to bundles:
/// <list type="bullet">
/// <item><c>questions/**/*.yaml</c> → <c>sealed/questions/</c> (or <c>sealed/simulation/</c> for the simulation pool)</item>
/// <item><c>labs/&lt;lab&gt;/{module,plant,tests,fix}/</c> → <c>sealed/labs/&lt;lab&gt;.&lt;part&gt;</c></item>
/// <item><c>references/&lt;dlv&gt;.md</c> → <c>sealed/references/&lt;dlv&gt;.reference</c></item>
/// <item><c>releases/&lt;n&gt;/</c> → <c>sealed/releases/release-&lt;n&gt;</c></item>
/// <item><c>drills/&lt;drill&gt;.md</c> → <c>sealed/drills/&lt;drill&gt;.answer</c></item>
/// <item><c>deep-dives/&lt;id&gt;/</c> → <c>sealed/deep-dives/&lt;id&gt;.solution</c></item>
/// </list>
/// </summary>
public static class SealPlanner
{
    private static readonly (string Part, string ItemType, SealTier Tier)[] LabParts =
    [
        ("module", "lab-module", SealTier.Start),
        ("plant", "lab-plant", SealTier.Start),
        ("tests", "lab-tests", SealTier.Earned),
        ("fix", "lab-fix", SealTier.Earned),
    ];

    private static readonly JsonSerializerOptions CompactJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Plans every item under <paramref name="sourcesRoot"/>.</summary>
    public static IReadOnlyList<SealItem> Plan(string sourcesRoot, string repoRoot)
    {
        var items = new List<SealItem>();
        items.AddRange(Questions(Path.Join(sourcesRoot, "questions")));
        items.AddRange(Labs(Path.Join(sourcesRoot, "labs"), repoRoot));
        items.AddRange(Files(Path.Join(sourcesRoot, "references"), "*.md", "reference", "reference", SealTier.Submitted, "sealed/references", "text/markdown"));
        items.AddRange(Folders(Path.Join(sourcesRoot, "releases"), name => "release-" + name, "release", SealTier.Release, "sealed/releases"));
        items.AddRange(Files(Path.Join(sourcesRoot, "drills"), "*.md", "answer", "drill-answer", SealTier.Submitted, "sealed/drills", "text/markdown"));
        items.AddRange(Folders(Path.Join(sourcesRoot, "deep-dives"), name => name + ".solution", "deep-dive-solution", SealTier.Submitted, "sealed/deep-dives"));

        var duplicate = items.GroupBy(item => item.ItemId, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        return duplicate is null
            ? items.OrderBy(item => item.ItemId, StringComparer.Ordinal).ToList()
            : throw new InvalidOperationException("Two sources produce the item ID '" + duplicate.Key + "'.");
    }

    private static IEnumerable<SealItem> Questions(string root)
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        foreach (var path in Directory.EnumerateFiles(root, "*.yaml", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var question = YamlJson.Parse(File.ReadAllText(path)) as JsonObject
                ?? throw new InvalidOperationException("A question file is not a YAML mapping: " + Path.GetFileName(path));
            var id = question["id"]?.GetValue<string>() ?? throw new InvalidOperationException("A question has no id: " + Path.GetFileName(path));
            var pool = question["pool"]?.GetValue<string>() ?? "practice";
            var objective = question["objectiveId"]?.GetValue<string>();
            var simulation = pool == "simulation";
            var json = question.ToJsonString(CompactJson);
            yield return new SealItem(
                id,
                "question",
                simulation ? SealTier.Simulation : SealTier.Practice,
                "application/json",
                (simulation ? "sealed/simulation/" : "sealed/questions/") + id + SealedBundle.FileSuffix,
                () => Encoding.UTF8.GetBytes(json))
            {
                ObjectiveId = objective,
                ExamDomain = objective is { Length: > 0 } ? "D" + objective[0] : null,
                Pool = pool,
                AiTopic = question["aiTopic"]?.GetValueKind() == JsonValueKind.String ? question["aiTopic"]!.GetValue<string>() : null,
            };
        }
    }

    private static IEnumerable<SealItem> Labs(string root, string repoRoot)
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        foreach (var labFolder in Directory.EnumerateDirectories(root).Order(StringComparer.Ordinal))
        {
            var labId = Path.GetFileName(labFolder);
            var objective = LabObjective(repoRoot, labId);
            foreach (var (part, itemType, tier) in LabParts)
            {
                var folder = Path.Join(labFolder, part);
                if (Directory.Exists(folder))
                {
                    yield return new SealItem(labId + "." + part, itemType, tier, SafeArchive.ContentType, "sealed/labs/" + labId + "." + part + SealedBundle.FileSuffix, () => SafeArchive.CreateTarGz(folder))
                    {
                        ObjectiveId = objective,
                        ExamDomain = objective is { Length: > 0 } ? "D" + objective[0] : null,
                    };
                }
            }
        }
    }

    private static IEnumerable<SealItem> Files(string root, string pattern, string suffix, string itemType, SealTier tier, string target, string contentType)
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        foreach (var path in Directory.EnumerateFiles(root, pattern).Order(StringComparer.Ordinal))
        {
            var id = Path.GetFileNameWithoutExtension(path) + "." + suffix;
            yield return new SealItem(id, itemType, tier, contentType, target + "/" + id + SealedBundle.FileSuffix, () => File.ReadAllBytes(path));
        }
    }

    private static IEnumerable<SealItem> Folders(string root, Func<string, string> idFor, string itemType, SealTier tier, string target)
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        foreach (var folder in Directory.EnumerateDirectories(root).Order(StringComparer.Ordinal))
        {
            var id = idFor(Path.GetFileName(folder));
            yield return new SealItem(id, itemType, tier, SafeArchive.ContentType, target + "/" + id + SealedBundle.FileSuffix, () => SafeArchive.CreateTarGz(folder));
        }
    }

    private static string? LabObjective(string repoRoot, string labId)
    {
        var manifest = Path.Join(repoRoot, "labs", labId, "lab.yaml");
        return File.Exists(manifest) && YamlJson.Parse(File.ReadAllText(manifest)) is JsonObject json && json["objectiveId"]?.GetValueKind() == JsonValueKind.String
            ? json["objectiveId"]!.GetValue<string>()
            : null;
    }
}
