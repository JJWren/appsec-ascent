using System.Text;
using Ascent.Cli.Tests.TestSupport;
using Ascent.Core.Domain;
using Ascent.Core.Errors;
using Ascent.Core.Platform;
using Ascent.Storage;
using Ascent.Tests.Shared;

namespace Ascent.Cli.Tests;

/// <summary>lab up|down|reset, flag, verify, teachback for Labs, teardown and guardrails, with fake tools.</summary>
public sealed class LabCommandTests
{
    private const string Lab = HelloLab.LabId;

    [Fact]
    [Trait("Rule", "LABE-04")]
    public async Task Lab_up_needs_the_rules_then_starts_once_and_plants_a_fresh_flag_each_time()
    {
        using var fixture = HelloLab.Create();
        using var engine = Engine(fixture);

        var refused = await engine.RunAsync("lab", "up", Lab);
        refused.ExitCode.ShouldBe(ExitCodes.CheckFailed);
        refused.Output.ShouldContain("FAIL: Accept the rules of engagement before any Lab.");

        await engine.RunWithInputAsync(["y"], "rules");
        var first = await engine.RunAsync("lab", "up", Lab);
        first.ExitCode.ShouldBe(ExitCodes.Ok);
        first.Output.ShouldContain("Fixture stage up.");
        first.Output.ShouldContain("Read the brief in labs/" + Lab + "/BRIEF.md.");
        var flag = File.ReadAllText(HelloLab.FlagFile(fixture.Root));

        var second = await engine.RunAsync("lab", "up", Lab);
        second.Output.ShouldContain("PASS: A fresh Flag is planted. Your work in my-work/ is untouched.");
        File.ReadAllText(HelloLab.FlagFile(fixture.Root)).ShouldNotBe(flag);

        (await engine.RunAsync("lab", "down", Lab)).Output.ShouldContain("Fixture stage down.");
        (await engine.RunWithInputAsync(["n"], "lab", "reset", Lab)).Output.ShouldContain("Nothing was changed.");
        (await engine.RunWithInputAsync(["y"], "lab", "reset", Lab)).Output.ShouldContain("PASS: Reset 1 file(s).");
        (await engine.RunAsync("lab", "up", "lab-nope")).ExitCode.ShouldBe(ExitCodes.Usage);
    }

    [Fact]
    [Trait("Rule", "FLAG-03")]
    public async Task Flag_says_only_whether_it_matched()
    {
        using var fixture = HelloLab.Create();
        using var engine = await StartedAsync(fixture);

        var wrong = await engine.RunWithInputAsync(["ASCENT{NOPE}"], "flag", Lab);
        wrong.ExitCode.ShouldBe(ExitCodes.CheckFailed);
        wrong.Output.ShouldContain("FAIL: That isn't the Flag.");

        for (var i = 0; i < 4; i++)
        {
            await engine.RunWithInputAsync(["ASCENT{NOPE}"], "flag", Lab);
        }

        var cooling = await engine.RunWithInputAsync(["ASCENT{NOPE}"], "flag", Lab);
        cooling.Output.ShouldContain("FAIL: Too many wrong tries. Try again after 12:01:00 UTC.");

        engine.Clock.Advance(TimeSpan.FromMinutes(2));
        var right = await engine.RunWithInputAsync([File.ReadAllText(HelloLab.FlagFile(fixture.Root))], "flag", Lab);
        right.Output.ShouldContain("PASS: Flag accepted: Red step done, +25 XP.");
        (await engine.RunWithInputAsync(["x"], "flag", Lab)).ExitCode.ShouldBe(ExitCodes.Usage);
    }

