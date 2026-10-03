using Ascent.Content.Model;

namespace Ascent.Content.Rules;

/// <summary>LEG rules: licensing, trademark notice and contribution policy.</summary>
public static class LegalRules
{
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".gif", ".svg", ".webp", ".ico"];

    /// <summary>All LEG rules.</summary>
    public static IReadOnlyList<IContentRule> All { get; } =
    [
        new Rule("LEG-01", "README carries the ISC2 notice and non-affiliation disclaimer", Disclaimer),
        new Rule("LEG-02", "Code and content licenses are present", Licenses),
        new Rule("LEG-03", "No ISC2 logos or images", NoLogos),
        new Rule("LEG-04", "CONTRIBUTING forbids exam-recalled content and states the PR policy", Contributing),
    ];

    private static IEnumerable<Finding> Disclaimer(RuleContext context)
    {
        var readme = context.Index.ReadText("README.md");
        if (readme is null
            || !readme.Contains("registered certification mark of ISC2", StringComparison.OrdinalIgnoreCase)
            || !readme.Contains("not affiliated", StringComparison.OrdinalIgnoreCase))
        {
            yield return Finding.Of("LEG-01", Severity.Error, "README.md",
                "README.md must state that CSSLP® is a registered certification mark of ISC2, Inc. and that the project is not affiliated with ISC2.");
        }
    }

    private static IEnumerable<Finding> Licenses(RuleContext context)
    {
        if (context.Index.ReadText("LICENSE")?.Contains("MIT License", StringComparison.Ordinal) != true)
        {
            yield return Finding.Of("LEG-02", Severity.Error, "LICENSE", "LICENSE must contain the MIT License for code.");
        }

        if (context.Index.ReadText("LICENSE-CONTENT")?.Contains("CC BY-NC-SA 4.0", StringComparison.Ordinal) != true)
        {
            yield return Finding.Of("LEG-02", Severity.Error, "LICENSE-CONTENT", "LICENSE-CONTENT must license content under CC BY-NC-SA 4.0.");
        }
    }

    private static IEnumerable<Finding> NoLogos(RuleContext context) =>
        context.Index.RepoFiles
            .Where(path => path.Contains("isc2", StringComparison.OrdinalIgnoreCase)
                           && ImageExtensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            .Select(path => Finding.Of("LEG-03", Severity.Warning, path, "Images of ISC2 marks or logos must not be included."));

    private static IEnumerable<Finding> Contributing(RuleContext context)
    {
        var text = context.Index.ReadText("CONTRIBUTING.md");
        if (text is null
            || !text.Contains("exam-recalled", StringComparison.OrdinalIgnoreCase)
            || !text.Contains("pull request", StringComparison.OrdinalIgnoreCase))
        {
            yield return Finding.Of("LEG-04", Severity.Error, "CONTRIBUTING.md",
                "CONTRIBUTING.md must forbid exam-recalled content and state the pull request policy.");
        }
    }
}
