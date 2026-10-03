using Ascent.Content.Coverage;
using Ascent.Content.Loading;
using Ascent.Content.Model;
using Ascent.Content.Reporting;
using Ascent.Content.Rules;
using Ascent.Content.Security;

namespace Ascent.Content.Tests;

public sealed class CoverageTests
{
    [Fact]
    public void Empty_repository_reports_zero_of_58()
    {
        using var repo = TestRepo.Create();
        var report = CoverageCalculator.Calculate(repo.Load())!;
        report.ObjectivesTotal.ShouldBe(58);
        report.ObjectivesComplete.ShouldBe(0);
        report.AiTopicsTotal.ShouldBe(41);
    }

    [Fact]
    public void Counts_quests_activities_and_question_headers()
    {
        using var repo = Samples.FullyValid();
        for (var i = 1; i <= 15; i++)
        {
            var id = FormattableString.Invariant($"qb-1.1-{i:000}");
            repo.Write("sealed/questions/" + id + ".bundle.json",
                Samples.Bundle(id, "question", "practice", "1.1", "practice", aiTopic: i == 1 ? "data-poisoning" : null, examDomain: "D1"));
        }

        var report = CoverageCalculator.Calculate(repo.Load())!;
        var objective = report.Objectives.Single(o => o.ObjectiveId == "1.1");
        objective.Quests.ShouldBe(1);
        objective.Drills.ShouldBe(1);
        objective.Practice.ShouldBe(15);
        objective.IsComplete.ShouldBeTrue();
        report.Domains.Single(d => d.DomainId == "D1").AiTopicsCovered.ShouldBe(1);
    }

    [Fact]
    public void Gate_reports_incomplete_objectives()
    {
        using var repo = Samples.FullyValid();
        var gaps = CoverageCalculator.GateGaps(CoverageCalculator.Calculate(repo.Load())!, "d1");
        gaps.ShouldContain(g => g.Contains("1.1", StringComparison.Ordinal));
        gaps.ShouldContain(g => g.Contains("1.2", StringComparison.Ordinal));
    }

    [Fact]
    public void Simulation_targets_follow_domain_weights()
    {
        using var repo = TestRepo.Create();
        var report = CoverageCalculator.Calculate(repo.Load())!;
        report.Domains.Single(d => d.DomainId == "D4").SimulationTarget.ShouldBe(37.5);
        report.Domains.Sum(d => d.SimulationTarget).ShouldBe(250, tolerance: 0.001);
    }
}

public sealed class ExceptionRegisterTests
{
    private static readonly DateOnly Today = new(2026, 10, 3);

    private static TestRepo WithEntry(string expires, string scanner = "trivy", string finding = "CVE-2026-12345") =>
        TestRepo.Create().Write(ExceptionRegister.RegisterPath, $"""
            exceptions:
              - id: EXC-001
                scanner: {scanner}
                finding: {finding}
                justification: Not reachable from the Engine; upstream fix expected next release.
                owner: maintainer
                expires: "{expires}"
            """);

    [Fact]
    public void Empty_register_passes()
    {
        using var repo = TestRepo.Create();
        ExceptionRegister.Check(repo.Load(), Today).ShouldBeEmpty();
    }

    [Fact]
    public void Expired_entry_fails_EXC_01()
    {
        using var repo = WithEntry("2026-10-02");
        ExceptionRegister.Check(repo.Load(), Today).ShouldContainRule("EXC-01");
    }

    [Fact]
    public void Entry_beyond_90_days_fails_EXC_02()
    {
        using var repo = WithEntry("2027-02-01");
        ExceptionRegister.Check(repo.Load(), Today).ShouldContainRule("EXC-02");
    }

    [Fact]
    public void Missing_register_fails()
    {
        using var repo = TestRepo.Create().Delete(ExceptionRegister.RegisterPath);
        ExceptionRegister.Check(repo.Load(), Today).ShouldContainRule("EXC-00");
    }

    [Fact]
    public void Emits_trivy_ignore_for_active_entries_only()
    {
        using var repo = WithEntry("2026-12-01");
        var entries = ExceptionRegister.Read(repo.Load());
        ExceptionRegister.TrivyIgnore(entries, Today).ShouldContain("CVE-2026-12345");
        ExceptionRegister.TrivyIgnore(entries, new DateOnly(2026, 12, 2)).ShouldNotContain("CVE-2026-12345");
    }

    [Fact]
    public void Emits_nuget_suppression_with_advisory_url()
    {
        using var repo = WithEntry("2026-12-01", scanner: "nuget", finding: "GHSA-aaaa-bbbb-cccc");
        var props = ExceptionRegister.NuGetSuppressions(ExceptionRegister.Read(repo.Load()), Today);
        props.ShouldContain("<NuGetAuditSuppress Include=\"https://github.com/advisories/GHSA-aaaa-bbbb-cccc\" />");
    }
}

public sealed class DeterminismTests
{
    [Fact]
    public void Lint_output_is_identical_across_runs()
    {
        using var repo = Samples.FullyValid().Write("docs/guide.md", "# Guide\n[a](missing.md)\n[b](also-missing.md)\n");
        var first = JsonOutput.Serialize(new FindingsReport(repo.Lint()));
        var second = JsonOutput.Serialize(new FindingsReport(repo.Lint()));
        second.ShouldBe(first);
    }

    [Fact]
    public void Findings_are_sorted_by_path_then_line()
    {
        using var repo = TestRepo.Create().Write("docs/b.md", "# B\n[x](nope.md)\n").Write("docs/a.md", "# A\n\n[x](nope.md)\n[y](nope2.md)\n");
        var findings = repo.Lint();
        findings.Select(f => (f.Path, f.Line ?? 0)).ShouldBe(findings.Select(f => (f.Path, f.Line ?? 0)).OrderBy(x => x.Path, StringComparer.Ordinal).ThenBy(x => x.Item2));
    }
}

public sealed class RealRepositoryTests
{
    [Fact]
    public void The_repository_itself_lints_without_errors()
    {
        var findings = new RuleEngine().Run(ContentLoader.Load(TestRepo.RealRoot));
        findings.ShouldHaveNoErrors();
    }

    [Fact]
    public void Every_rule_id_is_unique()
    {
        RuleEngine.DefaultRules.Select(r => r.Id).ShouldBeUnique();
    }

    [Fact]
    public void The_real_outline_matches_the_published_structure()
    {
        var outline = ContentLoader.Load(TestRepo.RealRoot).Outline!;
        outline.Version.ShouldBe("2023-09-15");
        outline.Domains.Select(d => d.Objectives.Count).ShouldBe([2, 9, 8, 7, 6, 8, 13, 5]);
        outline.Domains.Sum(d => d.Weight).ShouldBe(100);
        outline.Domains.ShouldAllBe(d => d.AiGuidanceTopics.Count >= 5);
    }
}
