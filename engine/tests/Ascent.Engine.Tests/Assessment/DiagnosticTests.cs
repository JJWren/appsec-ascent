using System.Text.Json.Nodes;
using Ascent.Assessment.Services;
using Ascent.Core.Domain;
using Ascent.Core.Errors;
using Ascent.Engine.Tests.TestSupport;
using Ascent.Tests.Shared;

namespace Ascent.Engine.Tests.Assessment;

/// <summary>The Diagnostic and the study plan (DX-01..03).</summary>
public sealed class DiagnosticTests
{
    private static readonly string[] Domains = ["D1", "D2", "D3", "D4", "D5", "D6", "D7", "D8"];

    [Fact]
    [Trait("Rule", "DX-01")]
    public void Forty_diagnostic_items_are_drawn_by_exam_weight()
    {
        using var fixture = Diagnostic(perDomain: 6);
        using var game = new GameHarness(fixture);

        var attempt = game.Diagnostic.StartOrResume();

        attempt.ItemIds.Count.ShouldBe(DiagnosticService.ItemCount);
        attempt.ItemIds.Distinct().Count().ShouldBe(40);
        attempt.ItemIds.ShouldAllBe(id => id.StartsWith("qb-dx-", StringComparison.Ordinal));
        var byDomain = attempt.ItemIds.GroupBy(id => id[6..8]).ToDictionary(g => g.Key, g => g.Count());
        Domains.Select(d => byDomain[d]).ShouldBe([5, 4, 5, 6, 6, 6, 4, 4]);
        game.Diagnostic.StartOrResume().Id.ShouldBe(attempt.Id);
    }

    [Fact]
    [Trait("Rule", "DX-01")]
    public void A_small_pool_is_used_whole_and_an_empty_pool_or_outline_is_explained()
    {
        using (var fixture = Diagnostic(perDomain: 2))
        using (var game = new GameHarness(fixture))
        {
            game.Diagnostic.StartOrResume().ItemIds.Count.ShouldBe(16);
        }

        using (var empty = CurriculumFixture.Create().Question("qb-1", "1.1"))
        using (var game = new GameHarness(empty))
        {
            Should.Throw<AscentException>(() => game.Diagnostic.StartOrResume()).Message.ShouldContain("no questions yet");
        }
    }

    [Fact]
    [Trait("Rule", "DX-01")]
    [Trait("Rule", "DX-02")]
    [Trait("Rule", "DX-03")]
    public void Weak_domains_get_more_weeks_and_the_plan_ends_with_two_simulation_weeks()
    {
        using var fixture = Diagnostic(perDomain: 5);
        foreach (var domain in Domains)
        {
            fixture.Quest("q-" + domain, domain[1] + ".1", domain, minutes: 300);
        }

        fixture.Lab("lab-d4-01", "4.1").Lab("lab-d5-01", "5.1");
        using var game = new GameHarness(fixture);
        var diagnostic = game.Diagnostic;
        var attempt = diagnostic.StartOrResume();

        // Every D1 answer is right; every other answer is wrong.
        foreach (var itemId in attempt.ItemIds.Take(30))
        {
            diagnostic.Answer(attempt, itemId, itemId.Contains("-D1-", StringComparison.Ordinal) ? "A" : "B");
        }

        game.Restart();
        diagnostic = game.Diagnostic;
        attempt = diagnostic.StartOrResume();
        diagnostic.Answered(attempt).Count.ShouldBe(30);
        foreach (var itemId in attempt.ItemIds.Skip(30))
        {
            diagnostic.Answer(attempt, itemId, itemId.Contains("-D1-", StringComparison.Ordinal) ? "A" : "B");
        }

        var start = new DateOnly(2026, 10, 5);
        var plan = diagnostic.Finish(attempt, id => game.Questions.Serve(id, ServeContext.Diagnostic), weeklyHours: 8, start);

        // 8 Quests × 5 h + 2 Labs × 1.5 h + 8 Boss Fights × 0.75 h + 6 h of Simulations = 55 h, so 7 weeks at 8 h.
        plan.EstimatedHours.ShouldBe(55);
        plan.TotalWeeks.ShouldBe(7);
        plan.Domains.Sum(d => d.Weeks).ShouldBe(7);
        plan.ExamWindowStart.ShouldBe(start.AddDays(7 * 9));
        plan.ExamWindowEnd.ShouldBe(start.AddDays(7 * 10));

        var d1 = plan.Domains.Single(d => d.Domain == "D1");
        d1.ScorePercent.ShouldBe(100);
        var sum = 12 + (1.25 * (11 + 13 + 15 + 14 + 14 + 11 + 10));
        d1.Factor.ShouldBe(12 / sum, 1e-9);
        plan.Domains.Single(d => d.Domain == "D8").Factor.ShouldBe(12.5 / sum, 1e-9);
        plan.Domains.Single(d => d.Domain == "D2").ScorePercent.ShouldBe(0);

        // Diagnostic items never become review cards, and the plan is stored with the attempt.
        game.Cards.All().ShouldBeEmpty();
        var stored = game.Attempts.LatestDiagnostic()!;
        stored.TakenUtc.ShouldNotBeNull();
        JsonNode.Parse(stored.Plan!)!["examWindowStart"]!.GetValue<string>().ShouldBe("2026-12-07");
        JsonNode.Parse(stored.PerDomain!)!["D1"]!.AsArray().Select(n => n!.GetValue<int>()).ShouldBe([5, 5]);
        game.Attempts.OpenDiagnostic().ShouldBeNull();
        PlanText.Date(plan.ExamWindowEnd).ShouldBe("2026-12-14");
    }

    [Fact]
    [Trait("Rule", "DX-03")]
    public void The_plan_needs_at_least_one_hour_a_week()
    {
        using var fixture = Diagnostic(perDomain: 1);
        using var game = new GameHarness(fixture);

        Should.Throw<ArgumentOutOfRangeException>(() => StudyPlanner.Plan(
            new Dictionary<string, (int, int)>(), CurriculumFixture.RealOutline, game.Catalog, weeklyHours: 0, new DateOnly(2026, 10, 5)));
        StudyPlanner.Plan(new Dictionary<string, (int, int)>(), CurriculumFixture.RealOutline, game.Catalog, 40, new DateOnly(2026, 10, 5)).TotalWeeks.ShouldBe(1);
    }

    private static CurriculumFixture Diagnostic(int perDomain)
    {
        var fixture = CurriculumFixture.Create();
        foreach (var domain in Domains)
        {
            for (var i = 1; i <= perDomain; i++)
            {
                fixture.Question("qb-dx-" + domain + "-" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture), domain[1] + ".1", pool: "diagnostic");
            }
        }

        return fixture;
    }
}