    [Fact]
    [Trait("Rule", "LABE-02")]
    [Trait("Rule", "LABE-03")]
    public async Task Verify_reports_failures_then_the_teach_back_completes_the_lab()
    {
        using var fixture = HelloLab.Create();
        using var engine = await StartedAsync(fixture);
        (await engine.RunAsync("verify", Lab)).Output.ShouldContain("verify unlocks once you've captured this Lab's Flag.");
        await engine.RunWithInputAsync([File.ReadAllText(HelloLab.FlagFile(fixture.Root))], "flag", Lab);
        var runner = (RecordingProcessRunner)engine.Processes;
        runner.Installed.Add(ExternalTool.Dotnet);

        runner.Respond = _ => new ProcessResult(1, "error CS1002: ; expected", string.Empty, false, false);
        var broken = await engine.RunAsync("verify", Lab);
        broken.ExitCode.ShouldBe(ExitCodes.CheckFailed);
        broken.Output.ShouldContain("FAIL: The tests didn't run:\n  error CS1002: ; expected".ReplaceLineEndings());

        runner.Respond = TrxWriter(failed: 1);
        var failing = await engine.RunAsync("verify", Lab);
        failing.Output.ShouldContain("FAIL: 1 of 2 security test(s) fail:");
        failing.Output.ShouldContain("  The_backdoor_no_longer_hands_out_a_freshly_planted_flag");

        (await engine.RunAsync("teachback", Lab, "--text", "Too soon.")).Output.ShouldContain("The Explain step comes after your fix passes.");

        runner.Respond = TrxWriter(failed: 0);
        var passing = await engine.RunAsync("verify", Lab);
        passing.Output.ShouldContain("Blue step done, +40 XP. Now explain what you found and fixed: ascent teachback " + Lab);
        (await engine.RunAsync("verify", Lab)).Output.ShouldContain("This Lab was already fixed.");

        var tooShort = await engine.RunAsync("teachback", Lab, "--text", "It was a backdoor.");
        tooShort.ExitCode.ShouldBe(ExitCodes.Usage);
        tooShort.Output.ShouldContain("needs 30–150 words");

        var prompted = await engine.RunWithInputAsync(["Short.", string.Join(' ', Enumerable.Repeat("word", 30))], "teachback", Lab);
        prompted.Output.ShouldContain("Use 30–150 words.");
        prompted.Output.ShouldContain("Explain step done, +10 XP. Lab complete.");
        (await engine.RunAsync("teachback", Lab, "--text", string.Join(' ', Enumerable.Repeat("again", 30)))).Output.ShouldNotContain("+10 XP");
    }

    [Fact]
    [Trait("Rule", "CLD-01")]
    [Trait("Rule", "CLD-02")]
    public async Task Cloud_stages_wait_for_the_guardrails_then_show_the_estimate_and_ask_first()
    {
        using var fixture = Cloud(paid: false);
        using var engine = await StartedAsync(fixture, "lab-cloud");
        var runner = (RecordingProcessRunner)engine.Processes;

        var noAz = await engine.RunAsync("lab", "up", "lab-cloud", "--cloud");
        noAz.ExitCode.ShouldBe(ExitCodes.CheckFailed);
        noAz.Output.ShouldContain("FAIL: Azure CLI not installed");
        noAz.Output.ShouldContain("Cloud Stages stay blocked until the guardrails are in place (CLD-01).");

        runner.Installed.Add(ExternalTool.Az);
        runner.Respond = Azure(budgets: "[{\"amount\": 5}]");
        var partial = await engine.RunAsync("guardrails", "check");
        partial.ExitCode.ShouldBe(ExitCodes.CheckFailed);
        partial.Output.ShouldContain("PASS: Signed in to Pay-As-You-Go.");
        partial.Output.ShouldContain("FAIL: $10 budget alert missing.");
        partial.Output.ShouldContain("Set them up with 'ascent guardrails apply'.");

        runner.Respond = Azure(budgets: "[{\"amount\": 5}, {\"amount\": 10}, {\"amount\": 20}]");
        (await engine.RunAsync("guardrails", "check")).ExitCode.ShouldBe(ExitCodes.Ok);
        var declined = await engine.RunWithInputAsync(["n"], "lab", "up", "lab-cloud", "--cloud");
        declined.Output.ShouldContain("Estimate: $0.25, within free grants.");
        declined.Output.ShouldContain("  az group create --name ascent-lab-cloud-202610031200 --location eastus --tags ascent:lab=lab-cloud expires-on=2026-10-03T16:00:00Z");
        declined.Output.ShouldContain("Nothing was deployed.");

        engine.HttpTransport = new RecordingHttpHandler { Respond = _ => RecordingHttpHandler.Json("{\"Items\": [{\"type\": \"Consumption\", \"retailPrice\": 0.01}]}") };
        var deployed = await engine.RunWithInputAsync(["y"], "lab", "up", "lab-cloud", "--cloud", "--refresh-estimate");
        deployed.Output.ShouldContain("Refreshed from the Azure Retail Prices API: $1.25.");
        deployed.Output.ShouldContain("PASS: Deployed. Tear it down before it expires for the Teardown Bonus: ascent teardown");
        runner.Lines(ExternalTool.Az).ShouldContain(l => l.StartsWith("deployment group create --resource-group ascent-lab-cloud-202610031200", StringComparison.Ordinal));

        var local = await engine.RunAsync("lab", "up", Lab, "--cloud");
        local.ExitCode.ShouldBe(ExitCodes.Usage);
        local.Output.ShouldContain("This Lab has no Cloud Stage.");
    }

