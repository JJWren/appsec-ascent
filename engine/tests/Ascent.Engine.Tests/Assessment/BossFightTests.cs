using Ascent.Assessment.Questions;
using Ascent.Assessment.Services;
using Ascent.Core.Domain;
using Ascent.Core.Errors;
using Ascent.Core.Progress;
using Ascent.Engine.Tests.TestSupport;
using Ascent.Tests.Shared;

namespace Ascent.Engine.Tests.Assessment;

/// <summary>Boss Fights (BF-01..06) and the single active attempt (P17).</summary>
public sealed class BossFightTests
{
    [Fact]
    [Trait("Rule", "BF-01")]
    public void Thirty_items_spread_equally_with_leftovers_to_the_weakest_objectives()
    {
        using var fixture = Pool("D2", perObjective: 5);
        using var game = new GameHarness(fixture);
        var objectives = CurriculumFixture.ObjectivesOf("D2");
        objectives.Count.ShouldBe(9);

        // Objectives 2.1–2.6 have been reviewed correctly; 2.7–2.9 have no mastery yet.
        foreach (var objective in objectives.Take(6))
        {
            game.Reviews.Record(game.Catalog.Questions.First(q => q.ObjectiveId == objective), true, ReviewContext.StandUp, 0);
        }

        var attempt = game.Bosses.StartOrResume("D2");

        attempt.ItemIds.Count.ShouldBe(BossFightService.ItemCount);
        attempt.ItemIds.Distinct().Count().ShouldBe(30);
        var perObjective = attempt.ItemIds.GroupBy(ObjectiveOf).ToDictionary(g => g.Key, g => g.Count());
        objectives.Select(o => perObjective[o]).ShouldBe([3, 3, 3, 3, 3, 3, 4, 4, 4]);
        attempt.Kind.ShouldBe(BossKind.First);
        attempt.DeadlineUtc.ShouldBe(attempt.StartedUtc + TimeSpan.FromMinutes(45));
    }

    [Fact]
    [Trait("Rule", "BF-02")]
    [Trait("Rule", "BF-03")]
    public void Answers_after_the_deadline_are_refused_and_unanswered_items_count_as_wrong()
    {
        using var fixture = Pool("D1", perObjective: 15);
        using var game = new GameHarness(fixture);
        var bosses = game.Bosses;
        var attempt = bosses.StartOrResume("D1");

        foreach (var itemId in attempt.ItemIds.Take(25))
        {
            bosses.Answer(attempt, itemId, "A", game.Clock.GetUtcNow()).ShouldBeTrue();
        }

        game.Clock.Advance(TimeSpan.FromMinutes(46));
        bosses.Answer(attempt, attempt.ItemIds[25], "A", game.Clock.GetUtcNow()).ShouldBeFalse();
        bosses.Answered(attempt).Count.ShouldBe(25);

        var result = bosses.Finish(attempt, Serve(game));

        result.ShouldBe(result with { ExamDomain = "D1", Correct = 25, Total = 30, ScorePercent = 83, Passed = true, XpAwarded = 100 });
        result.Items.Count(i => i.Answer is null).ShouldBe(5);
        result.Items.Where(i => i.Answer is null).ShouldAllBe(i => !i.Correct);
        game.Ledger.Total.ShouldBe(100);
    }

    [Fact]
    [Trait("Rule", "BF-03")]
    [Trait("Rule", "BF-04")]
    [Trait("Rule", "BF-05")]
    public void A_failure_schedules_a_rematch_and_a_later_pass_earns_sixty_once()
    {
        using var fixture = Pool("D1", perObjective: 15);
        using var game = new GameHarness(fixture);

        var first = Fight(game, "D1", correct: 20);
        first.Passed.ShouldBeFalse();
        first.ScorePercent.ShouldBe(67);
        first.XpAwarded.ShouldBe(0);
        game.Bosses.RematchDomains().ShouldBe(["D1"]);

        var second = Fight(game, "D1", correct: 21);
        second.Passed.ShouldBeTrue();
        second.XpAwarded.ShouldBe(60);
        game.Bosses.RematchDomains().ShouldBeEmpty();
        game.Attempts.BossAttempts("D1")[1].Kind.ShouldBe(BossKind.Rematch);

        Fight(game, "D1", correct: 30).XpAwarded.ShouldBe(0);
        game.Ledger.Total.ShouldBe(60);
        game.Bosses.BestScores().ShouldBe(new Dictionary<string, int> { ["D1"] = 100 });
    }

