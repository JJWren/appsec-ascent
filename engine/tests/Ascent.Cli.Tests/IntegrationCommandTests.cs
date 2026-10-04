using System.Net;
using Ascent.Cli.Tests.TestSupport;
using Ascent.Core.Domain;
using Ascent.Core.Errors;
using Ascent.Core.Platform;
using Ascent.Core.Profile;
using Ascent.Core.Progress;
using Ascent.Storage;
using Ascent.Tests.Shared;

namespace Ascent.Cli.Tests;

/// <summary>start's workspace offer, release next, doctor, bug, sync and portfolio.</summary>
public sealed class IntegrationCommandTests
{
    [Fact]
    [Trait("Rule", "REL-03")]
    public async Task Start_creates_the_workspace_and_offers_once_to_make_it_a_repository()
    {
        using var fixture = CurriculumFixture.Create().Write("throughline/README.md", "Release 0");
        using var engine = TestEngine.For(fixture);
        var git = (RecordingProcessRunner)engine.Processes;

        (await engine.RunAsync("start")).Output.ShouldContain("Created your workspace in my-work/, which git ignores in this repository.");
        File.ReadAllText(Path.Join(fixture.Root, "my-work", "throughline", "README.md")).ShouldBe("Release 0");
        git.Commands.ShouldBeEmpty();

        git.Installed.Add(ExternalTool.Git);
        var offered = await engine.RunWithInputAsync(["y"], "start");
        offered.Output.ShouldContain("  git commit --quiet --message \"Start my AppSec Ascent workspace\"");
        offered.Output.ShouldContain("PASS: my-work/ is now a git repository.");
        git.Lines(ExternalTool.Git).ShouldBe(["init", "add --all", "commit --quiet --message Start my AppSec Ascent workspace"]);
        LearnerCommandTests.Profile(engine).Values.ShouldContainKey(ProfileKeys.WorkspaceOfferedUtc);

        (await engine.RunAsync("start")).Output.ShouldNotContain("git repository");
    }

    [Fact]
    [Trait("Rule", "REL-01")]
    [Trait("Rule", "REL-02")]
    public async Task Release_next_waits_for_the_domain_then_lists_its_steps_and_switches()
    {
        using var fixture = Releases();
        using var engine = TestEngine.For(fixture);

        var early = await engine.RunAsync("release", "next");
        early.ExitCode.ShouldBe(ExitCodes.Usage);
        early.Output.ShouldContain("Release 1 unlocks once you finish q-ori-1.");

        (await engine.RunWithInputAsync(["ORI?"], "release", "next", "--skip")).Output.ShouldContain("Nothing was changed.");

        var skipped = await engine.RunWithInputAsync(["ORI", "y"], "release", "next", "--skip");
        skipped.Output.ShouldContain("WARN: Skipping the rest of ORI");
        skipped.Output.ShouldContain("WARN: my-work/throughline/ will be replaced without an archive of your ORI work.");
        skipped.Output.ShouldContain("PASS: Release 1 is in my-work/throughline/ (1 files).");
        File.ReadAllText(Path.Join(fixture.Root, "my-work", "throughline", "release.txt")).ShouldBe("Release 1");
    }

    [Fact]
    [Trait("Rule", "REL-03")]
    public async Task Release_next_offers_a_repository_and_archives_before_switching()
    {
        using var fixture = Releases();
        using var engine = TestEngine.For(fixture);
        await engine.RunAsync("teachback", "q-ori-1", "--text", "Done.");
        Directory.CreateDirectory(Path.Join(fixture.Root, "my-work", "throughline"));
        var git = (RecordingProcessRunner)engine.Processes;
        git.Installed.Add(ExternalTool.Git);
        git.Respond = c => c.Arguments[0] == "init" ? Init(fixture) : null;

        (await engine.RunWithInputAsync(["y", "n"], "release", "next")).Output.ShouldContain("Nothing was changed.");
        var switched = await engine.RunWithInputAsync(["y"], "release", "next");

        switched.Output.ShouldContain("  git branch --force portfolio/orientation");
        switched.Output.ShouldContain("  (my-work/throughline/ is replaced with Release 1)");
        switched.Output.ShouldContain("Your ORI work is on the branch portfolio/orientation.");
        git.Lines(ExternalTool.Git).ShouldContain("branch --force portfolio/orientation");
        git.Lines(ExternalTool.Git).ShouldContain("commit --quiet --allow-empty --message Release 1 baseline");
    }

