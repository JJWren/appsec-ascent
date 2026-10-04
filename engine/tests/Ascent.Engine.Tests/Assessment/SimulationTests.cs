using System.Globalization;
using Ascent.Assessment.Questions;
using Ascent.Assessment.Services;
using Ascent.Core.Domain;
using Ascent.Core.Errors;
using Ascent.Core.Progress;
using Ascent.Engine.Tests.TestSupport;
using Ascent.Sealing;
using Ascent.Tests.Shared;

namespace Ascent.Engine.Tests.Assessment;

/// <summary>Simulations (SIM-01..06): the unlock gate, fixed forms, the running clock, resume and expiry.</summary>
public sealed class SimulationTests
{
    // Each Domain's share of 125 (largest remainder over the weights 12/11/13/15/14/14/11/10), twice over.
    private static readonly Dictionary<string, int> Quotas = new()
    {
        ["D1"] = 15,
        ["D2"] = 14,
        ["D3"] = 16,
        ["D4"] = 19,
        ["D5"] = 18,
        ["D6"] = 17,
        ["D7"] = 14,
        ["D8"] = 12,
    };

    [Fact]
    [Trait("Rule", "SIM-01")]
    public void Simulations_unlock_when_every_boss_fight_reaches_seventy()
    {
        using var fixture = Reserved();
        using var game = new GameHarness(fixture);

        game.Simulations.LockedBy(new Dictionary<string, int>()).ShouldBe(["D1", "D2", "D3", "D4", "D5", "D6", "D7", "D8"]);
        foreach (var domain in Quotas.Keys)
        {
            game.RecordBossScore(domain, domain == "D6" ? 69 : 70);
        }

        game.Simulations.LockedBy(game.Bosses.BestScores()).ShouldBe(["D6"]);
        game.RecordBossScore("D6", 90);
        game.Simulations.LockedBy(game.Bosses.BestScores()).ShouldBeEmpty();
    }

