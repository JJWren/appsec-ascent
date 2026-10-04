using System.Globalization;
using Ascent.Cli.Tests.TestSupport;
using Ascent.Core.Errors;
using Ascent.Core.Progress;
using Ascent.Storage;
using Ascent.Tests.Shared;

namespace Ascent.Cli.Tests;

/// <summary>standup, diagnostic, boss and sim (SU-*, DX-*, BF-*, SIM-*, WG-02).</summary>
public sealed class AssessmentCommandTests
{
    [Fact]
    [Trait("Rule", "WG-02")]
    public async Task A_stand_up_with_nothing_due_counts_once_the_learner_confirms()
    {
        using var fixture = CurriculumFixture.Create();
        using var engine = TestEngine.For(fixture);

        var declined = await engine.RunWithInputAsync(["n"], "standup");
        declined.Output.ShouldContain("Nothing is due today.");
        declined.Output.ShouldNotContain("Stand-up days");

        var (exitCode, output) = await engine.RunWithInputAsync(["y"], "standup");

        exitCode.ShouldBe(ExitCodes.Ok);
        output.ShouldContain("This week: 1/5 Stand-up days. Week streak: 0.");
        using var database = LearnerCommandTests.Open(engine);
        new XpLedger(new XpStore(database), engine.Clock).Total.ShouldBe(5);
    }

    [Fact]
    [Trait("Rule", "SU-01")]
    [Trait("Rule", "SU-03")]
    [Trait("Rule", "SU-04")]
    public async Task A_finished_lesson_brings_its_questions_into_the_stand_up_with_full_feedback()
    {
        using var fixture = CurriculumFixture.Create().Quest("q-1.1", "1.1", "D1");
        for (var i = 1; i <= 6; i++)
        {
            fixture.Question("qb-1.1-" + i.ToString("000", CultureInfo.InvariantCulture), "1.1");
        }

        using var engine = TestEngine.For(fixture);
        (await engine.RunAsync("teachback", "q-1.1", "--text", "Confidentiality.")).ExitCode.ShouldBe(ExitCodes.Ok);

        // Five new cards a day: an invalid answer is asked again, then four right and one wrong.
        var (exitCode, output) = await engine.RunWithInputAsync(["Z", "A", "A", "A", "A", "B"], "standup");

        exitCode.ShouldBe(ExitCodes.Ok);
        output.ShouldContain("1/5 · Objective 1.1 · new");
        output.ShouldContain("Stem of qb-1.1-001?");
        output.ShouldContain("  A) Option A");
        output.ShouldContain("Choose one option (for example B).");
        output.ShouldContain("Answer with one option letter.");
        output.ShouldContain("PASS: Correct.");
        output.ShouldContain("FAIL: Not quite. The answer is A.");
        output.ShouldContain("  B) Why B.");
        output.ShouldContain("  Source: Example standard, NIST (https://csrc.nist.gov/)");
        output.ShouldContain("4/5 correct.");
        output.ShouldNotContain("qb-1.1-006");

        using (var database = LearnerCommandTests.Open(engine))
        {
            var cards = new ReviewCardStore(database);
            cards.All().Count.ShouldBe(5);
            cards.Reviews().Count(r => !r.Correct).ShouldBe(1);
        }

        // Later the same day nothing new is introduced.
        (await engine.RunWithInputAsync(["y"], "standup")).Output.ShouldContain("Nothing is due today.");
    }

    [Fact]
    [Trait("Rule", "WG-02")]
    public async Task An_unreadable_question_is_skipped_without_losing_the_day()
    {
        using var fixture = CurriculumFixture.Create().Quest("q-1.1", "1.1", "D1").Question("qb-1.1-001", "1.1").Question("qb-1.1-002", "1.1");
        var bundle = Path.Join(fixture.Root, "sealed", "items", "qb-1.1-002.bundle.json");
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(bundle))!;
        json["signature"] = Convert.ToBase64String(new byte[64]);
        File.WriteAllText(bundle, json.ToJsonString());
        using var engine = TestEngine.For(fixture);
        await engine.RunAsync("teachback", "q-1.1", "--text", "Confidentiality.");

        var (exitCode, output) = await engine.RunWithInputAsync(["A"], "standup");

