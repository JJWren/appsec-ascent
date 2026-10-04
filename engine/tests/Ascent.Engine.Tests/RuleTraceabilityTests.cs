using System.Text.RegularExpressions;
using Ascent.Tests.Shared;

namespace Ascent.Engine.Tests;

/// <summary>
/// Rule-ID traceability (P25, TEST-U2-02): every rule in <c>docs/engine/rules.md</c> has a test tagged with its ID, and
/// every tag names a rule there. The scan reads test sources, so it covers every test project.
/// </summary>
public sealed partial class RuleTraceabilityTests
{
    private static string RulesPath => Path.Join(CurriculumFixture.RealRoot, "docs", "engine", "rules.md");

    [Fact]
    public void Every_rule_has_a_test_and_every_tag_names_a_rule()
    {
        var rules = DocumentedRules();
        var tagged = TaggedRules();

        // A guard against a parsing slip that finds nothing and so checks nothing.
        rules.Count.ShouldBeGreaterThanOrEqualTo(45);
        rules.ShouldContain("XP-01");
        rules.ShouldContain("BAK-01");
        rules.Except(tagged).Order(StringComparer.Ordinal).ShouldBeEmpty("these rules have no test tagged with their ID");
        tagged.Except(rules).Order(StringComparer.Ordinal).ShouldBeEmpty("these tags name rules that aren't in docs/engine/rules.md");
    }

    [Fact]
    public void Rule_ids_are_unique()
    {
        var ids = RuleRow().Matches(File.ReadAllText(RulesPath)).Select(m => m.Groups["id"].Value).ToList();

        ids.GroupBy(id => id, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ShouldBeEmpty();
    }

    [Fact]
    [Trait("Rule", "SEAL-06")]
    public void The_docs_say_sealing_is_a_spoiler_shield_not_drm()
    {
        File.ReadAllText(RulesPath).ShouldContain("This is a spoiler shield, not DRM.");
        File.ReadAllText(Path.Join(CurriculumFixture.RealRoot, "docs", "adr", "0005-sealing-mechanism.md")).ShouldContain("spoiler shield against accidental exposure, not DRM");
    }

    private static HashSet<string> DocumentedRules() =>
        RuleRow().Matches(File.ReadAllText(RulesPath)).Select(m => m.Groups["id"].Value).ToHashSet(StringComparer.Ordinal);

    private static HashSet<string> TaggedRules() =>
        Directory.EnumerateFiles(Path.Join(CurriculumFixture.RealRoot, "engine", "tests"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .SelectMany(path => RuleTrait().Matches(File.ReadAllText(path)).Select(m => m.Groups["id"].Value))
            .ToHashSet(StringComparer.Ordinal);

    [GeneratedRegex(@"^\|\s*(?<id>[A-Z][A-Z0-9]*-\d{2})\s*\|", RegexOptions.Multiline | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex RuleRow();

    [GeneratedRegex(@"\[Trait\(""Rule"",\s*""(?<id>[^""]+)""\)\]", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex RuleTrait();
}
