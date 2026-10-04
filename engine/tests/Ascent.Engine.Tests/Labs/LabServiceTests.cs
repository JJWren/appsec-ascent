using Ascent.Core.Curriculum;
using Ascent.Core.Domain;
using Ascent.Core.Errors;
using Ascent.Core.Platform;
using Ascent.Engine.Tests.TestSupport;
using Ascent.Labs;
using Ascent.Sealing;
using Ascent.Sealing.Flags;
using Ascent.Storage;
using Ascent.Tests.Shared;

namespace Ascent.Engine.Tests.Labs;

/// <summary>The Lab lifecycle: Red → Blue → Explain (LABE-01..05, FLAG-01..04).</summary>
public sealed class LabServiceTests
{
    private const string Lab = HelloLab.LabId;

    [Fact]
    [Trait("Rule", "LABE-01")]
    [Trait("Rule", "LABE-03")]
    [Trait("Rule", "FLAG-04")]
    public async Task A_lab_moves_one_stage_at_a_time_and_pays_each_steps_xp_once()
    {
        using var fixture = HelloLab.Create();
        using var game = Started(fixture);
        var labs = game.Labs;

        labs.Stage(Lab).ShouldBe(LabStage.Started);
        game.Orchestrator.Up.ShouldBe([Lab]);
        var flag = await File.ReadAllTextAsync(HelloLab.FlagFile(fixture.Root), TestContext.Current.CancellationToken);
        flag.ShouldMatch("^ASCENT\\{[A-Z2-7]{26}\\}$");
        File.Exists(HelloLab.Greeter(fixture.Root)).ShouldBeTrue();

        // Explain and verify can't skip ahead.
        Should.Throw<AscentException>(() => labs.Explain(Lab, Words(30), TeachBacks(game))).NextStep!.ShouldContain("ascent verify");
        (await Should.ThrowAsync<AscentException>(() => labs.VerifyAsync(Lab, TestContext.Current.CancellationToken))).NextStep!.ShouldContain("ascent flag");

        labs.SubmitFlag(Lab, " " + flag + "\n").Outcome.ShouldBe(FlagOutcome.Accepted);
        labs.Stage(Lab).ShouldBe(LabStage.FlagCaptured);
        Should.Throw<UsageException>(() => labs.SubmitFlag(Lab, flag)).Message.ShouldContain("already captured");

        PassTests(game, failed: 0);
        var outcome = await labs.VerifyAsync(Lab, TestContext.Current.CancellationToken);
        outcome.NowFixed.ShouldBeTrue();
        outcome.Run.AllPassed.ShouldBeTrue();
        (await labs.VerifyAsync(Lab, TestContext.Current.CancellationToken)).NowFixed.ShouldBeFalse();

        labs.Explain(Lab, Words(30), TeachBacks(game)).ShouldEndWith(Lab + ".md");
        labs.Stage(Lab).ShouldBe(LabStage.Explained);
        labs.Explain(Lab, Words(40), TeachBacks(game));

        game.Ledger.Events.Select(e => (e.Kind, e.Points)).ShouldBe([(XpKind.LabRed, 25), (XpKind.LabBlue, 40), (XpKind.LabExplain, 10)]);
        var state = game.LabStates.Find(Lab)!;
        state.VerifyAttempts.ShouldBe(2);
        state.FlagCapturedUtc.ShouldNotBeNull();
        state.FixedUtc.ShouldNotBeNull();
        state.ExplainedUtc.ShouldNotBeNull();
    }

    [Fact]
    [Trait("Rule", "LABE-02")]
    public async Task Verify_needs_every_test_to_pass()
    {
        using var fixture = HelloLab.Create();
        using var game = Started(fixture);
        game.Labs.SubmitFlag(Lab, await File.ReadAllTextAsync(HelloLab.FlagFile(fixture.Root), TestContext.Current.CancellationToken));

        PassTests(game, failed: 1);
        var failing = await game.Labs.VerifyAsync(Lab, TestContext.Current.CancellationToken);

        failing.NowFixed.ShouldBeFalse();
        failing.Run.ShouldBe(failing.Run with { Built = true, Total = 2, Passed = 1, Failed = 1 });
        failing.Run.FailedTests.ShouldBe(["The_backdoor_no_longer_hands_out_a_freshly_planted_flag"]);
        game.Labs.Stage(Lab).ShouldBe(LabStage.FlagCaptured);

        // The tests ran in an isolated scope, with ThroughlineRoot pointing at the workspace (P3).
        var run = game.Processes.Commands.Last(c => c.Tool == ExternalTool.Dotnet);
        run.Arguments.ShouldContain("--property:ThroughlineRoot=" + Path.GetFullPath(Path.Join(fixture.Root, "my-work", "throughline")));
        run.Environment["MSBUILDDISABLENODEREUSE"].ShouldBe("1");
        run.WorkingDirectory.ShouldStartWith(fixture.Paths.Unsealed);
        File.Exists(Path.Join(fixture.Paths.Unsealed, "Directory.Build.props")).ShouldBeTrue();
        Directory.EnumerateDirectories(fixture.Paths.Unsealed).ShouldBeEmpty();
    }