        exitCode.ShouldBe(ExitCodes.Ok);
        output.ShouldContain("WARN: The Sealed item 'qb-1.1-002' is unavailable. Skipped for today.");
        output.ShouldContain("1/1 correct.");
        output.ShouldContain("This week: 1/5 Stand-up days.");
    }

    [Fact]
    [Trait("Rule", "DX-01")]
    [Trait("Rule", "DX-03")]
    public async Task The_diagnostic_gives_no_feedback_and_ends_with_a_plan()
    {
        using var fixture = CurriculumFixture.Create();
        foreach (var domain in new[] { "D1", "D2", "D3", "D4", "D5", "D6", "D7", "D8" })
        {
            fixture.Question("qb-dx-" + domain, domain[1] + ".1", pool: "diagnostic").Quest("q-" + domain, domain[1] + ".1", domain, minutes: 120);
        }

        using var engine = TestEngine.For(fixture);

        var (exitCode, output) = await engine.RunWithInputAsync(Enumerable.Repeat("A", 8), "diagnostic");

        exitCode.ShouldBe(ExitCodes.Ok);
        output.ShouldContain("8 questions, untimed. Results come at the end, and every answer is saved as you go.");
        output.ShouldNotContain("Correct.");
        output.ShouldContain("Your study plan");
        output.ShouldContain("D1      1/1 (100%)");
        output.ShouldContain("About 28 hours: 4 weeks at 8 hours a week, then 2 Simulation weeks.");
        output.ShouldContain("Suggested exam window: 2026-11-14 to 2026-11-21.");
        output.ShouldContain("This assumes 8 hours a week. Change it with 'ascent config weeklyHours <hours>', then retake the Diagnostic for a new plan.");

        (await engine.RunAsync("config", "weeklyHours", "14")).ExitCode.ShouldBe(ExitCodes.Ok);
        var retake = await engine.RunWithInputAsync(Enumerable.Repeat("B", 8), "diagnostic");
        retake.Output.ShouldContain("About 28 hours: 2 weeks at 14 hours a week");
        retake.Output.ShouldContain("D1      0/1 (0%)");
        retake.Output.ShouldNotContain("This assumes");
    }

    [Fact]
    [Trait("Rule", "BF-02")]
    [Trait("Rule", "BF-03")]
    public async Task A_boss_fight_is_scored_at_the_end_with_a_full_review()
    {
        using var fixture = Pool("D1", perObjective: 15);
        using var engine = TestEngine.For(fixture);
        var answers = Enumerable.Repeat("A", 22).Concat(Enumerable.Repeat("B", 8));

        var (exitCode, output) = await engine.RunWithInputAsync(answers, "boss", "d1");

        exitCode.ShouldBe(ExitCodes.Ok);
        output.ShouldContain("D1 Boss Fight");
        output.ShouldContain("30 questions in 45 minutes, pass at 70%. No feedback until the end; unanswered questions count as wrong.");
        output.ShouldContain("1/30 · 45:00 left");
        output.ShouldContain("PASS: 22/30 (73%). Passed.");
        output.ShouldContain("+100 XP");
        output.ShouldContain("Review");
        output.ShouldContain("Your answer: B");
        output.IndexOf("Not quite", StringComparison.Ordinal).ShouldBeGreaterThan(output.IndexOf("Review", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Rule", "BF-02")]
    [Trait("Rule", "BF-05")]
    public async Task A_boss_fight_resumes_after_an_interruption_and_ends_when_time_is_up()
    {
        using var fixture = Pool("D1", perObjective: 15);
        using var engine = TestEngine.For(fixture);

        // The input ends after 10 answers, as if the terminal closed; the answers are already saved.
        var interrupted = await engine.RunWithInputAsync(Enumerable.Repeat("A", 10), "boss", "D1");
        interrupted.ExitCode.ShouldBe(ExitCodes.Usage);

        engine.Clock.Advance(TimeSpan.FromMinutes(20));
        var resumed = await engine.RunWithInputAsync(Enumerable.Repeat("B", 5), "boss", "D1");
        resumed.Output.ShouldContain("11/30 · 25:00 left");
        resumed.ExitCode.ShouldBe(ExitCodes.Usage);

        engine.Clock.Advance(TimeSpan.FromMinutes(30));
        var (exitCode, output) = await engine.RunAsync("boss", "D1");

        exitCode.ShouldBe(ExitCodes.Ok);
        output.ShouldContain("WARN: Time's up.");
        output.ShouldContain("FAIL: 10/30 (33%). Not passed yet. A rematch is due, and your Stand-ups favour D1 until you pass.");
        output.ShouldContain("Your answer: (none)");
        (await engine.RunAsync("status")).Output.ShouldContain("WARN: Rematch due: D1.");
    }

    [Fact]
    public async Task Boss_fights_take_a_domain_from_d1_to_d8()
    {
        using var fixture = CurriculumFixture.Create();
        using var engine = TestEngine.For(fixture);

        var (exitCode, output) = await engine.RunAsync("boss", "D9");

        exitCode.ShouldBe(ExitCodes.Usage);
        output.ShouldContain("The Domain must be D1 to D8.");
    }

    [Fact]
    [Trait("Rule", "SIM-01")]
    public async Task A_locked_simulation_names_the_domains_below_seventy()
    {
        using var fixture = CurriculumFixture.Create();
        using var engine = TestEngine.For(fixture);
        RecordBossScores(engine, 70, except: ("D3", 50));

        var (exitCode, output) = await engine.RunAsync("sim");

        exitCode.ShouldBe(ExitCodes.CheckFailed);
        output.ShouldContain("FAIL: Simulations unlock once every Boss Fight's best score is at least 70%. Still below: D3.");
        output.ShouldContain("Next: Run 'ascent boss D3'.");
    }

    [Fact]
    [Trait("Rule", "SIM-03")]
    [Trait("Rule", "SIM-04")]
    public async Task A_simulation_runs_against_the_clock_and_reviews_every_item_at_the_end()
    {
        using var fixture = Reserved();
        using var engine = TestEngine.For(fixture);
        RecordBossScores(engine, 80);

        (await engine.RunWithInputAsync(["n"], "sim")).Output.ShouldContain("A Simulation is 125 questions in 3 hours.");

        var started = await engine.RunWithInputAsync(["y", .. Enumerable.Repeat("A", 60)], "sim");
        started.ExitCode.ShouldBe(ExitCodes.Usage);
        started.Output.ShouldContain("Simulation A");
        started.Output.ShouldContain("1/125 · 180:00 left");

        engine.Clock.Advance(TimeSpan.FromHours(4));
        var (exitCode, output) = await engine.RunAsync("sim", "B");

        exitCode.ShouldBe(ExitCodes.Ok);
        output.ShouldContain("INFO: Simulation A is still in progress, so it resumes first.");
        output.ShouldContain("FAIL: Form A: 60/125 (48%), target 70%. Time ran out; unanswered questions count as wrong.");
        output.ShouldContain("Your answer: (none)");
        output.ShouldNotContain("Exam Ready");
    }

    [Fact]
    [Trait("Rule", "SIM-05")]
    public async Task Passing_both_forms_first_time_shows_exam_ready()
    {
        using var fixture = Reserved();
        using var engine = TestEngine.For(fixture);
        RecordBossScores(engine, 80);

        (await engine.RunWithInputAsync(["y", .. Enumerable.Repeat("A", 125)], "sim")).Output.ShouldContain("PASS: Form A: 125/125 (100%), target 70%.");
        var (_, output) = await engine.RunWithInputAsync(["y", .. Enumerable.Repeat("A", 125)], "sim");

        output.ShouldContain("PASS: Form B: 125/125 (100%), target 70%.");
        output.ShouldContain("PASS: Exam Ready: both forms reached the target on the first attempt.");
        (await engine.RunAsync("status")).Output.ShouldContain("Exam Ready");
    }

    internal static CurriculumFixture Pool(string examDomain, int perObjective)
    {
        var fixture = CurriculumFixture.Create();
        foreach (var objective in CurriculumFixture.ObjectivesOf(examDomain))
        {
            for (var i = 1; i <= perObjective; i++)
            {
                fixture.Question("qb-" + objective + "-" + i.ToString("000", CultureInfo.InvariantCulture), objective);
            }
        }

        return fixture;
    }

    private static CurriculumFixture Reserved()
    {
        var quotas = new Dictionary<string, int> { ["D1"] = 15, ["D2"] = 14, ["D3"] = 16, ["D4"] = 19, ["D5"] = 18, ["D6"] = 17, ["D7"] = 14, ["D8"] = 12 };
        var fixture = CurriculumFixture.Create();
        foreach (var (domain, quota) in quotas)
        {
            for (var i = 1; i <= 2 * quota; i++)
            {
                fixture.Question("qb-sim-" + domain + "-" + i.ToString("000", CultureInfo.InvariantCulture), domain[1] + ".1", pool: "simulation");
            }
        }

        return fixture;
    }

    private static void RecordBossScores(TestEngine engine, int score, (string Domain, int Score)? except = null)
    {
        using var database = LearnerCommandTests.Open(engine);
        var attempts = new AttemptStore(database);
        foreach (var domain in new[] { "D1", "D2", "D3", "D4", "D5", "D6", "D7", "D8" })
        {
            var value = except is { } e && e.Domain == domain ? e.Score : score;
            var start = engine.Clock.GetUtcNow();
            var id = attempts.StartBoss(domain, BossKind.First, start, start.AddMinutes(45), ["x"]);
            attempts.FinishBoss(id, start, value, 100, value, value >= 70);
        }
    }
}