    [Fact]
    [Trait("Rule", "SIM-02")]
    public void Forms_a_and_b_are_fixed_partitions_by_domain_weight()
    {
        using var fixture = Reserved();
        using var game = new GameHarness(fixture);

        var (a, b) = game.Simulations.Forms();
        var (again, _) = game.Simulations.Forms();

        a.Count.ShouldBe(SimulationService.FormSize);
        b.Count.ShouldBe(SimulationService.FormSize);
        a.Intersect(b).ShouldBeEmpty();
        a.ShouldBe(again);
        a.GroupBy(DomainOf).ToDictionary(g => g.Key, g => g.Count()).ShouldBe(Quotas, ignoreOrder: true);
        a.Concat(b).ShouldAllBe(id => id.StartsWith("qb-sim-", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Rule", "SIM-02")]
    public void A_short_reserved_pool_names_the_shortfall()
    {
        using var fixture = Reserved(missingFromD8: 3);
        using var game = new GameHarness(fixture);

        Should.Throw<AscentException>(() => game.Simulations.Forms()).Message.ShouldBe("The Simulation pool needs 3 more item(s) before its forms are complete.");
    }

    [Fact]
    [Trait("Rule", "SIM-03")]
    [Trait("Rule", "SIM-04")]
    public void The_clock_keeps_running_across_a_restart_and_the_attempt_expires_at_the_deadline()
    {
        using var fixture = Reserved();
        using var game = new GameHarness(fixture);
        var attempt = game.Simulations.Start(null);
        attempt.Form.ShouldBe("A");
        attempt.DeadlineUtc.ShouldBe(attempt.StartedUtc + TimeSpan.FromHours(3));

        foreach (var itemId in attempt.ItemIds.Take(50))
        {
            game.Simulations.Answer(attempt, itemId, "A", game.Clock.GetUtcNow()).ShouldBeTrue();
        }

        // The Engine stops; time passes; it starts again and resumes the same attempt.
        game.Restart();
        game.Clock.Advance(TimeSpan.FromHours(2));
        var resumed = game.Simulations.Active()!;
        resumed.Id.ShouldBe(attempt.Id);
        game.Simulations.Answered(resumed).Count.ShouldBe(50);
        foreach (var itemId in resumed.ItemIds.Skip(50).Take(40))
        {
            game.Simulations.Answer(resumed, itemId, "A", game.Clock.GetUtcNow()).ShouldBeTrue();
        }

        game.Clock.Advance(TimeSpan.FromMinutes(61));
        game.Simulations.Answer(resumed, resumed.ItemIds[90], "A", game.Clock.GetUtcNow()).ShouldBeFalse();

        var result = game.Simulations.Finish(resumed, Serve(game, resumed.Id), expired: true);

        result.Status.ShouldBe(SimulationStatus.Expired);
        result.Correct.ShouldBe(90);
        result.Total.ShouldBe(125);
        result.ScorePercent.ShouldBe(72);
        result.ReachedTarget.ShouldBeTrue();
        game.Simulations.Active().ShouldBeNull();
        game.Attempts.SimulationAttempts().Single().ScorePercent.ShouldBe(72);
    }

    [Fact]
    [Trait("Rule", "SIM-04")]
    [Trait("Rule", "SIM-05")]
    public void Exam_ready_counts_only_first_attempts_of_both_forms()
    {
        using var fixture = Reserved();
        using var game = new GameHarness(fixture);

        Take(game, null, correct: 100).Form.ShouldBe("A");
        game.Simulations.ExamReady().ShouldBeFalse();

        var failedB = Take(game, null, correct: 80);
        failedB.Form.ShouldBe("B");
        failedB.ScorePercent.ShouldBe(64);
        failedB.ReachedTarget.ShouldBeFalse();
        game.Simulations.ExamReady().ShouldBeFalse();

        Should.Throw<UsageException>(() => game.Simulations.Start(null)).NextStep!.ShouldContain("ascent sim A");
        Should.Throw<UsageException>(() => game.Simulations.Start("C"));

        // A retake of B is marked as seen, so it can't make the Learner Exam Ready.
        Take(game, "b", correct: 125).Form.ShouldBe("B");
        game.Attempts.SimulationAttempts().Select(s => s.SeenForm).ShouldBe([false, false, true]);
        game.Simulations.ExamReady().ShouldBeFalse();
    }

    [Fact]
    [Trait("Rule", "SIM-05")]
    public void Passing_both_first_attempts_makes_the_learner_exam_ready()
    {
        using var fixture = Reserved();
        using var game = new GameHarness(fixture);

        Take(game, "B", correct: 88);
        Take(game, "A", correct: 90);

        game.Simulations.ExamReady().ShouldBeTrue();
    }

    [Fact]
    [Trait("Rule", "SIM-06")]
    [Trait("Rule", "SU-02")]
    public void Simulation_items_open_only_inside_their_attempt_and_never_become_cards()
    {
        using var fixture = Reserved();
        fixture.Question("qb-1.1-001", "1.1");
        using var game = new GameHarness(fixture);
        var attempt = game.Simulations.Start("A");
        var other = attempt.ItemIds[0];
        var notInForm = game.Simulations.Forms().B[0];

        game.Questions.ServeInSimulation(other, attempt.Id).Pool.ShouldBe("simulation");
        Should.Throw<SealedItemUnavailableException>(() => game.Questions.ServeInSimulation(notInForm, attempt.Id));
        Should.Throw<SealedItemUnavailableException>(() => game.Questions.Serve(other, ServeContext.StandUp));
        Should.Throw<SealedItemUnavailableException>(() => game.Questions.Serve(other, ServeContext.BossFight));

        game.Simulations.Finish(attempt, Serve(game, attempt.Id), expired: false);

        Should.Throw<SealedItemUnavailableException>(() => game.Questions.ServeInSimulation(other, attempt.Id));
        game.Cards.All().ShouldBeEmpty();
        StandUpPlanner.Plan(game.Clock.GetUtcNow(), [], game.Catalog.Questions, new HashSet<string> { "1.1" }, new HashSet<string>(), 0).New.ShouldBe(["qb-1.1-001"]);
    }

    [Fact]
    public void A_timed_attempt_blocks_the_other_kind()
    {
        using var fixture = Reserved();
        fixture.Question("qb-1.1-001", "1.1");
        using var game = new GameHarness(fixture);
        game.Simulations.Start("A");

        Should.Throw<UsageException>(() => game.Bosses.StartOrResume("D1")).NextStep.ShouldBe("Finish it first with 'ascent sim'.");
        Should.Throw<InvalidOperationException>(() => game.Simulations.Start("B"));
    }

    internal static CurriculumFixture Reserved(int missingFromD8 = 0)
    {
        var fixture = CurriculumFixture.Create();
        foreach (var (domain, quota) in Quotas)
        {
            var count = (2 * quota) - (domain == "D8" ? missingFromD8 : 0);
            for (var i = 1; i <= count; i++)
            {
                fixture.Question("qb-sim-" + domain + "-" + i.ToString("000", CultureInfo.InvariantCulture), domain[1] + ".1", pool: "simulation");
            }
        }

        return fixture;
    }

    private static Func<string, Question> Serve(GameHarness game, long attemptId) => id => game.Questions.ServeInSimulation(id, attemptId);

    // Takes a whole form: the first `correct` answers right, the rest wrong.
    private static SimulationResult Take(GameHarness game, string? form, int correct)
    {
        var attempt = game.Simulations.Start(form);
        for (var i = 0; i < attempt.ItemIds.Count; i++)
        {
            game.Simulations.Answer(attempt, attempt.ItemIds[i], i < correct ? "A" : "B", game.Clock.GetUtcNow());
        }

        var result = game.Simulations.Finish(attempt, Serve(game, attempt.Id), expired: false);
        game.Clock.Advance(TimeSpan.FromDays(1));
        return result;
    }

    private static string DomainOf(string itemId) => itemId.Split('-')[2];
}
