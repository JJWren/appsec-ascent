using Ascent.Core.Domain;
using Ascent.Core.Progress;
using Ascent.Engine.Tests.TestSupport;
using Ascent.Tests.Shared;

namespace Ascent.Engine.Tests.Progress;

/// <summary>The XP ledger, Rank thresholds and gates, and badges (XP-01..03, RNK-01..04).</summary>
public sealed class XpAndRankTests
{
    private static readonly RankGates NoGates = new(AllBossFightsPassed: false, CapstoneComplete: false);
    private static readonly RankGates AllGates = new(AllBossFightsPassed: true, CapstoneComplete: true);

    [Theory]
    [Trait("Rule", "XP-01")]
    [InlineData(XpKind.Lesson, 10)]
    [InlineData(XpKind.Drill, 15)]
    [InlineData(XpKind.LabRed, 25)]
    [InlineData(XpKind.LabBlue, 40)]
    [InlineData(XpKind.LabExplain, 10)]
    [InlineData(XpKind.Deliverable, 40)]
    [InlineData(XpKind.DeliverablePass, 10)]
    [InlineData(XpKind.StandUp, 5)]
    [InlineData(XpKind.WeeklyGoal, 20)]
    [InlineData(XpKind.BossFirstPass, 100)]
    [InlineData(XpKind.BossRematchPass, 60)]
    [InlineData(XpKind.ContentBug, 25)]
    [InlineData(XpKind.CloudStage, 30)]
    [InlineData(XpKind.TeardownBonus, 10)]
    [InlineData(XpKind.TeardownPenalty, -20)]
    [InlineData(XpKind.DeepDive, 75)]
    [InlineData(XpKind.ReviewerFlag, 30)]
    public void The_award_table_matches_the_rules(XpKind kind, int points) => XpAwards.Points(kind).ShouldBe(points);

    [Theory]
    [Trait("Rule", "XP-01")]
    [Trait("Rule", "WG-03")]
    [InlineData(1, 0)]
    [InlineData(2, 5)]
    [InlineData(3, 10)]
    [InlineData(5, 20)]
    [InlineData(12, 20)]
    public void The_week_streak_bonus_is_five_per_extra_week_capped_at_twenty(int streak, int bonus) => XpAwards.StreakBonus(streak).ShouldBe(bonus);

    [Fact]
    [Trait("Rule", "XP-01")]
    public void Each_award_is_made_once_per_kind_and_reference()
    {
        var ledger = new XpLedger(new MemoryXpStore(), TimeProvider.System);

        ledger.Award(XpKind.Lesson, "q-1.1").ShouldBeTrue();
        ledger.Award(XpKind.Lesson, "q-1.1").ShouldBeFalse();
        ledger.Award(XpKind.Drill, "q-1.1").ShouldBeTrue();

        ledger.Total.ShouldBe(25);
        ledger.Has(XpKind.Lesson, "q-1.1").ShouldBeTrue();
        ledger.Has(XpKind.Lesson, "q-1.2").ShouldBeFalse();
        Should.Throw<ArgumentException>(() => ledger.Award(XpKind.Lesson, " "));
    }

    [Fact]
    [Trait("Rule", "XP-02")]
    public void Bonus_sources_are_marked_and_kept_out_of_core_xp()
    {
        Enum.GetValues<XpKind>().Where(XpAwards.IsBonus).ShouldBe(
            [XpKind.CloudStage, XpKind.TeardownBonus, XpKind.TeardownPenalty, XpKind.DeepDive, XpKind.ReviewerFlag],
            ignoreOrder: true);

        using var fixture = CurriculumFixture.Create()
            .Quest("q-5.1", "5.1", "D5", labs: ["lab-d5-01", "lab-d5-02"])
            .Lab("lab-d5-01", "5.1")
            .Lab("lab-d5-02", "5.1", bonus: true, windowsOnly: true);
        using var game = new GameHarness(fixture);

        // One Quest (10), one core Lab (25 + 40 + 10) and one Boss Fight (100); the bonus Lab adds nothing.
        game.Catalog.CoreXpMax.ShouldBe(10 + 75 + 100);

        var ledger = game.Ledger;
        ledger.Award(XpKind.DeepDive, "dd-1").ShouldBeTrue();
        ledger.Events.Single().Bonus.ShouldBeTrue();
        ledger.Total.ShouldBe(75);
    }

    [Fact]
    [Trait("Rule", "XP-03")]
    public void Penalties_never_take_the_total_below_zero_or_leave_a_debt()
    {
        var ledger = new XpLedger(new MemoryXpStore(), TimeProvider.System);

        ledger.Award(XpKind.TeardownPenalty, "deploy-1").ShouldBeTrue();
        ledger.Total.ShouldBe(0);

        ledger.Award(XpKind.Lesson, "q-1.1");
        ledger.Total.ShouldBe(10);

        ledger.Award(XpKind.TeardownPenalty, "deploy-2");
        ledger.Total.ShouldBe(0);

        ledger.Award(XpKind.BossFirstPass, "D1");
        ledger.Award(XpKind.TeardownPenalty, "deploy-3");
        ledger.Total.ShouldBe(80);
        ledger.Events.Count(e => e.Points < 0).ShouldBe(3);
    }

