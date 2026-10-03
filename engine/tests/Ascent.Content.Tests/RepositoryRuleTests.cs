using Ascent.Content.Model;

namespace Ascent.Content.Tests;

public sealed class LegalRuleTests
{
    [Fact]
    public void Readme_without_disclaimer_fails_LEG_01()
    {
        using var repo = TestRepo.Create().Write("README.md", "# AppSec Ascent\n");
        repo.Lint().ShouldContainRule("LEG-01");
    }

    [Fact]
    public void Missing_content_license_fails_LEG_02()
    {
        using var repo = TestRepo.Create().Delete("LICENSE-CONTENT");
        repo.Lint().ShouldContainRule("LEG-02");
    }

    [Fact]
    public void Exam_vendor_logo_warns_LEG_03()
    {
        using var repo = TestRepo.Create().Write("docs/images/isc2-logo.png", "not really an image");
        repo.Lint().ShouldContainRule("LEG-03", Severity.Warning);
    }

    [Fact]
    public void Contributing_without_exam_policy_fails_LEG_04()
    {
        using var repo = TestRepo.Create().Write("CONTRIBUTING.md", "# Contributing\nPull requests welcome.\n");
        repo.Lint().ShouldContainRule("LEG-04");
    }
}

public sealed class BundleRuleTests
{
    [Fact]
    public void Extra_header_field_fails_BND_02()
    {
        var bundle = Samples.Bundle("qb-1.1-001", "question", "practice", "1.1", "practice", examDomain: "D1")
            .Replace("\"formatVersion\": 1,", "\"formatVersion\": 1,\n    \"answer\": \"A\",", StringComparison.Ordinal);
        using var repo = TestRepo.Create().Write("sealed/questions/qb-1.1-001.bundle.json", bundle);
        var findings = repo.Lint();
        findings.ShouldContainRule("BND-02");
        findings.ShouldContainRule("SCH-01");
    }

    [Fact]
    public void Bundle_file_name_must_match_item_BND_03()
    {
        using var repo = TestRepo.Create().Write("sealed/questions/qb-1.1-002.bundle.json",
            Samples.Bundle("qb-1.1-001", "question", "practice", "1.1", "practice", examDomain: "D1"));
        repo.Lint().ShouldContainRule("BND-03");
    }
}

public sealed class LinkRuleTests
{
    [Fact]
    public void Broken_relative_link_fails_LNK_01()
    {
        using var repo = TestRepo.Create().Write("docs/guide.md", "# Guide\nSee [missing](missing.md).\n");
        repo.Lint().ShouldContainRule("LNK-01");
    }

    [Fact]
    public void Broken_anchor_fails_LNK_01()
    {
        using var repo = TestRepo.Create()
            .Write("docs/guide.md", "# Guide\nSee [setup](other.md#not-there).\n")
            .Write("docs/other.md", "# Other\n## Setup steps\n");
        repo.Lint().ShouldContainRule("LNK-01");
    }

    [Fact]
    public void Valid_links_and_anchors_pass()
    {
        using var repo = TestRepo.Create()
            .Write("docs/guide.md", "# Guide\nSee [setup](other.md#setup-steps), [top](#guide) and [site](https://example.com/x).\n```\n[ignored](nowhere.md)\n```\n")
            .Write("docs/other.md", "# Other\n## Setup steps\n");
        repo.Lint().ShouldNotContainRule("LNK-01");
    }
}

public sealed class RepoRuleTests
{
    [Fact]
    public void Gitignore_must_exclude_private_folders_REPO_01()
    {
        using var repo = TestRepo.Create().Write(".gitignore", "bin/\nobj/\n");
        repo.Lint().ShouldContainRule("REPO-01");
    }

    [Fact]
    public void Security_policy_must_scope_out_throughline_REPO_02()
    {
        using var repo = TestRepo.Create().Write("SECURITY.md", "# Security\nEmail us.\n");
        repo.Lint().ShouldContainRule("REPO-02");
    }

    [Fact]
    public void Content_bug_template_is_required_REPO_03()
    {
        using var repo = TestRepo.Create().Delete(".github/ISSUE_TEMPLATE/content-bug.yml");
        repo.Lint().ShouldContainRule("REPO-03");
    }

    [Theory]
    [InlineData("      - uses: actions/checkout@v4", true)]
    [InlineData("      - uses: actions/checkout@main", true)]
    [InlineData("      - uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1", false)]
    [InlineData("      - uses: ./.github/actions/local", false)]
    [InlineData("      - uses: docker://ghcr.io/aquasecurity/trivy:0.75.0", true)]
    [InlineData("      - uses: docker://ghcr.io/aquasecurity/trivy@sha256:af6acf9a6b85dfe389a1941505c0ce9efef52a4719635e1a962f022a3d855daa", false)]
    public void Actions_must_be_pinned_by_sha_REPO_04(string usesLine, bool flagged)
    {
        using var repo = TestRepo.Create().Write(".github/workflows/test.yml", "jobs:\n  x:\n    steps:\n" + usesLine + "\n");
        var findings = repo.Lint();
        findings.Any(f => f.RuleId == "REPO-04").ShouldBe(flagged);
    }
}
