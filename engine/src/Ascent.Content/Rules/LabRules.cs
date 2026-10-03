using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Ascent.Content.Loading;
using Ascent.Content.Model;

namespace Ascent.Content.Rules;

/// <summary>LAB rules: manifests, Briefs, Sealed references and cost safety.</summary>
public static partial class LabRules
{
    private static readonly string[] RequiredBriefSections = ["Scenario", "Acceptance criteria", "Stages", "Rules of engagement"];

    private static readonly (string Part, string ItemType, string Tier)[] SealedParts =
    [
        ("tests", "lab-tests", "earned"),
        ("fix", "lab-fix", "earned"),
        ("plant", "lab-plant", "start"),
    ];

    /// <summary>All LAB rules.</summary>
    public static IReadOnlyList<IContentRule> All { get; } =
    [
        new Rule("LAB-01", "Every Lab has a valid, spoiler-free Brief", Briefs),
        new Rule("LAB-02", "Stages are declared consistently with honest cost estimates", Stages),
        new Rule("LAB-03", "Paid or Windows-only Labs are bonus content", Bonus),
        new Rule("LAB-04", "Sealed references resolve to bundles of the right tier", SealedReferences),
        new Rule("LAB-05", "Cloud Stages never deploy resources on the never-deploy list", NeverDeploy),
    ];

    private static IEnumerable<ContentDocument> Manifests(RuleContext context) =>
        context.Index.OfKind(DocumentKind.LabManifest).Where(m => m.IsSchemaValid);

    private static IEnumerable<Finding> Briefs(RuleContext context)
    {
        var briefs = context.Index.OfKind(DocumentKind.LabBrief).ToDictionary(b => Folder(b.RelativePath), StringComparer.Ordinal);
        var manifests = context.Index.OfKind(DocumentKind.LabManifest).ToDictionary(m => Folder(m.RelativePath), StringComparer.Ordinal);

        foreach (var (folder, manifest) in manifests)
        {
            if (!briefs.TryGetValue(folder, out var brief))
            {
                yield return Finding.Of("LAB-01", Severity.Error, manifest.RelativePath, "The Lab has no BRIEF.md.");
                continue;
            }

            if (JsonRead.Str(brief.Data, "objectiveId") is { } briefObjective && JsonRead.Str(manifest.Data, "objectiveId") is { } manifestObjective
                && briefObjective != manifestObjective)
            {
                yield return Finding.Of("LAB-01", Severity.Error, brief.RelativePath, "The Brief's objectiveId does not match lab.yaml.");
            }

            var headings = brief.Markdown?.Sections().Select(s => s.Heading).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
            foreach (var section in RequiredBriefSections.Where(s => !headings.Contains(s)))
            {
                yield return Finding.Of("LAB-01", Severity.Error, brief.RelativePath, "The Brief is missing the '## " + section + "' section.");
            }
        }

        foreach (var (folder, brief) in briefs.Where(b => !manifests.ContainsKey(b.Key)))
        {
            yield return Finding.Of("LAB-01", Severity.Error, brief.RelativePath, "BRIEF.md has no lab.yaml in labs/" + folder + "/.");
        }
    }

    private static IEnumerable<Finding> Stages(RuleContext context)
    {
        var briefs = context.Index.OfKind(DocumentKind.LabBrief).ToDictionary(b => Folder(b.RelativePath), StringComparer.Ordinal);
        foreach (var manifest in Manifests(context))
        {
            var cloud = JsonRead.Obj(JsonRead.Obj(manifest.Data, "stages"), "cloud");
            if (cloud is not null)
            {
                var estimate = JsonRead.Num(cloud, "estimateUsd") ?? 0;
                if (JsonRead.Bool(cloud, "freeTier") == true && estimate > 0)
                {
                    yield return Finding.Of("LAB-02", Severity.Warning, manifest.RelativePath, "A free-tier Cloud Stage should estimate $0.");
                }

                if (JsonRead.Bool(cloud, "paidSideQuest") == true && estimate <= 0)
                {
                    yield return Finding.Of("LAB-02", Severity.Warning, manifest.RelativePath, "A paid side-quest must state its estimated cost.");
                }
            }

            if (briefs.TryGetValue(Folder(manifest.RelativePath), out var brief))
            {
                var briefStages = JsonRead.Strings(JsonRead.Arr(brief.Data, "stages"));
                if (briefStages.Contains("cloud") != (cloud is not null))
                {
                    yield return Finding.Of("LAB-02", Severity.Error, brief.RelativePath, "The Brief's stages must match lab.yaml (cloud stage present or not).");
                }
            }
        }
    }

