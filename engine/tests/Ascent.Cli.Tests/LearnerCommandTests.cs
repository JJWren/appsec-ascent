using Ascent.Cli.Tests.TestSupport;
using Ascent.Core.Domain;
using Ascent.Core.Errors;
using Ascent.Core.Platform;
using Ascent.Core.Profile;
using Ascent.Core.Progress;
using Ascent.Storage;
using Ascent.Tests.Shared;

namespace Ascent.Cli.Tests;

/// <summary>start, rules, config, next, quest, teachback and status (E1-04, E1-07, E2-01, E2-04, EXM-01, WG-04).</summary>
public sealed class LearnerCommandTests
{
    [Fact]
    public async Task Start_creates_the_profile_and_points_at_the_first_quest()
    {
        using var fixture = CurriculumFixture.Create().Quest("q-1.1", "1.1", "D1").Quest("q-ori-1", "ORI-1", "ORI");
        using var engine = TestEngine.For(fixture);

        var (exitCode, output) = await engine.RunAsync("start");

        exitCode.ShouldBe(ExitCodes.Ok);
        output.ShouldContain("INFO: Before any Lab, read and accept the rules of engagement: ascent rules");
        output.ShouldContain("This week: 0/5 Stand-up days. Week streak: 0.");
        output.ShouldContain("Next Quest: q-ori-1 · Quest q-ori-1 (ORI, Objective ORI-1). Open it with 'ascent quest q-ori-1'.");
        output.ShouldNotContain("exam");
        Profile(engine).LearnerId.ShouldNotBeNull();
    }

    [Fact]
    public async Task Start_says_when_every_quest_is_done()
    {
        using var fixture = CurriculumFixture.Create();
        using var engine = TestEngine.For(fixture);

        var (_, output) = await engine.RunAsync("start");

        output.ShouldContain("Every Quest is complete. Run 'ascent status' to see where you stand.");
    }

    [Fact]
    [Trait("Rule", "EXM-01")]
    public async Task A_complete_season_prompts_for_the_exam_date_with_the_plans_window()
    {
        using var fixture = CurriculumFixture.Create().Season(complete: true);
        using var engine = TestEngine.For(fixture);
        using (var database = Open(engine))
        {
            var attempts = new AttemptStore(database);
            var id = attempts.StartDiagnostic(["qb-1"], engine.Clock.GetUtcNow());
            attempts.FinishDiagnostic(id, engine.Clock.GetUtcNow(), "{}", "{\"examWindowStart\":\"2027-01-04\",\"examWindowEnd\":\"2027-01-11\"}");
        }

        var (exitCode, output) = await engine.RunWithInputAsync(["soon", "2027-01-06"], "start");

        exitCode.ShouldBe(ExitCodes.Ok);
        output.ShouldContain("INFO: Season 1 is complete. Time to book your exam.");
        output.ShouldContain("Your study plan suggests booking between 2027-01-04 and 2027-01-11.");
        output.ShouldContain("Use a date like 2027-02-15, or 'later'.");
        output.ShouldContain("PASS: Exam date saved: 2027-01-06.");
        Profile(engine).ExamDate.ShouldBe(new DateOnly(2027, 1, 6));

        (await engine.RunAsync("start")).Output.ShouldNotContain("book your exam");
    }

    [Fact]
    [Trait("Rule", "EXM-01")]
    public async Task The_exam_prompt_can_wait_until_tomorrow()
    {
        using var fixture = CurriculumFixture.Create().Season(complete: true);
        using var engine = TestEngine.For(fixture);

        (await engine.RunWithInputAsync(["later"], "start")).Output.ShouldContain("Exam date (yyyy-MM-dd), or 'later':");
        (await engine.RunAsync("start")).Output.ShouldNotContain("book your exam");

        engine.Clock.Advance(TimeSpan.FromDays(1));
        var (_, output) = await engine.RunAsync("start", "--later");
        output.ShouldContain("OK. I'll ask about your exam date again tomorrow.");
        Profile(engine).ExamPromptSnoozedUntil.ShouldBe(new DateOnly(2026, 10, 5));
    }

    [Fact]
    public async Task Rules_are_accepted_once()
    {
        using var fixture = CurriculumFixture.Create();
        using var engine = TestEngine.For(fixture);

        var declined = await engine.RunWithInputAsync(["n"], "rules");
        declined.ExitCode.ShouldBe(ExitCodes.CheckFailed);
        declined.Output.ShouldContain("1. Attack only the Throughline System running on your own machine or in your own Azure subscription.");
        declined.Output.ShouldContain("WARN: Not accepted. Labs stay locked until you accept them.");

        var accepted = await engine.RunWithInputAsync(["y"], "rules");
        accepted.ExitCode.ShouldBe(ExitCodes.Ok);
        accepted.Output.ShouldContain("PASS: Accepted. Labs are unlocked.");
        Profile(engine).RulesAccepted.ShouldBeTrue();

        (await engine.RunAsync("rules")).Output.ShouldContain("PASS: You've already accepted these rules.");
    }