    [Fact]
    [Trait("Rule", "CLD-02")]
    public async Task A_paid_side_quest_needs_the_lab_id_typed()
    {
        using var fixture = Cloud(paid: true);
        using var engine = await StartedAsync(fixture, "lab-cloud");
        var runner = (RecordingProcessRunner)engine.Processes;
        runner.Installed.Add(ExternalTool.Az);
        runner.Respond = Azure(budgets: "[{\"amount\": 5}, {\"amount\": 10}, {\"amount\": 20}]");

        (await engine.RunWithInputAsync(["yes"], "lab", "up", "lab-cloud", "--cloud")).Output.ShouldContain("Nothing was deployed.");
        (await engine.RunWithInputAsync(["lab-cloud"], "lab", "up", "lab-cloud", "--cloud")).Output.ShouldContain("PASS: Deployed.");
    }

    [Fact]
    [Trait("Rule", "CLD-04")]
    [Trait("Rule", "CLD-05")]
    public async Task Teardown_confirms_deletes_and_checks_and_status_flags_overruns()
    {
        using var fixture = HelloLab.Create();
        using var engine = Engine(fixture);
        var runner = (RecordingProcessRunner)engine.Processes;
        (await engine.RunAsync("teardown")).Output.ShouldContain("The Azure CLI isn't installed");

        runner.Installed.Add(ExternalTool.Az);
        using (var database = LearnerCommandTests.Open(engine))
        {
            var now = engine.Clock.GetUtcNow();
            new CloudDeploymentStore(database).Add(Lab, "ascent-old", now.AddHours(-9), now.AddHours(-5));
        }

        var gone = false;
        runner.Respond = c => (c.Arguments[0], c.Arguments[1]) switch
        {
            ("group", "exists") => RecordingProcessRunner.Ok(gone ? "false" : "true"),
            ("group", "list") => RecordingProcessRunner.Ok(gone ? "[]" : "[{\"name\": \"ascent-old\"}]"),
            ("group", "delete") => Delete(() => gone = true),
            _ => null,
        };

        var status = await engine.RunAsync("status");
        status.Output.ShouldContain("WARN: The Cloud Stage 'ascent-old' expired at 2026-10-03 07:00 UTC and is still running: -20 XP (once). Remove it with 'ascent teardown'.");

        (await engine.RunWithInputAsync(["n"], "teardown")).Output.ShouldContain("Nothing was deleted.");
        var torn = await engine.RunWithInputAsync(["y"], "teardown");
        torn.ExitCode.ShouldBe(ExitCodes.Ok);
        torn.Output.ShouldContain("Deleted ascent-old.");
        torn.Output.ShouldContain("PASS: Torn down, and nothing tagged ascent:lab is left.");
        torn.Output.ShouldNotContain("Teardown Bonus");

        runner.Respond = c => c.Arguments[1] == "list" ? RecordingProcessRunner.Ok("[{\"name\": \"ascent-stuck\"}]") : null;
        var stuck = await engine.RunWithInputAsync(["y"], "teardown");
        stuck.ExitCode.ShouldBe(ExitCodes.CheckFailed);
        stuck.Output.ShouldContain("FAIL: Still there: ascent-stuck.");
    }

