using Ascent.Content.Loading;
using Ascent.Content.Model;

namespace Ascent.Content.Rules;

/// <summary>LNK rules: internal links and anchors resolve. External links are checked by a scheduled workflow (LNK-02).</summary>
public static class LinkRules
{
    private static readonly string[] CheckedRoots = ["curriculum/", "labs/", "deliverables/", "docs/"];
    private static readonly string[] CheckedRootFiles = ["README.md", "CONTRIBUTING.md", "SECURITY.md", "CONTEXT.md"];

    /// <summary>All LNK rules evaluated during lint.</summary>
    public static IReadOnlyList<IContentRule> All { get; } =
    [
        new Rule("LNK-01", "Internal links and anchors resolve", InternalLinks),
    ];

    private static IEnumerable<Finding> InternalLinks(RuleContext context)
    {
        var markdownFiles = context.Index.RepoFiles.Where(IsChecked).ToList();
        var cache = new Dictionary<string, MarkdownDocument>(StringComparer.Ordinal);

        foreach (var file in markdownFiles)
        {
            var document = Load(context, file, cache);
            foreach (var link in document.RelativeLinks())
            {
                var problem = Resolve(context, file, link.Target, cache);
                if (problem is not null)
                {
                    yield return Finding.Of("LNK-01", Severity.Error, file, problem, link.Line);
                }
            }
        }
    }

    private static string? Resolve(RuleContext context, string file, string target, Dictionary<string, MarkdownDocument> cache)
    {
        var hashIndex = target.IndexOf('#', StringComparison.Ordinal);
        var pathPart = hashIndex >= 0 ? target[..hashIndex] : target;
        var anchor = hashIndex >= 0 ? target[(hashIndex + 1)..] : null;
        var queryIndex = pathPart.IndexOf('?', StringComparison.Ordinal);
        if (queryIndex >= 0)
        {
            pathPart = pathPart[..queryIndex];
        }

        var targetFile = file;
        if (pathPart.Length > 0)
        {
            var baseDirectory = Path.GetDirectoryName(Path.Combine(context.Index.Root, file))!;
            var full = Path.GetFullPath(Path.Combine(baseDirectory, Uri.UnescapeDataString(pathPart)));
            if (!File.Exists(full) && !Directory.Exists(full))
            {
                return "Link target '" + target + "' does not exist.";
            }

            targetFile = Path.GetRelativePath(context.Index.Root, full).Replace('\\', '/');
        }

        if (anchor is null || !targetFile.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || !File.Exists(Path.Combine(context.Index.Root, targetFile)))
        {
            return null;
        }

        return Load(context, targetFile, cache).Anchors().Contains(anchor) ? null : "Anchor '#" + anchor + "' does not exist in " + targetFile + ".";
    }

    private static MarkdownDocument Load(RuleContext context, string file, Dictionary<string, MarkdownDocument> cache)
    {
        if (!cache.TryGetValue(file, out var document))
        {
            document = MarkdownDocument.Parse(context.Index.ReadText(file) ?? string.Empty);
            cache[file] = document;
        }

        return document;
    }

    private static bool IsChecked(string path) =>
        path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
        && (CheckedRootFiles.Contains(path, StringComparer.Ordinal) || CheckedRoots.Any(root => path.StartsWith(root, StringComparison.Ordinal)));
}