    [Fact]
    public async Task Config_lists_shows_changes_and_unsets_settings()
    {
        using var fixture = CurriculumFixture.Create();
        using var engine = TestEngine.For(fixture);

        (await engine.RunAsync("config", "weeklyGoal", "3")).Output.ShouldContain("PASS: weeklyGoal = 3");
        (await engine.RunAsync("config", "weeklyGoal")).Output.ShouldContain("weeklyGoal = 3");

        var (_, list) = await engine.RunAsync("config");
        list.ShouldContain("Setting");
        list.ShouldContain("weeklyGoal");
        list.ShouldContain("(default)");

        (await engine.RunAsync("config", "weeklyGoal", "--unset")).Output.ShouldContain("PASS: weeklyGoal is back to its default.");
        (await engine.RunAsync("config", "weeklyGoal")).Output.ShouldContain("weeklyGoal = (default)");

        var invalid = await engine.RunAsync("config", "weeklyGoal", "9");
        invalid.ExitCode.ShouldBe(ExitCodes.Usage);
        invalid.Output.ShouldContain("FAIL: weeklyGoal: Use a whole number from 1 to 7.");

        var unknown = await engine.RunAsync("config", "favouriteColour");
        unknown.ExitCode.ShouldBe(ExitCodes.Usage);
        unknown.Output.ShouldContain("There is no setting called 'favouriteColour'.");
    }

    [Fact]
    public async Task Next_and_quest_show_the_way_through_a_quest()
    {
        using var fixture = CurriculumFixture.Create()
            .Quest("q-1.1", "1.1", "D1", minutes: 45, drills: ["drl-d1-01"], body: "Confidentiality limits disclosure.")
            .Drill("drl-d1-01", "1.1");
        using var engine = TestEngine.For(fixture);

        var (_, next) = await engine.RunAsync("next");
        next.ShouldContain("q-1.1 · Quest q-1.1");
        next.ShouldContain("D1, Objective 1.1, about 45 minutes.");

        var (exitCode, quest) = await engine.RunAsync("quest", "q-1.1");
        exitCode.ShouldBe(ExitCodes.Ok);
        quest.ShouldContain("Confidentiality limits disclosure.");
        quest.ShouldContain("INFO: drill drl-d1-01 to do");
        quest.ShouldContain("When you've read the lesson, explain it in your own words: ascent teachback q-1.1");

        var missing = await engine.RunAsync("quest", "q-9.9");
        missing.ExitCode.ShouldBe(ExitCodes.Usage);
        missing.Output.ShouldContain("There is no Quest called 'q-9.9'.");
    }

    [Fact]
    [Trait("Rule", "XP-01")]
    public async Task A_teach_back_completes_the_lesson_and_a_quest_without_activities()
    {
        using var fixture = CurriculumFixture.Create().Quest("q-1.1", "1.1", "D1").Quest("q-1.2", "1.2", "D1");
        using var engine = TestEngine.For(fixture);

        var (exitCode, output) = await engine.RunAsync("teachback", "q-1.1", "--text", "Confidentiality keeps data from the wrong people.");

        exitCode.ShouldBe(ExitCodes.Ok);
        output.ShouldContain("PASS: Teach-back saved to " + Path.Join(fixture.Root, "journal", "q-1.1.md") + ".");
        output.ShouldContain("Quest complete.");
        (await engine.RunAsync("quest", "q-1.1")).Output.ShouldContain("PASS: Quest complete.");
        (await engine.RunAsync("next")).Output.ShouldContain("q-1.2");

        var file = fixture.Write("tb.md", "Least privilege limits damage.").Root + "/tb.md";
        (await engine.RunAsync("teachback", "q-1.2", "--file", file)).ExitCode.ShouldBe(ExitCodes.Ok);
        (await engine.RunAsync("next")).Output.ShouldContain("Every Quest is complete.");

        var prompted = await engine.RunWithInputAsync([string.Join(' ', Enumerable.Repeat("word", 151)), "Now it fits."], "teachback", "q-1.1");
        prompted.ExitCode.ShouldBe(ExitCodes.Ok);
        prompted.Output.ShouldContain("Use 1–150 words.");

        var tooLong = await engine.RunAsync("teachback", "q-1.1", "--text", string.Join(' ', Enumerable.Repeat("word", 151)));
        tooLong.ExitCode.ShouldBe(ExitCodes.Usage);
        tooLong.Output.ShouldContain("A Teach-back needs 1–150 words; this one has 151.");

        var huge = fixture.Write("huge.md", new string('x', 70_000)).Root + "/huge.md";
        (await engine.RunAsync("teachback", "q-1.1", "--file", huge)).Output.ShouldContain("is larger than 64 KB");
        (await engine.RunAsync("teachback", "q-1.1", "--file", Path.Join(fixture.Root, "none.md"))).ExitCode.ShouldBe(ExitCodes.Usage);
    }