    private static IEnumerable<Finding> Bonus(RuleContext context) =>
        Manifests(context)
            .Where(m => (JsonRead.Bool(JsonRead.Obj(JsonRead.Obj(m.Data, "stages"), "cloud"), "paidSideQuest") == true || JsonRead.Bool(m.Data, "windowsOnly") == true)
                        && JsonRead.Bool(m.Data, "bonus") != true)
            .Select(m => Finding.Of("LAB-03", Severity.Error, m.RelativePath, "Paid or Windows-only Labs must be marked bonus: true.",
                hint: "Rank progress must never require money or Windows."));

    private static IEnumerable<Finding> SealedReferences(RuleContext context)
    {
        var headers = ContentFacts.HeadersById(context.Index);
        foreach (var manifest in Manifests(context))
        {
            var labId = manifest.Id!;
            var references = new List<(string Name, string? Ref, string ItemType, string Tier)>
            {
                ("module", JsonRead.Str(JsonRead.Obj(manifest.Data, "module"), "sealedRef"), "lab-module", "start"),
            };
            references.AddRange(SealedParts.Select(p => (p.Part, JsonRead.Str(JsonRead.Obj(manifest.Data, "sealed"), p.Part), p.ItemType, p.Tier)));

            foreach (var (name, reference, itemType, tier) in references)
            {
                var problem = ReferenceProblem(labId, reference, itemType, tier, headers);
                if (problem is not null)
                {
                    yield return Finding.Of("LAB-04", Severity.Error, manifest.RelativePath, "Sealed " + name + " reference: " + problem);
                }
            }
        }
    }

    private static string? ReferenceProblem(string labId, string? reference, string itemType, string tier, IReadOnlyDictionary<string, JsonObject> headers)
    {
        if (reference is null || !reference.StartsWith(labId + ".", StringComparison.Ordinal))
        {
            return "must start with '" + labId + ".'.";
        }

        if (!headers.TryGetValue(reference, out var header))
        {
            return "no Sealed Bundle '" + reference + "' exists.";
        }

        return JsonRead.Str(header, "itemType") != itemType || JsonRead.Str(header, "tier") != tier
            ? "bundle '" + reference + "' must be itemType " + itemType + " with tier " + tier + "."
            : null;
    }

    private static IEnumerable<Finding> NeverDeploy(RuleContext context)
    {
        foreach (var manifest in Manifests(context))
        {
            var bicep = JsonRead.Str(JsonRead.Obj(JsonRead.Obj(manifest.Data, "stages"), "cloud"), "bicep");
            if (bicep is null)
            {
                continue;
            }

            var text = context.Index.ReadText(bicep);
            if (text is null)
            {
                yield return Finding.Of("LAB-05", Severity.Error, manifest.RelativePath, "Cloud Stage template '" + bicep + "' does not exist.");
                continue;
            }

            foreach (var finding in ScanTemplate(bicep, text))
            {
                yield return finding;
            }
        }
    }

    /// <summary>Finds never-deploy resources in a Bicep template.</summary>
    public static IEnumerable<Finding> ScanTemplate(string path, string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var match = NeverDeployResource().Match(lines[i]);
            if (!match.Success)
            {
                continue;
            }

            var type = match.Groups["type"].Value;
            var window = string.Join('\n', lines.Skip(i).Take(40));
            if (!IsAllowedVariant(type, window))
            {
                yield return Finding.Of("LAB-05", Severity.Error, path, "Resource type " + type + " is on the never-deploy list.", i + 1,
                    "Use a free or low-cost alternative; see the cost guardrails in the Lab Brief.");
            }
        }
    }

    // Some resource types are only expensive in particular variants: Bastion is free on the Developer SKU,
    // Front Door (Cdn/profiles) only needs blocking on Premium, and solutions only when they enable Sentinel.
    private static bool IsAllowedVariant(string type, string window) => type.ToUpperInvariant() switch
    {
        "MICROSOFT.NETWORK/BASTIONHOSTS" => window.Contains("'Developer'", StringComparison.Ordinal),
        "MICROSOFT.CDN/PROFILES" => !window.Contains("Premium_AzureFrontDoor", StringComparison.Ordinal),
        "MICROSOFT.OPERATIONSMANAGEMENT/SOLUTIONS" => !window.Contains("SecurityInsights", StringComparison.Ordinal),
        _ => false,
    };

    private static string Folder(string relativePath) => relativePath.Split('/')[1];

    [GeneratedRegex(
        @"'(?<type>Microsoft\.Network/applicationGateways|Microsoft\.Network/azureFirewalls|Microsoft\.Network/ddosProtectionPlans|Microsoft\.Network/bastionHosts|Microsoft\.KeyVault/managedHSMs|Microsoft\.Network/frontDoors|Microsoft\.Cdn/profiles|Microsoft\.SecurityInsights/[A-Za-z]+|Microsoft\.OperationsManagement/solutions)@",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, 1000)]
    private static partial Regex NeverDeployResource();
}
