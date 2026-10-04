using Ascent.Core.Domain;
using Ascent.Core.Profile;
using Ascent.Core.Progress;
using Ascent.Core.Time;
using Ascent.Engine.Tests.TestSupport;
using Ascent.Tests.Shared;

namespace Ascent.Engine.Tests.Progress;

/// <summary>Stand-up days, the weekly goal and the week streak (WG-01..04).</summary>
public sealed class WeekGoalTests
{
    [Fact]
    [Trait("Rule", "WG-01")]
    public void The_goal_counts_local_stand_up_days_per_iso_week_and_defaults_to_five()
    {
        new LearnerProfile(new Dictionary<string, string>()).WeeklyGoal.ShouldBe(LearnerProfile.DefaultWeeklyGoal);
        LearnerProfile.DefaultWeeklyGoal.ShouldBe(5);
        new LearnerProfile(new Dictionary<string, string> { [ProfileKeys.WeeklyGoal] = "3" }).WeeklyGoal.ShouldBe(3);
        ConfigCatalog.Find(ProfileKeys.WeeklyGoal)!.Validate("8").ShouldNotBeNull();
        ConfigCatalog.Find(ProfileKeys.WeeklyGoal)!.Validate("7").ShouldBeNull();

        using var fixture = CurriculumFixture.Create();
        using var game = new GameHarness(fixture);
        var goal = game.WeekGoal;
        goal.RecordStandUp(3, 5);
        goal.RecordStandUp(2, 5); // A second session the same day is the same day.
        game.Clock.Advance(TimeSpan.FromDays(6)); // Sunday of the same ISO week.
        var status = goal.RecordStandUp(1, 5);

        status.Week.ShouldBe(new IsoWeek(2026, 41));
        status.Days.ShouldBe(2);
        status.Goal.ShouldBe(5);
        status.Met.ShouldBeFalse();

        game.Clock.Advance(TimeSpan.FromDays(1)); // Monday: a new week.
        goal.Status(5).Days.ShouldBe(0);
    }

    [Fact]
    [Trait("Rule", "WG-02")]
    public void A_stand_up_with_nothing_due_still_counts_the_day()
    {
        using var fixture = CurriculumFixture.Create();
        using var game = new GameHarness(fixture);

        var status = game.WeekGoal.RecordStandUp(itemsReviewed: 0, goal: 1);

        status.Days.ShouldBe(1);
        status.Met.ShouldBeTrue();
        game.Ledger.Events.Select(e => e.Kind).ShouldBe([XpKind.StandUp, XpKind.WeeklyGoal]);
    }

    [Fact]
    [Trait("Rule", "WG-03")]
    [Trait("Rule", "WG-04")]
    public void Meeting_the_goal_pays_once_a_week_and_consecutive_weeks_build_a_streak()
    {
        using var fixture = CurriculumFixture.Create();
        using var game = new GameHarness(fixture);
        var goal = game.WeekGoal;

        // Week 41: Monday to Thursday, goal 3.
        for (var day = 0; day < 4; day++)
        {
            goal.RecordStandUp(5, 3);
            game.Clock.Advance(TimeSpan.FromDays(1));
        }

        var week41 = goal.Status(3);
        week41.ShouldBe(new WeekStatus(new IsoWeek(2026, 41), 4, 3, Met: true, Streak: 1));
        game.Ledger.Total.ShouldBe((4 * 5) + 20);

        // Week 42: three days; the streak reaches 2 and earns +5.
        game.Clock.Advance(TimeSpan.FromDays(3));
        for (var day = 0; day < 3; day++)
        {
            goal.RecordStandUp(5, 3);
            game.Clock.Advance(TimeSpan.FromDays(1));
        }

        goal.Status(3).Streak.ShouldBe(2);
        game.Ledger.Total.ShouldBe(40 + (3 * 5) + 20 + 5);

        // Week 43 is missed, so by week 44 the streak is back to 0 until the goal is met again.
        game.Clock.Advance(TimeSpan.FromDays(11));
        goal.Status(3).ShouldBe(new WeekStatus(new IsoWeek(2026, 44), 0, 3, Met: false, Streak: 0));
        for (var day = 0; day < 3; day++)
        {
            goal.RecordStandUp(5, 3);
            game.Clock.Advance(TimeSpan.FromDays(1));
        }

        goal.Status(3).Streak.ShouldBe(1);
        goal.LongestStreak().ShouldBe(2);
        game.Ledger.Events.Count(e => e.Kind == XpKind.WeeklyGoal).ShouldBe(3);
        game.Ledger.Events.Count(e => e.Kind == XpKind.WeekStreak).ShouldBe(1);
    }

    [Fact]
    [Trait("Rule", "WG-04")]
    public void Last_weeks_streak_stays_visible_until_this_week_is_decided()
    {
        using var fixture = CurriculumFixture.Create();
        using var game = new GameHarness(fixture);
        game.WeekGoal.RecordStandUp(1, 1);

        game.Clock.Advance(TimeSpan.FromDays(7));

        game.WeekGoal.Status(1).ShouldBe(new WeekStatus(new IsoWeek(2026, 42), 0, 1, Met: false, Streak: 1));
    }
}