    [Theory]
    [Trait("Rule", "RNK-01")]
    [InlineData(0, Rank.Developer)]
    [InlineData(99, Rank.Developer)]
    [InlineData(100, Rank.SecurityChampion)]
    [InlineData(349, Rank.SecurityChampion)]
    [InlineData(350, Rank.AppSecEngineer)]
    [InlineData(600, Rank.SeniorAppSecEngineer)]
    [InlineData(849, Rank.SeniorAppSecEngineer)]
    [InlineData(850, Rank.PrincipalAppSecEngineer)]
    [InlineData(5000, Rank.PrincipalAppSecEngineer)]
    public void Ranks_are_fractions_of_core_xp(long total, Rank expected) =>
        RankCalculator.Candidate(total, 1000, AllGates).ShouldBe(expected);

    [Fact]
    [Trait("Rule", "RNK-01")]
    public void Thresholds_round_up_and_an_empty_curriculum_means_developer()
    {
        RankCalculator.Threshold(Rank.SecurityChampion, 1001).ShouldBe(101);
        RankCalculator.Threshold(Rank.SecurityChampion, 110).ShouldBe(11);
        RankCalculator.Threshold(Rank.PrincipalAppSecEngineer, 220).ShouldBe(187);
        RankCalculator.Threshold(Rank.Developer, 1001).ShouldBe(0);
        RankCalculator.Threshold(Rank.PrincipalAppSecEngineer, 0).ShouldBe(0);
        RankCalculator.Candidate(10_000, 0, AllGates).ShouldBe(Rank.Developer);
        RankCalculator.Next(Rank.Developer).ShouldBe(Rank.SecurityChampion);
        RankCalculator.Next(Rank.PrincipalAppSecEngineer).ShouldBeNull();
        Enum.GetValues<Rank>().Select(RankCalculator.DisplayName).ShouldBe(
            ["Developer", "Security Champion", "AppSec Engineer", "Senior AppSec Engineer", "Principal AppSec Engineer"]);
        Should.Throw<ArgumentOutOfRangeException>(() => RankCalculator.Percent((Rank)99));
        Should.Throw<ArgumentOutOfRangeException>(() => RankCalculator.DisplayName((Rank)99));
    }

    [Fact]
    [Trait("Rule", "RNK-02")]
    public void Senior_needs_every_boss_fight_and_principal_also_needs_the_capstone()
    {
        RankCalculator.Candidate(1000, 1000, NoGates).ShouldBe(Rank.AppSecEngineer);
        RankCalculator.Candidate(1000, 1000, new RankGates(AllBossFightsPassed: true, CapstoneComplete: false)).ShouldBe(Rank.SeniorAppSecEngineer);
        RankCalculator.Candidate(1000, 1000, new RankGates(AllBossFightsPassed: false, CapstoneComplete: true)).ShouldBe(Rank.AppSecEngineer);
        RankCalculator.Candidate(1000, 1000, AllGates).ShouldBe(Rank.PrincipalAppSecEngineer);
    }

    [Fact]
    [Trait("Rule", "RNK-03")]
    public void Ranks_never_go_down()
    {
        // A penalty, or a Curriculum change that raises CoreXpMax, lowers the candidate but not the Rank reached.
        RankCalculator.Candidate(400, 2000, NoGates).ShouldBe(Rank.SecurityChampion);
        RankCalculator.Current(400, 2000, NoGates, Rank.AppSecEngineer).ShouldBe(Rank.AppSecEngineer);
        RankCalculator.Current(400, 2000, NoGates, null).ShouldBe(Rank.SecurityChampion);
        RankCalculator.Current(900, 1000, AllGates, Rank.AppSecEngineer).ShouldBe(Rank.PrincipalAppSecEngineer);
    }

    [Fact]
    [Trait("Rule", "RNK-04")]
    [Trait("Rule", "LABE-05")]
    public void Every_rank_is_reachable_on_the_free_linux_path_with_slack()
    {
        using var fixture = SeasonShaped();
        using var game = new GameHarness(fixture);
        var catalog = game.Catalog;
        catalog.Quests.Count.ShouldBe(58 + 2 + 1);
        catalog.BossDomains.Count.ShouldBe(8);

        // A Learner on the $0 / Linux path does every core activity and no bonus content at all.
        var ledger = game.Ledger;
        foreach (var quest in catalog.Quests)
        {
            ledger.Award(XpKind.Lesson, quest.Id);
        }

        foreach (var drill in catalog.Drills)
        {
            ledger.Award(XpKind.Drill, drill);
        }

        foreach (var lab in catalog.Labs.Where(l => !l.Bonus))
        {
            ledger.Award(XpKind.LabRed, lab.Id);
            ledger.Award(XpKind.LabBlue, lab.Id);
            ledger.Award(XpKind.LabExplain, lab.Id);
        }

        foreach (var deliverable in catalog.Deliverables)
        {
            ledger.Award(XpKind.Deliverable, deliverable);
            ledger.Award(XpKind.DeliverablePass, deliverable);
        }

        foreach (var domain in catalog.BossDomains)
        {
            ledger.Award(XpKind.BossFirstPass, domain);
        }

        var coreXpMax = catalog.CoreXpMax;
        ledger.Total.ShouldBe(coreXpMax);
        catalog.Labs.Count(l => l.Bonus).ShouldBeGreaterThan(0);

        // Principal needs at most 90% of CoreXpMax, so a Learner can miss some core XP and skip all bonus content.
        RankCalculator.Threshold(Rank.PrincipalAppSecEngineer, coreXpMax).ShouldBeLessThanOrEqualTo((long)Math.Floor(0.9 * coreXpMax));
        RankCalculator.Candidate((long)Math.Floor(0.9 * coreXpMax), coreXpMax, AllGates).ShouldBe(Rank.PrincipalAppSecEngineer);
        RankCalculator.Candidate(ledger.Total, coreXpMax, AllGates).ShouldBe(Rank.PrincipalAppSecEngineer);
    }

