using Ascent.Cli.Tests.TestSupport;
using Ascent.Core.Domain;
using Ascent.Core.Errors;
using Ascent.Core.Platform;
using Ascent.Core.Progress;
using Ascent.Storage;
using Ascent.Tests.Shared;

namespace Ascent.Cli.Tests;

/// <summary>
/// The hello-lab end-to-end test (P28, TEST-U2-05): a synthetic Lab, sealed at test time, played through the real
/// commands. <c>verify</c> builds and runs real tests with the .NET SDK, in an isolated unsealed scope.
/// </summary>
public sealed class HelloLabTests
{
    [Fact]
    [Trait("Category", "E2E")]
    [Trait("Rule", "LABE-02")]
    [Trait("Rule", "FLAG-04")]
    public async Task Hello_lab_plays_through_red_blue_and_explain()
    {
        using var fixture = HelloLab.Create();
        using var engine = TestEngine.For(fixture);
        engine.Processes = new ProcessRunner();
        engine.Orchestrator = new FixtureOrchestrator();
        (await engine.RunWithInputAsync(["y"], "rules")).ExitCode.ShouldBe(ExitCodes.Ok);
        (await engine.RunAsync("teachback", HelloLab.QuestId, "--text", "Every greeting is a public interface.")).ExitCode.ShouldBe(ExitCodes.Ok);

        // Red: start the Lab, capture the planted Flag, submit it.
        var up = await engine.RunAsync("lab", "up", HelloLab.LabId);
        up.ExitCode.ShouldBe(ExitCodes.Ok);
        up.Output.ShouldContain("Lab started: 1 file(s) unpacked into my-work/throughline/, and a Flag is planted.");
        var flag = await File.ReadAllTextAsync(HelloLab.FlagFile(fixture.Root), TestContext.Current.CancellationToken);
        up.Output.ShouldNotContain(flag);

        var flagged = await engine.RunWithInputAsync([flag], "flag", HelloLab.LabId);
        flagged.Output.ShouldContain("PASS: Flag accepted: Red step done, +25 XP.");
        flagged.Output.ShouldNotContain(flag);

        // Blue: the released tests fail against the vulnerable code, then pass once it's fixed.
        var failing = await engine.RunAsync("verify", HelloLab.LabId);
        failing.ExitCode.ShouldBe(ExitCodes.CheckFailed, failing.Output);
        failing.Output.ShouldContain("FAIL: 1 of 2 security test(s) fail:");
        failing.Output.ShouldContain("The_backdoor_no_longer_hands_out_a_freshly_planted_flag");

        HelloLab.ApplyFix(fixture.Root);
        var passing = await engine.RunAsync("verify", HelloLab.LabId);
        passing.ExitCode.ShouldBe(ExitCodes.Ok, passing.Output);
        passing.Output.ShouldContain("PASS: All 2 security test(s) pass.");
        passing.Output.ShouldContain("Blue step done, +40 XP.");

        // Explain: the Teach-back completes the Lab, and with it the Quest.
        var explained = await engine.RunAsync("teachback", HelloLab.LabId, "--text", string.Join(' ', Enumerable.Repeat("The greeter returned the planted secret to anyone named admin, so I removed the debug branch.", 3)));
        explained.Output.ShouldContain("Explain step done, +10 XP. Lab complete.");
        explained.Output.ShouldContain("PASS: Quest complete: " + HelloLab.QuestId);

        using var database = LearnerCommandTests.Open(engine);
        new LabStateStore(database).Find(HelloLab.LabId)!.Stage.ShouldBe(LabStage.Explained);
        new XpLedger(new XpStore(database), engine.Clock).Total.ShouldBe(10 + 25 + 40 + 10);
        var releases = new KeyReleaseStore(database);
        releases.IsReleased(HelloLab.LabId + ".module", SealTier.Start).ShouldBeTrue();
        releases.IsReleased(HelloLab.LabId + ".plant", SealTier.Start).ShouldBeTrue();
        releases.IsReleased(HelloLab.LabId + ".tests", SealTier.Earned).ShouldBeTrue();
        releases.IsReleased(HelloLab.LabId + ".fix", SealTier.Earned).ShouldBeFalse();
        Directory.EnumerateDirectories(engine.Paths.Unsealed).ShouldBeEmpty();
    }
}