    [Fact]
    public async Task Doctor_reports_each_area_and_fails_only_on_required_problems()
    {
        using var fixture = CurriculumFixture.Create();
        using var engine = TestEngine.For(fixture);
        var runner = (RecordingProcessRunner)engine.Processes;
        runner.Installed.Add(ExternalTool.Dotnet);
        runner.Respond = c => c.Tool == ExternalTool.Dotnet ? RecordingProcessRunner.Ok("10.0.303") : null;

        var missingDocker = await engine.RunAsync("doctor");
        missingDocker.ExitCode.ShouldBe(ExitCodes.CheckFailed);
        missingDocker.Output.ShouldContain("Local Stage");
        missingDocker.Output.ShouldContain("FAIL: Docker: Install Docker Desktop");
        missingDocker.Output.ShouldContain("INFO: AI endpoint: The AI reviewer is optional.");
        missingDocker.Output.ShouldContain("Trusted key");
        missingDocker.Output.ShouldContain("not created yet");

        runner.Installed.Add(ExternalTool.Docker);
        runner.Installed.Add(ExternalTool.Az);
        runner.Respond = c => c.Tool switch
        {
            ExternalTool.Dotnet => RecordingProcessRunner.Ok("10.0.303"),
            ExternalTool.Az when c.Arguments[0] == "account" => RecordingProcessRunner.Fail("Please run 'az login'."),
            _ => RecordingProcessRunner.Ok("ok"),
        };
        var ready = await engine.RunAsync("doctor");
        ready.ExitCode.ShouldBe(ExitCodes.Ok);
        ready.Output.ShouldContain("Guardrails: Run 'az login', then 'ascent guardrails check'.");
        ready.Output.ShouldContain("ok");
    }

    [Fact]
    [Trait("Rule", "BUG-01")]
    [Trait("Rule", "SU-05")]
    public async Task Bug_prints_and_opens_a_prefilled_report_and_pauses_the_card()
    {
        using var fixture = CurriculumFixture.Create().Question("qb-1.1-001", "1.1");
        using var engine = TestEngine.For(fixture, plain: false);
        using (var database = LearnerCommandTests.Open(engine))
        {
            var now = engine.Clock.GetUtcNow();
            new ReviewCardStore(database).Save(new ReviewCardRecord("qb-1.1-001", "1.1", "D1", "{}", now, now, null, 1, 0, false));
        }

        var (exitCode, output) = await engine.RunAsync("bug", "qb-1.1-001", "c1");

        exitCode.ShouldBe(ExitCodes.Ok);
        output.ShouldContain("https://github.com/JJWren/appsec-ascent/issues/new?template=content-bug.yml");
        output.ShouldContain("Its review card is paused until the bug is resolved");
        engine.OpenedUrls.Single().AbsoluteUri.ShouldContain("item-id=qb-1.1-001");
        using var check = LearnerCommandTests.Open(engine);
        new ReviewCardStore(check).Find("qb-1.1-001")!.Suspended.ShouldBeTrue();
    }

    [Fact]
    [Trait("Rule", "BUG-01")]
    public async Task Bug_in_plain_mode_only_prints_the_link()
    {
        using var fixture = CurriculumFixture.Create();
        using var engine = TestEngine.For(fixture);

        (await engine.RunAsync("bug", "lab-d5-01")).Output.ShouldContain("item-id=lab-d5-01");
        engine.OpenedUrls.ShouldBeEmpty();
        (await engine.RunAsync("bug", "bad id!")).ExitCode.ShouldBe(ExitCodes.Usage);
    }