    [Fact]
    [Trait("Rule", "CLD-01")]
    public async Task Guardrails_apply_shows_the_deployment_and_asks_first()
    {
        using var fixture = HelloLab.Create();
        using var engine = Engine(fixture);
        var runner = (RecordingProcessRunner)engine.Processes;
        (await engine.RunAsync("guardrails", "apply")).Output.ShouldContain("Azure CLI not installed");

        runner.Installed.Add(ExternalTool.Az);
        runner.Respond = Azure(budgets: "[]");
        Directory.CreateDirectory(Path.Join(fixture.Root, "my-work", "throughline"));
        (await engine.RunAsync("guardrails", "apply")).Output.ShouldContain("The guardrail template isn't in your workspace yet.");

        fixture.Write("my-work/throughline/infra/guardrails/main.bicep", "targetScope = 'subscription'");
        (await engine.RunWithInputAsync(["n"], "guardrails", "apply")).Output.ShouldContain("Nothing was deployed.");
        var applied = await engine.RunWithInputAsync(["y"], "guardrails", "apply");
        applied.Output.ShouldContain("This deploys budget alerts at $5, $10 and $20 and a Policy allow-list to Pay-As-You-Go:");
        applied.Output.ShouldContain("  az deployment sub create --name ascent-guardrails --location eastus --template-file");
        runner.Lines(ExternalTool.Az).ShouldContain(l => l.StartsWith("deployment sub create", StringComparison.Ordinal));
    }

    internal static TestEngine Engine(CurriculumFixture fixture)
    {
        var engine = TestEngine.For(fixture);
        engine.Orchestrator = new FixtureOrchestrator();
        return engine;
    }

    internal static async Task<TestEngine> StartedAsync(CurriculumFixture fixture, string lab = Lab)
    {
        var engine = Engine(fixture);
        await engine.RunWithInputAsync(["y"], "rules");
        (await engine.RunAsync("lab", "up", lab)).ExitCode.ShouldBe(ExitCodes.Ok);
        return engine;
    }

    // Answers 'dotnet run' by writing a TRX report for the hello-lab's two tests.
    internal static Func<ToolCommand, ProcessResult?> TrxWriter(int failed) => command =>
    {
        var arguments = command.Arguments.ToList();
        File.WriteAllText(
            arguments[arguments.IndexOf("-result-trx") + 1],
            "<TestRun xmlns=\"http://microsoft.com/schemas/VisualStudio/TeamTest/2010\"><Results>"
            + "<UnitTestResult testName=\"Visitors_are_greeted_by_name\" outcome=\"Passed\" />"
            + "<UnitTestResult testName=\"The_backdoor_no_longer_hands_out_a_freshly_planted_flag\" outcome=\"" + (failed > 0 ? "Failed" : "Passed") + "\" />"
            + "</Results><ResultSummary><Counters total=\"2\" passed=\"" + (2 - failed) + "\" failed=\"" + failed + "\" /></ResultSummary></TestRun>",
            Encoding.UTF8);
        return RecordingProcessRunner.Ok();
    };

    private static Func<ToolCommand, ProcessResult?> Azure(string budgets) => c => (c.Arguments[0], c.Arguments[1]) switch
    {
        ("account", "show") => RecordingProcessRunner.Ok("{\"name\": \"Pay-As-You-Go\"}"),
        ("consumption", "budget") => RecordingProcessRunner.Ok(budgets),
        ("policy", "assignment") => RecordingProcessRunner.Ok("[{\"name\": \"ascent-guardrails\"}]"),
        _ => null,
    };

    private static ProcessResult? Delete(Action onDelete)
    {
        onDelete();
        return null;
    }

    private static CurriculumFixture Cloud(bool paid)
    {
        var fixture = HelloLab.Create()
            .Lab("lab-cloud", "1.1", cloud: "  cloud:\n    bicep: infra/labs/lab-cloud.bicep\n    estimateUsd: 0.25\n    freeTier: " + (paid ? "false" : "true")
                + "\n    paidSideQuest: " + (paid ? "true" : "false") + "\n    teardownWindowHours: 4\n");
        fixture.SealFolder("lab-cloud.module", "lab-module", SealTier.Start, Path.Join(HelloLab.Source, "module"));
        fixture.Seal("lab-cloud.plant", "lab-plant", SealTier.Start, Encoding.UTF8.GetBytes("{\"kind\":\"file\",\"path\":\"throughline/.flags/cloud.txt\"}"));
        fixture.Write("throughline/infra/labs/lab-cloud.bicep", "param location string");
        fixture.Write("throughline/infra/labs/lab-cloud.prices.json", "{\"items\": [{\"filter\": \"meterName eq 'vCPU'\", \"quantity\": 125}]}");
        return fixture;
    }
}
