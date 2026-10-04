using System.Text;
using Ascent.Assessment.Services;
using Ascent.Core.Curriculum;
using Ascent.Core.Domain;
using Ascent.Core.Errors;
using Ascent.Core.Progress;
using Ascent.Engine.Tests.TestSupport;
using Ascent.Sealing;
using Ascent.Tests.Shared;

namespace Ascent.Engine.Tests.Assessment;

/// <summary>The Stand-up composer, serving through the sealing pipeline, and review recording (SU-01..05, BF-05).</summary>
public sealed class StandUpTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    [Trait("Rule", "SU-01")]
    public void Due_cards_come_first_oldest_first_up_to_twenty()
    {
        var questions = Enumerable.Range(1, 30).Select(i => Info("qb-" + i.ToString("00"), "1.1")).ToList();
        var cards = questions.Select((q, i) => Card(q.ItemId, "1.1", Now.AddHours(-i))).ToList();
        cards.Add(Card("qb-future", "1.1", Now.AddDays(1)));

        var plan = StandUpPlanner.Plan(Now, cards, questions, new HashSet<string>(), new HashSet<string>(), 0);

        plan.Due.Count.ShouldBe(StandUpPlanner.MaxDue);
        plan.Due[0].ShouldBe("qb-30");
        plan.Due[^1].ShouldBe("qb-11");
        plan.New.ShouldBeEmpty();
        plan.All.ShouldBe(plan.Due);
    }

    [Fact]
    [Trait("Rule", "SU-01")]
    public void New_cards_come_from_finished_lessons_up_to_five_a_day()
    {
        var questions = new[] { "1.2", "1.1", "2.1" }
            .SelectMany(o => Enumerable.Range(1, 4).Select(i => Info("qb-" + o + "-" + i.ToString("00"), o)))
            .ToList();
        var lessons = new HashSet<string> { "1.1", "1.2" };

        var plan = StandUpPlanner.Plan(Now, [], questions, lessons, new HashSet<string>(), 0);
        plan.New.ShouldBe(["qb-1.1-01", "qb-1.1-02", "qb-1.1-03", "qb-1.1-04", "qb-1.2-01"]);

        StandUpPlanner.Plan(Now, [], questions, lessons, new HashSet<string>(), introducedToday: 3).New.Count.ShouldBe(2);
        StandUpPlanner.Plan(Now, [], questions, lessons, new HashSet<string>(), introducedToday: 9).New.ShouldBeEmpty();

        var known = new List<ReviewCardRecord> { Card("qb-1.1-01", "1.1", Now.AddDays(3)) };
        StandUpPlanner.Plan(Now, known, questions, lessons, new HashSet<string>(), 0).New[0].ShouldBe("qb-1.1-02");
    }

    [Fact]
    [Trait("Rule", "SU-01")]
    [Trait("Rule", "BF-05")]
    public void Domains_flagged_for_a_rematch_come_first()
    {
        var questions = new List<QuestionInfo> { Info("qb-a", "1.1"), Info("qb-b", "4.1"), Info("qb-c", "1.2"), Info("qb-d", "4.2") };
        var cards = new List<ReviewCardRecord> { Card("qb-a", "1.1", Now.AddDays(-5)), Card("qb-b", "4.1", Now.AddDays(-1)) };

        var plan = StandUpPlanner.Plan(Now, cards, questions, new HashSet<string> { "1.2", "4.2" }, new HashSet<string> { "D4" }, 0);

        plan.Due.ShouldBe(["qb-b", "qb-a"]);
        plan.New.ShouldBe(["qb-d", "qb-c"]);
    }

    [Fact]
    [Trait("Rule", "SU-02")]
    [Trait("Rule", "SU-05")]
    public void Only_unsuspended_practice_items_are_planned()
    {
        var questions = new List<QuestionInfo>
        {
            Info("qb-practice", "1.1"),
            Info("qb-diagnostic", "1.1", "diagnostic"),
            Info("qb-simulation", "1.1", "simulation"),
            Info("qb-suspended", "1.1"),
        };
        var cards = new List<ReviewCardRecord>
        {
            Card("qb-practice", "1.1", Now.AddDays(-1)),
            Card("qb-diagnostic", "1.1", Now.AddDays(-1)),
            Card("qb-suspended", "1.1", Now.AddDays(-1)) with { Suspended = true },
            Card("qb-retired", "1.1", Now.AddDays(-1)),
        };

        var plan = StandUpPlanner.Plan(Now, cards, questions, new HashSet<string> { "1.1" }, new HashSet<string>(), 0);

        plan.Due.ShouldBe(["qb-practice"]);
        plan.New.ShouldBeEmpty();
    }

    [Fact]
    [Trait("Rule", "SU-02")]
    [Trait("Rule", "SEAL-03")]
    [Trait("Rule", "SEAL-05")]
    public void Questions_are_served_only_in_their_own_context_and_each_release_is_recorded()
    {
        using var fixture = CurriculumFixture.Create()
            .Question("qb-1.1-001", "1.1")
            .Question("qb-dx-001", "1.1", pool: "diagnostic")
            .Question("qb-sim-001", "1.1", pool: "simulation");
        using var game = new GameHarness(fixture);
        var questions = game.Questions;

        var served = questions.Serve("qb-1.1-001", ServeContext.StandUp);
        served.Stem.ShouldBe("Stem of qb-1.1-001?");
        questions.Serve("qb-1.1-001", ServeContext.BossFight).Id.ShouldBe("qb-1.1-001");
        questions.Serve("qb-dx-001", ServeContext.Diagnostic).Pool.ShouldBe("diagnostic");

        Should.Throw<SealedItemUnavailableException>(() => questions.Serve("qb-dx-001", ServeContext.StandUp));
        Should.Throw<SealedItemUnavailableException>(() => questions.Serve("qb-1.1-001", ServeContext.Diagnostic));
        Should.Throw<SealedItemUnavailableException>(() => questions.Serve("qb-sim-001", ServeContext.StandUp));
        Should.Throw<SealedItemUnavailableException>(() => questions.ServeInSimulation("qb-sim-001", 1));

        var releases = new Ascent.Storage.KeyReleaseStore(game.Database);
        releases.IsReleased("qb-1.1-001", SealTier.Practice).ShouldBeTrue();
        releases.IsReleased("qb-sim-001", SealTier.Simulation).ShouldBeFalse();
    }

    [Fact]
    public void A_question_that_cannot_be_read_is_reported_as_a_content_bug()
    {
        using var fixture = CurriculumFixture.Create().Seal("qb-bad", "question", SealTier.Practice, Encoding.UTF8.GetBytes("not json"), "1.1", "D1", "practice");
        using var game = new GameHarness(fixture);

        var error = Should.Throw<AscentException>(() => game.Questions.Serve("qb-bad", ServeContext.StandUp));

        error.Message.ShouldBe("The question 'qb-bad' couldn't be read.");
        error.ExitCode.ShouldBe(ExitCodes.CheckFailed);
    }

    [Fact]
    [Trait("Rule", "SU-04")]
    public void Recording_a_review_creates_the_card_and_logs_the_answer()
    {
        using var fixture = CurriculumFixture.Create();
        using var game = new GameHarness(fixture);
        var reviews = game.Reviews;

        var card = reviews.Record(Info("qb-1", "1.1"), correct: true, ReviewContext.StandUp, 1234);
        reviews.Record(Info("qb-2", "1.2"), correct: false, ReviewContext.Quest, 10);
        reviews.Record(Info("qb-1", "1.1"), correct: false, ReviewContext.BossFight, 0);

        card.DueUtc.ShouldBeGreaterThan(game.Clock.GetUtcNow());
        game.Cards.All().Select(c => c.ItemId).ShouldBe(["qb-2", "qb-1"], ignoreOrder: true);
        game.Cards.Reviews().Select(r => (r.ItemId, r.Context, r.Correct, r.ElapsedMs)).ShouldBe(
            [("qb-1", ReviewContext.StandUp, true, 1234L), ("qb-2", ReviewContext.Quest, false, 10L), ("qb-1", ReviewContext.BossFight, false, 0L)]);
        reviews.Mastery([Info("qb-1", "1.1"), Info("qb-2", "1.2")]).ShouldBe(new Dictionary<string, double> { ["1.1"] = 0.5, ["1.2"] = 0 });
        Should.Throw<InvalidOperationException>(() => reviews.Record(Info("qb-dx", "1.1", "diagnostic"), true, ReviewContext.StandUp, 0));
    }

    [Theory]
    [InlineData(10, new[] { 3.0, 3.0, 4.0 }, new[] { 3, 3, 4 })]
    [InlineData(10, new[] { 1.0, 1.0, 1.0 }, new[] { 4, 3, 3 })]
    [InlineData(7, new[] { 0.0, 0.0, 0.0 }, new[] { 0, 0, 0 })]
    [InlineData(5, new[] { -1.0, 1.0, 0.0 }, new[] { 0, 5, 0 })]
    public void Quotas_split_by_largest_remainder_with_ties_to_the_earlier_key(int total, double[] weights, int[] expected)
    {
        var keys = new[] { "a", "b", "c" };
        var quotas = Quota.Allocate(total, keys.Select((k, i) => (k, weights[i])).ToDictionary(p => p.k, p => p.Item2));

        keys.Select(k => quotas[k]).ShouldBe(expected);
        Quota.Allocate(3, new Dictionary<string, double>()).ShouldBeEmpty();
    }

    internal static QuestionInfo Info(string itemId, string objectiveId, string pool = "practice") =>
        new(itemId, objectiveId, "D" + objectiveId[0], pool, "sealed/items/" + itemId + ".bundle.json");

    private static ReviewCardRecord Card(string itemId, string objectiveId, DateTimeOffset due) =>
        new(itemId, objectiveId, "D" + objectiveId[0], "{}", due, due.AddDays(-10), null, 1, 0, false);
}