    [Fact]
    public void Badges_come_from_the_ledger_and_progress_facts()
    {
        var at = DateTimeOffset.UnixEpoch;
        XpEvent Event(XpKind kind, string refId) => new(at, kind, refId, XpAwards.Points(kind), XpAwards.IsBonus(kind));
        var events = new List<XpEvent>
        {
            Event(XpKind.LabRed, "lab-d5-01"),
            Event(XpKind.ContentBug, "42"),
            Event(XpKind.ReviewerFlag, "dlv-d3-01"),
            Event(XpKind.DeepDive, "dd-1"),
        };
        events.AddRange(Enumerable.Range(1, 5).Select(i => Event(XpKind.TeardownBonus, "deploy-" + i)));

        var (badges, cleared) = Badges.Compute(new BadgeFacts(events, new Dictionary<string, int> { ["D1"] = 100 }, ["D1"], 8, ExamReady: true));

        badges.ShouldBe([Badge.FirstBlood, Badge.CleanSweep, Badge.BugHunter, Badge.FrugalEngineer, Badge.PromptBreaker, Badge.DeepDiver, Badge.Marathoner, Badge.ExamReady]);
        cleared.ShouldBe(["D1"]);

        events.Add(Event(XpKind.TeardownPenalty, "deploy-6"));
        var (fewer, _) = Badges.Compute(new BadgeFacts(events, new Dictionary<string, int> { ["D1"] = 99 }, [], 7, ExamReady: false));
        fewer.ShouldBe([Badge.FirstBlood, Badge.BugHunter, Badge.PromptBreaker, Badge.DeepDiver]);

        Enum.GetValues<Badge>().Select(Badges.Name).ShouldBe(
            ["First Blood", "Clean Sweep", "Bug Hunter", "Frugal Engineer", "Prompt Breaker", "Deep Diver", "Marathoner", "Exam Ready"]);
        Should.Throw<ArgumentOutOfRangeException>(() => Badges.Name((Badge)99));
    }

    // The Season's shape: a Quest per Objective of the real outline, Orientation and Capstone Quests, a drill and a
    // Deliverable per Domain, a core Lab per Domain and two bonus Labs (Windows-only and paid).
    internal static CurriculumFixture SeasonShaped()
    {
        var fixture = CurriculumFixture.Create();
        fixture.Quest("q-ori-1", "ORI-1", "ORI").Quest("q-ori-2", "ORI-2", "ORI").Quest("q-cap-1", "CAP-1", "CAP");
        foreach (var domain in CurriculumFixture.RealOutline.Domains)
        {
            var objectives = domain.Objectives.Select(o => o.Id).ToList();
            var n = domain.Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
            for (var i = 0; i < objectives.Count; i++)
            {
                var first = i == 0;
                fixture.Quest(
                    "q-" + objectives[i],
                    objectives[i],
                    domain.Id,
                    labs: first ? ["lab-d" + n + "-01"] : [],
                    drills: first ? ["drl-d" + n + "-01"] : [],
                    deliverables: first ? ["dlv-d" + n + "-01"] : []);
            }

            fixture.Lab("lab-d" + n + "-01", objectives[0]).Drill("drl-d" + n + "-01", objectives[0]).Deliverable("dlv-d" + n + "-01", objectives[0]);
        }

        return fixture.Lab("lab-d7-90", "7.1", bonus: true, windowsOnly: true).Lab("lab-d4-90", "4.1", bonus: true);
    }

    private sealed class MemoryXpStore : IXpStore
    {
        private readonly List<XpEvent> events = [];

        public bool TryAdd(XpEvent xpEvent)
        {
            if (Has(xpEvent.Kind, xpEvent.RefId))
            {
                return false;
            }

            events.Add(xpEvent);
            return true;
        }

        public bool Has(XpKind kind, string refId) => events.Any(e => e.Kind == kind && e.RefId == refId);

        public IReadOnlyList<XpEvent> All() => events;
    }
}