    [Fact]
    [Trait("Rule", "BUG-02")]
    public async Task Sync_pays_for_confirmed_bugs_once()
    {
        using var fixture = CurriculumFixture.Create();
        using var engine = TestEngine.For(fixture);
        var github = new RecordingHttpHandler { Respond = _ => RecordingHttpHandler.Json("[{\"number\": 7, \"state\": \"open\", \"body\": \"### Item ID\\n\\nqb-1.1-001\"}]") };
        engine.HttpTransport = github;

        (await engine.RunAsync("sync")).Output.ShouldContain("Your GitHub user name isn't set.");
        await engine.RunAsync("config", "githubUser", "ada-lovelace");
        var first = await engine.RunAsync("sync");
        first.Output.ShouldContain("PASS: 1 confirmed Content Bug(s); 1 new, +25 XP.");
        (await engine.RunAsync("sync")).Output.ShouldContain("1 confirmed Content Bug(s); 0 new, +0 XP.");
        github.Requests.ShouldAllBe(r => r.Uri.Host == "api.github.com" && r.Method == HttpMethod.Get);

        github.Respond = _ => new HttpResponseMessage(HttpStatusCode.Forbidden);
        (await engine.RunAsync("sync")).ExitCode.ShouldBe(ExitCodes.CheckFailed);
    }

    [Fact]
    [Trait("Rule", "PORT-01")]
    [Trait("Rule", "PORT-03")]
    public async Task Portfolio_writes_the_summary_and_includes_only_submitted_eligible_work()
    {
        using var fixture = WorkCommandTests.Fixture();
        using var engine = TestEngine.For(fixture);
        using (var database = LearnerCommandTests.Open(engine))
        {
            new XpLedger(new XpStore(database), engine.Clock).Award(XpKind.LabRed, "lab-d5-01");
        }

        var (_, written) = await engine.RunAsync("portfolio");
        written.ShouldContain("PASS: Wrote " + Path.Join("portfolio", "PORTFOLIO.md"));
        var markdown = File.ReadAllText(Path.Join(fixture.Root, "portfolio", "PORTFOLIO.md"));
        markdown.ShouldContain("- **Badges:** First Blood");
        markdown.ShouldContain("- **Exam Ready:** no");

        (await engine.RunAsync("portfolio", "--include", "dlv-d3-01")).Output.ShouldContain("hasn't been submitted yet");
        await engine.RunAsync("deliver", "dlv-d3-01");
        File.WriteAllText(Path.Join(fixture.Root, "my-work", "deliverables", "dlv-d3-01.yaml"), "main:\n  - summary: Done.\n");
        await engine.RunWithInputAsync(["3", "3", "y"], "deliver", "dlv-d3-01");

        var included = await engine.RunAsync("portfolio", "--include", "dlv-d3-01");
        included.Output.ShouldContain("PASS: Copied to " + Path.Join("portfolio", "deliverables", "dlv-d3-01.yaml") + ".");
        File.ReadAllText(Path.Join(fixture.Root, "portfolio", "PORTFOLIO.md")).ShouldContain("](deliverables/dlv-d3-01.yaml)");
        (await engine.RunAsync("portfolio", "--include", "nope")).ExitCode.ShouldBe(ExitCodes.Usage);
    }

    [Fact]
    [Trait("Rule", "PORT-03")]
    public async Task Templates_that_are_not_portfolio_eligible_are_refused()
    {
        using var fixture = WorkCommandTests.Fixture();
        fixture.Write("deliverables/templates/dlv-d3-01.yaml", File.ReadAllText(Path.Join(fixture.Root, "deliverables", "templates", "dlv-d3-01.yaml")).Replace("portfolioEligible: true", "portfolioEligible: false", StringComparison.Ordinal));
        using var engine = TestEngine.For(fixture);

        (await engine.RunAsync("portfolio", "--include", "dlv-d3-01")).Output.ShouldContain("isn't eligible for the portfolio");
    }

    private static ProcessResult? Init(CurriculumFixture fixture)
    {
        Directory.CreateDirectory(Path.Join(fixture.Root, "my-work", ".git"));
        return null;
    }

    private static CurriculumFixture Releases()
    {
        var fixture = CurriculumFixture.Create().Quest("q-ori-1", "ORI-1", "ORI").Write("release-1/release.txt", "Release 1");
        fixture.SealFolder("release-1", "release", SealTier.Release, Path.Join(fixture.Root, "release-1"));
        return fixture;
    }
}
