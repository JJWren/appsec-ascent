using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Ascent.Content.Loading;

/// <summary>A level-2 section of a Markdown body.</summary>
public sealed record MarkdownSection(string Heading, int Line, string Text);

/// <summary>A relative or anchor link found in a Markdown body.</summary>
public sealed record MarkdownLink(string Target, int Line);

/// <summary>
/// Minimal Markdown reader: YAML front matter, level-2 sections, heading anchors and links.
/// Fenced code blocks are ignored for headings and links.
/// </summary>
public sealed partial class MarkdownDocument
{
    private readonly string[] _bodyLines;

    private MarkdownDocument(string? frontMatter, string[] bodyLines, int bodyStartLine)
    {
        FrontMatter = frontMatter;
        _bodyLines = bodyLines;
        BodyStartLine = bodyStartLine;
    }

    /// <summary>The YAML between the opening and closing <c>---</c> lines, if present.</summary>
    public string? FrontMatter { get; }

    /// <summary>1-based line number of the first body line in the original file.</summary>
    public int BodyStartLine { get; }

    /// <summary>The Markdown body (everything after the front matter).</summary>
    public string Body => string.Join('\n', _bodyLines);

    /// <summary>Splits front matter from the body. Line endings are normalized to LF.</summary>
    public static MarkdownDocument Parse(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        if (lines.Length > 0 && lines[0].TrimEnd() == "---")
        {
            for (var i = 1; i < lines.Length; i++)
            {
                if (lines[i].TrimEnd() == "---")
                {
                    var frontMatter = string.Join('\n', lines[1..i]);
                    return new MarkdownDocument(frontMatter, lines[(i + 1)..], i + 2);
                }
            }
        }

        return new MarkdownDocument(null, lines, 1);
    }

    /// <summary>Level-2 sections with their text, excluding fenced code.</summary>
    public IReadOnlyList<MarkdownSection> Sections()
    {
        var sections = new List<MarkdownSection>();
        string? heading = null;
        var headingLine = 0;
        var text = new StringBuilder();

        foreach (var (line, number, inCode) in Walk())
        {
            if (!inCode && line.StartsWith("## ", StringComparison.Ordinal))
            {
                if (heading is not null)
                {
                    sections.Add(new MarkdownSection(heading, headingLine, text.ToString()));
                }

                heading = line[3..].Trim();
                headingLine = number;
                text.Clear();
            }
            else if (heading is not null)
            {
                text.Append(line).Append('\n');
            }
        }

        if (heading is not null)
        {
            sections.Add(new MarkdownSection(heading, headingLine, text.ToString()));
        }

        return sections;
    }

    /// <summary>GitHub-style anchor slugs for every heading (duplicates get -1, -2, ... suffixes).</summary>
    public IReadOnlySet<string> Anchors()
    {
        var anchors = new HashSet<string>(StringComparer.Ordinal);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (line, _, inCode) in Walk())
        {
            var match = inCode ? Match.Empty : HeadingPattern().Match(line);
            if (!match.Success)
            {
                continue;
            }

            var slug = Slug(match.Groups["text"].Value);
            if (counts.TryGetValue(slug, out var count))
            {
                counts[slug] = count + 1;
                anchors.Add(slug + "-" + (count + 1).ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                counts[slug] = 0;
                anchors.Add(slug);
            }
        }

        return anchors;
    }

    /// <summary>Links whose target is relative or an in-page anchor (external URLs are skipped).</summary>
    public IReadOnlyList<MarkdownLink> RelativeLinks()
    {
        var links = new List<MarkdownLink>();
        foreach (var (line, number, inCode) in Walk())
        {
            if (inCode)
            {
                continue;
            }

            var withoutInlineCode = InlineCodePattern().Replace(line, string.Empty);
            foreach (Match match in LinkPattern().Matches(withoutInlineCode))
            {
                var target = match.Groups["target"].Value;
                if (IsRelative(target))
                {
                    links.Add(new MarkdownLink(target, number));
                }
            }
        }

        return links;
    }

    /// <summary>Computes a GitHub-style heading anchor.</summary>
    public static string Slug(string headingText)
    {
        var builder = new StringBuilder(headingText.Length);
        foreach (var character in headingText.Trim())
        {
            if (char.IsLetterOrDigit(character) || character is '-' or '_')
            {
                builder.Append(char.ToLowerInvariant(character));
            }
            else if (character == ' ')
            {
                builder.Append('-');
            }
        }

        return builder.ToString();
    }

    private static bool IsRelative(string target) =>
        !target.Contains("://", StringComparison.Ordinal) &&
        !target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) &&
        !target.StartsWith("tel:", StringComparison.OrdinalIgnoreCase);

    private IEnumerable<(string Line, int Number, bool InCode)> Walk()
    {
        var inCode = false;
        for (var i = 0; i < _bodyLines.Length; i++)
        {
            var line = _bodyLines[i];
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                inCode = !inCode;
                yield return (line, BodyStartLine + i, true);
                continue;
            }

            yield return (line, BodyStartLine + i, inCode);
        }
    }

    [GeneratedRegex(@"^#{1,6}\s+(?<text>.+?)\s*#*\s*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(@"`[^`]*`", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex InlineCodePattern();

    [GeneratedRegex(@"\[[^\]]*\]\((?<target>[^)\s]+)(?:\s+""[^""]*"")?\)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex LinkPattern();
}