    [Fact]
    [Trait("Rule", "BF-04")]
    public void With_sixty_or_more_items_the_previous_attempts_items_are_left_out()
    {
        using (var fixture = Pool("D1", perObjective: 30))
        using (var game = new GameHarness(fixture))
        {
            var first = Fight(game, "D1", correct: 0);
            var second = game.Bosses.StartOrResume("D1");
            second.ItemIds.Intersect(first.Items.Select(i => i.ItemId)).ShouldBeEmpty();
        }

        using (var small = Pool("D1", perObjective: 15))
        using (var game = new GameHarness(small))
        {
            var first = Fight(game, "D1", correct: 0);
            var second = game.Bosses.StartOrResume("D1");
            second.ItemIds.Order().ShouldBe(first.Items.Select(i => i.ItemId).Order());
        }
    }

    [Fact]
    [Trait("Rule", "BF-06")]
    public void Boss_fight_answers_update_review_cards()
    {
        using var fixture = Pool("D1", perObjective: 15);
        using var game = new GameHarness(fixture);
        var bosses = game.Bosses;
        var attempt = bosses.StartOrResume("D1");
        bosses.Answer(attempt, attempt.ItemIds[0], "A", game.Clock.GetUtcNow());
        bosses.Answer(attempt, attempt.ItemIds[1], "B", game.Clock.GetUtcNow());

        bosses.Finish(attempt, Serve(game));

        game.Cards.All().Select(c => c.ItemId).ShouldBe([attempt.ItemIds[0], attempt.ItemIds[1]], ignoreOrder: true);
        game.Cards.Reviews().ShouldAllBe(r => r.Context == ReviewContext.BossFight);
        game.Cards.Reviews().Select(r => r.Correct).ShouldBe([true, false]);
    }

    [Fact]
    public void Only_one_timed_attempt_runs_at_a_time_and_it_survives_a_restart()
    {
        using var fixture = Pool("D1", perObjective: 15);
        fixture.Question("qb-2.1-001", "2.1");
        using var game = new GameHarness(fixture);
        var attempt = game.Bosses.StartOrResume("D1");
        game.Bosses.Answer(attempt, attempt.ItemIds[0], "A", game.Clock.GetUtcNow());

        game.Restart();

        var resumed = game.Bosses.StartOrResume("D1");
        resumed.Id.ShouldBe(attempt.Id);
        resumed.ItemIds.ShouldBe(attempt.ItemIds);
        game.Bosses.Answered(resumed).ShouldBe([attempt.ItemIds[0]]);
        Should.Throw<UsageException>(() => game.Bosses.StartOrResume("D2")).Message.ShouldContain("D1 Boss Fight is still in progress");
        Should.Throw<InvalidOperationException>(() => game.Attempts.StartBoss("D2", BossKind.First, game.Clock.GetUtcNow(), game.Clock.GetUtcNow(), ["x"]));
        Should.Throw<InvalidOperationException>(() => game.Attempts.StartSimulation("A", game.Clock.GetUtcNow(), game.Clock.GetUtcNow(), ["x"], false));
    }

    [Fact]
    public void A_domain_without_questions_has_no_boss_fight_yet()
    {
        using var fixture = Pool("D1", perObjective: 1);
        using var game = new GameHarness(fixture);

        Should.Throw<AscentException>(() => game.Bosses.StartOrResume("D5")).Message.ShouldBe("The D5 Boss Fight has no questions yet.");
        Should.Throw<ArgumentException>(() => game.Bosses.StartOrResume(" "));
    }

    internal static CurriculumFixture Pool(string examDomain, int perObjective)
    {
        var fixture = CurriculumFixture.Create();
        foreach (var objective in CurriculumFixture.ObjectivesOf(examDomain))
        {
            for (var i = 1; i <= perObjective; i++)
            {
                fixture.Question("qb-" + objective + "-" + i.ToString("000", System.Globalization.CultureInfo.InvariantCulture), objective);
            }
        }

        return fixture;
    }

    internal static Func<string, Question> Serve(GameHarness game) => id => game.Questions.Serve(id, ServeContext.BossFight);

    // Answers the first `correct` items right (A) and the rest wrong (B), then scores the attempt.
    internal static BossResult Fight(GameHarness game, string examDomain, int correct)
    {
        var bosses = game.Bosses;
        var attempt = bosses.StartOrResume(examDomain);
        for (var i = 0; i < attempt.ItemIds.Count; i++)
        {
            bosses.Answer(attempt, attempt.ItemIds[i], i < correct ? "A" : "B", game.Clock.GetUtcNow());
        }

        var result = bosses.Finish(attempt, Serve(game));
        game.Clock.Advance(TimeSpan.FromHours(1));
        return result;
    }

    private static string ObjectiveOf(string itemId) => itemId.Split('-')[1];
}