    [Fact]
    [Trait("Rule", "LABE-02")]
    public async Task Tests_that_do_not_build_report_why()
    {
        using var fixture = HelloLab.Create();
        using var game = Started(fixture);
        game.Labs.SubmitFlag(Lab, await File.ReadAllTextAsync(HelloLab.FlagFile(fixture.Root), TestContext.Current.CancellationToken));
        game.Processes.Installed.Add(ExternalTool.Dotnet);
        game.Processes.Respond = _ => new ProcessResult(1, "Restore complete\nerror CS1002: ; expected\n", string.Empty, false, false);

        var outcome = await game.Labs.VerifyAsync(Lab, TestContext.Current.CancellationToken);

        outcome.Run.Built.ShouldBeFalse();
        outcome.Run.AllPassed.ShouldBeFalse();
        outcome.Run.Problem.ShouldBe("Restore complete\nerror CS1002: ; expected");
    }

    [Fact]
    [Trait("Rule", "LABE-04")]
    [Trait("Rule", "FLAG-01")]
    public async Task Starting_again_plants_a_new_flag_and_keeps_your_work_until_you_reset()
    {
        using var fixture = HelloLab.Create();
        using var game = Started(fixture);
        var first = await File.ReadAllTextAsync(HelloLab.FlagFile(fixture.Root), TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(HelloLab.Greeter(fixture.Root), "// my fix", TestContext.Current.CancellationToken);
        game.Labs.SubmitFlag(Lab, first).Outcome.ShouldBe(FlagOutcome.Accepted);

        var again = await game.Labs.UpAsync(Lab, TestContext.Current.CancellationToken);

        again.FirstStart.ShouldBeFalse();
        again.ModuleFiles.ShouldBe(0);
        game.Labs.Stage(Lab).ShouldBe(LabStage.FlagCaptured);
        (await File.ReadAllTextAsync(HelloLab.FlagFile(fixture.Root), TestContext.Current.CancellationToken)).ShouldNotBe(first);
        (await File.ReadAllTextAsync(HelloLab.Greeter(fixture.Root), TestContext.Current.CancellationToken)).ShouldBe("// my fix");

        game.Labs.Reset(Lab).ShouldBe(1);
        (await File.ReadAllTextAsync(HelloLab.Greeter(fixture.Root), TestContext.Current.CancellationToken)).ShouldContain("debug backdoor");
        game.Labs.Stage(Lab).ShouldBe(LabStage.FlagCaptured);
    }

    [Fact]
    [Trait("Rule", "FLAG-02")]
    [Trait("Rule", "FLAG-03")]
    public async Task Wrong_flags_get_no_hint_and_too_many_start_a_cooldown()
    {
        using var fixture = HelloLab.Create();
        using var game = Started(fixture);
        var flag = await File.ReadAllTextAsync(HelloLab.FlagFile(fixture.Root), TestContext.Current.CancellationToken);

        for (var i = 0; i < FlagService.CooldownAttempts; i++)
        {
            game.Labs.SubmitFlag(Lab, "ASCENT{NOTTHEFLAG}").Outcome.ShouldBe(FlagOutcome.Rejected);
        }

        game.Labs.SubmitFlag(Lab, flag).Outcome.ShouldBe(FlagOutcome.CoolingDown);
        game.Clock.Advance(FlagService.CooldownLength);
        game.Labs.SubmitFlag(Lab, flag).Outcome.ShouldBe(FlagOutcome.Accepted);
    }

    [Fact]
    [Trait("Rule", "SEAL-03")]
    public async Task Labs_need_the_rules_of_engagement_the_right_release_and_windows_for_windows_labs()
    {
        using var fixture = HelloLab.Create().Lab("lab-d7-90", "7.1", bonus: true, windowsOnly: true).Lab("lab-d2-01", "2.1", release: 2);
        using var game = new GameHarness(fixture);
        var ct = TestContext.Current.CancellationToken;

        (await Should.ThrowAsync<AscentException>(() => game.Labs.UpAsync(Lab, ct))).NextStep.ShouldBe("Run 'ascent rules'.");
        game.AcceptRules();
        (await Should.ThrowAsync<UsageException>(() => game.Labs.UpAsync("lab-d2-01", ct))).Message.ShouldBe("This Lab runs on Release 2, and your workspace is on Release 0.");
        Should.Throw<UsageException>(() => game.Labs.Find("lab-nope")).NextStep.ShouldBe("Open a Quest to see its Labs: ascent next");
        Should.Throw<AscentException>(() => game.Labs.SubmitFlag(Lab, "x")).Message.ShouldBe("This Lab hasn't started.");
        Should.Throw<UsageException>(() => game.Labs.Reset(Lab));

        game.IsWindows = false;
        (await Should.ThrowAsync<AscentException>(() => game.Labs.UpAsync("lab-d7-90", ct))).Message.ShouldContain("needs Windows");
        game.Labs.Stage("lab-d7-90").ShouldBe(LabStage.NotStarted);
    }

    [Fact]
    public async Task Lab_down_asks_the_local_stage_to_stop()
    {
        using var fixture = HelloLab.Create();
        using var game = Started(fixture);

        (await game.Labs.DownAsync(Lab, TestContext.Current.CancellationToken)).Message.ShouldBe("Fixture stage down.");
        game.Orchestrator.Down.ShouldBe([Lab]);
    }

    [Fact]
    public async Task The_production_stage_points_the_learner_at_the_apphost()
    {
        var orchestrator = new LocalStageOrchestrator();
        var lab = new LabInfo(Lab, "1.1", 0, false, false, 25, 40, 10) { Services = ["api", "web"] };
        using var temp = new TempDirectory();
        var request = new StageRequest(lab, temp.Path, temp.Combine("throughline"));

        (await orchestrator.UpAsync(request, TestContext.Current.CancellationToken)).Message.ShouldContain("no AppHost yet");
        temp.WriteFile("throughline/apphost/AppHost.csproj", "<Project />");
        var up = await orchestrator.UpAsync(request, TestContext.Current.CancellationToken);
        up.Running.ShouldBeFalse();
        up.Message.ShouldContain("dotnet run --project " + Path.Join("my-work", "throughline", "apphost"));
        up.Message.ShouldContain("api, web");
        (await orchestrator.DownAsync(request, TestContext.Current.CancellationToken)).Message.ShouldContain("Ctrl+C");
    }

    internal static GameHarness Started(CurriculumFixture fixture)
    {
        var game = new GameHarness(fixture);
        game.AcceptRules();
        var result = game.Labs.UpAsync(Lab, TestContext.Current.CancellationToken).GetAwaiter().GetResult();
        result.FirstStart.ShouldBeTrue();
        result.ModuleFiles.ShouldBe(1);
        return game;
    }

    // Answers 'dotnet run' by writing a TRX report with the hello-lab's two tests.
    internal static void PassTests(GameHarness game, int failed)
    {
        game.Processes.Installed.Add(ExternalTool.Dotnet);
        game.Processes.Respond = command =>
        {
            var arguments = command.Arguments.ToList();
            File.WriteAllText(arguments[arguments.IndexOf("-result-trx") + 1], Trx(failed));
            return RecordingProcessRunner.Ok("Test run summary");
        };
    }

    internal static string Trx(int failed) =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
        + "<TestRun xmlns=\"http://microsoft.com/schemas/VisualStudio/TeamTest/2010\"><Results>"
        + "<UnitTestResult testName=\"Visitors_are_greeted_by_name\" outcome=\"Passed\" />"
        + "<UnitTestResult testName=\"The_backdoor_no_longer_hands_out_a_freshly_planted_flag\" outcome=\"" + (failed > 0 ? "Failed" : "Passed") + "\" />"
        + "</Results><ResultSummary outcome=\"Completed\"><Counters total=\"2\" executed=\"2\" passed=\"" + (2 - failed) + "\" failed=\"" + failed + "\" error=\"0\" timeout=\"0\" aborted=\"0\" /></ResultSummary></TestRun>";

    internal static string Words(int count) => string.Join(' ', Enumerable.Repeat("word", count));

    private static TeachBackService TeachBacks(GameHarness game) => new(game.TeachBackStore, game.Fixture.Paths.DefaultJournal, game.Clock);
}
