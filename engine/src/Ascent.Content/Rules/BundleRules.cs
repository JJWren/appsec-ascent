using Ascent.Content.Loading;
using Ascent.Content.Model;

namespace Ascent.Content.Rules;

/// <summary>BND rules: Sealed Bundle metadata. Signature verification (BND-01) is added by the Engine's sealing module.</summary>
public static class BundleRules
{
    /// <summary>Header fields allowed in a Sealed Bundle (BND-02). Anything else could leak spoilers.</summary>
    public static readonly IReadOnlySet<string> HeaderAllowlist = new HashSet<string>(StringComparer.Ordinal)
    {
        "formatVersion", "itemId", "itemType", "tier", "objectiveId", "examDomain", "pool", "aiTopic",
        "contentType", "kdfInfo", "nonce", "createdUtc",
    };

    /// <summary>All BND rules evaluated during lint.</summary>
    public static IReadOnlyList<IContentRule> All { get; } =
    [
        new Rule("BND-02", "Bundle headers contain only allowlisted fields", HeaderFields),
        new Rule("BND-03", "Bundle files are named after their item ID", FileNames),
    ];

    private static IEnumerable<Finding> HeaderFields(RuleContext context)
    {
        foreach (var bundle in context.Index.OfKind(DocumentKind.Bundle))
        {
            if (JsonRead.Obj(bundle.Data, "header") is not { } header)
            {
                continue;
            }

            foreach (var (name, _) in header.Where(property => !HeaderAllowlist.Contains(property.Key)))
            {
                yield return Finding.Of("BND-02", Severity.Error, bundle.RelativePath, "Header field '" + name + "' is not allowlisted.");
            }
        }
    }

    private static IEnumerable<Finding> FileNames(RuleContext context) =>
        context.Index.OfKind(DocumentKind.Bundle)
            .Where(b => b.Id is not null && !string.Equals(b.FileName, b.Id + ".bundle.json", StringComparison.Ordinal))
            .Select(b => Finding.Of("BND-03", Severity.Error, b.RelativePath, "The bundle file should be named " + b.Id + ".bundle.json."));
}