    [Fact]
    [Trait("Rule", "EXM-01")]
    public async Task An_unreadable_study_plan_just_leaves_out_the_window()
    {
        using var fixture = CurriculumFixture.Create().Season(complete: true);
        using var engine = TestEngine.For(fixture);
        using (var database = Open(engine))
        {
            var attempts = new AttemptStore(database);
            attempts.FinishDiagnostic(attempts.StartDiagnostic(["qb-1"], engine.Clock.GetUtcNow()), engine.Clock.GetUtcNow(), "{}", "not json");
        }

        var (exitCode, output) = await engine.RunWithInputAsync(["later"], "start");

        exitCode.ShouldBe(ExitCodes.Ok);
        output.ShouldContain("Season 1 is complete.");
        output.ShouldNotContain("suggests booking");
    }

    [Fact]
    [Trait("Rule", "WG-04")]
    [Trait("Rule", "RNK-03")]
    [Trait("Rule", "S2-01")]
    public async Task Status_shows_rank_xp_badges_the_weekly_goal_rematches_and_season_two()
    {
        using var fixture = CurriculumFixture.Create().Quest("q-1.1", "1.1", "D1").Quest("q-4.1", "4.1", "D4");
        using var engine = TestEngine.For(fixture);
        using (var database = Open(engine))
        {
            var ledger = new XpLedger(new XpStore(database), engine.Clock);
            ledger.Award(XpKind.Lesson, "q-1.1");
            ledger.Award(XpKind.LabRed, "lab-d5-01");
            new StandUpStore(database).Record(new DateOnly(2026, 10, 3), engine.Clock.GetUtcNow(), 3);
            var attempts = new AttemptStore(database);
            var start = engine.Clock.GetUtcNow();
            var id = attempts.StartBoss("D4", BossKind.First, start, start.AddMinutes(45), ["x"]);
            attempts.FinishBoss(id, start, 10, 30, 33, passed: false);
            new Season2Store(database).Add("dd-1", Season2Reason.SkippedDeepDive, start);
        }

        // CoreXpMax = 2 Quests × 10 + 2 Boss Fights × 100 = 220; 35 XP passes Security Champion (22).
        var (exitCode, output) = await engine.RunAsync("status");

        exitCode.ShouldBe(ExitCodes.Ok);
        output.ShouldContain("PASS: Promoted to Security Champion!");
        output.ShouldContain("Rank: Security Champion");
        output.ShouldContain("XP: 35 (next: AppSec Engineer at 77)");
        output.ShouldContain("This week (2026-W40): 1/5 Stand-up days. Week streak: 0.");
        output.ShouldContain("Badges: First Blood");
        output.ShouldContain("D4          33%");
        output.ShouldContain("WARN: Rematch due: D4. Your Stand-ups favour these Domains until you pass.");
        output.ShouldContain("Season 2: dd-1");

        // A later Curriculum change raises CoreXpMax to 450, so 35 XP is below Security Champion's 45, but the Rank
        // reached stays (RNK-03).
        fixture.Quest("q-8.1", "8.1", "D8").Quest("q-8.2", "8.2", "D8").Quest("q-7.1", "7.1", "D7");
        var (_, again) = await engine.RunAsync("status");
        again.ShouldNotContain("Promoted");
        again.ShouldContain("Rank: Security Champion");
        again.ShouldContain("XP: 35 (next: AppSec Engineer at 158)");
    }

    [Fact]
    public async Task Status_on_a_fresh_profile_has_no_badges()
    {
        using var fixture = CurriculumFixture.Create();
        using var engine = TestEngine.For(fixture);

        var (_, output) = await engine.RunAsync("status");

        output.ShouldContain("Rank: Developer");
        output.ShouldContain("XP: 0 (next: Security Champion at 0)");
        output.ShouldContain("Badges: none yet");
        output.ShouldNotContain("Promoted");
    }

    internal static ProgressDatabase Open(TestEngine engine) => ProgressDatabase.Open(engine.Paths, engine.Clock, OwnerOnlyFiles.ForCurrentOs());

    internal static LearnerProfile Profile(TestEngine engine)
    {
        using var database = Open(engine);
        return new LearnerProfile(new ProfileStore(database).All());
    }
}
