using System.Net;
using Ascent.Core.Domain;
using Ascent.Core.Errors;
using Ascent.Core.Platform;
using Ascent.Engine.Tests.Assessment;
using Ascent.Engine.Tests.TestSupport;
using Ascent.Integrations;
using Ascent.Tests.Shared;

namespace Ascent.Engine.Tests.Integrations;

/// <summary>The environment doctor, Content Bugs and the portfolio (E1-03, BUG-01..02, SU-05, PORT-01..03).</summary>
public sealed class IntegrationTests
{
    [Fact]
    public async Task The_doctor_marks_missing_optional_tools_as_optional_with_install_hints()
    {
        var runner = new RecordingProcessRunner { Installed = { ExternalTool.Dotnet, ExternalTool.Docker, ExternalTool.Az } };
        runner.Respond = c => c.Tool switch
        {
            ExternalTool.Dotnet => RecordingProcessRunner.Ok("10.0.303\n"),
            ExternalTool.Docker => RecordingProcessRunner.Fail("Cannot connect to the Docker daemon"),
            ExternalTool.Az => RecordingProcessRunner.Ok("azure-cli                         2.67.0\n\ncore 2.67.0\n"),
            _ => null,
        };
        using var temp = new TempDirectory();

        var checks = await new EnvironmentDoctor(runner, temp.Path).CheckToolsAsync(TestContext.Current.CancellationToken);

        Find(checks, ".NET SDK").ShouldBe(new DoctorCheck(DoctorArea.LocalStage, ".NET SDK", DoctorStatus.Ok, "10.0.303"));
        Find(checks, "Docker").ShouldBe(Find(checks, "Docker") with { Status = DoctorStatus.Problem, Detail = "installed, but not working" });
        Find(checks, "Azure CLI").Detail.ShouldBe("azure-cli 2.67.0");
        Find(checks, "uv (Python)").ShouldBe(Find(checks, "uv (Python)") with { Status = DoctorStatus.Optional, Detail = "not found" });
        Find(checks, "uv (Python)").Hint!.ShouldContain("docs.astral.sh");
        Find(checks, "git").Status.ShouldBe(DoctorStatus.Optional);
        checks.Where(c => c.Status != DoctorStatus.Ok).ShouldAllBe(c => c.Hint != null);
        runner.Commands.ShouldAllBe(c => c.Timeout == ToolTimeouts.Probe && c.WorkingDirectory == temp.Path);
    }

    [Fact]
    public async Task An_old_sdk_is_a_problem()
    {
        var runner = new RecordingProcessRunner { Installed = { ExternalTool.Dotnet }, Respond = _ => RecordingProcessRunner.Ok("8.0.400") };

        var checks = await new EnvironmentDoctor(runner, Path.GetTempPath()).CheckToolsAsync(TestContext.Current.CancellationToken);

        Find(checks, ".NET SDK").Status.ShouldBe(DoctorStatus.Problem);
        Find(checks, ".NET SDK").Hint!.ShouldContain(".NET 10");
    }

    [Fact]
    [Trait("Rule", "PRV-01")]
    public async Task The_ai_endpoint_is_only_contacted_when_one_is_configured()
    {
        var handler = new RecordingHttpHandler();
        var endpoint = new Uri("http://localhost:11434/v1");
        using var http = EngineHttp.Create(new NetworkPolicy(endpoint), "1.0.0", handler);
        var ct = TestContext.Current.CancellationToken;

        (await EnvironmentDoctor.CheckAiEndpointAsync(http, null, ct)).Status.ShouldBe(DoctorStatus.Optional);
        handler.Requests.ShouldBeEmpty();

        (await EnvironmentDoctor.CheckAiEndpointAsync(http, endpoint, ct)).ShouldBe(new DoctorCheck(DoctorArea.AiReviewer, "AI endpoint", DoctorStatus.Ok, "http://localhost:11434"));
        handler.Requests.Single().Uri.ShouldBe(new Uri("http://localhost:11434/v1/models"));

        handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);
        (await EnvironmentDoctor.CheckAiEndpointAsync(http, endpoint, ct)).Detail.ShouldBe("answered HTTP 500");
        handler.Respond = _ => throw new HttpRequestException("refused");
        (await EnvironmentDoctor.CheckAiEndpointAsync(http, endpoint, ct)).Detail.ShouldBe("not reachable");
    }

    [Fact]
    [Trait("Rule", "BUG-01")]
    public void Bug_reports_carry_only_the_item_and_citation_ids()
    {
        var url = IssueUrlBuilder.Build("qb-5.1-007", "c2");

        url.Host.ShouldBe("github.com");
        url.AbsolutePath.ShouldBe("/JJWren/appsec-ascent/issues/new");
        Uri.UnescapeDataString(url.Query).ShouldBe("?template=content-bug.yml&title=[Content Bug] qb-5.1-007: &item-id=qb-5.1-007&citation-id=c2");
        IssueUrlBuilder.Build("lab-d5-01", null).Query.ShouldNotContain("citation-id");
        Should.Throw<UsageException>(() => IssueUrlBuilder.Build("../etc&x=1", null));
        Should.Throw<UsageException>(() => IssueUrlBuilder.Build("q-1.1", "c2&evil=1"));
    }

    [Fact]
    [Trait("Rule", "BUG-02")]
    public async Task Sync_reads_public_issues_by_user_and_label_with_one_retry()
    {
        var handler = new RecordingHttpHandler();
        var calls = 0;
        handler.Respond = _ => ++calls == 1
            ? new HttpResponseMessage(HttpStatusCode.BadGateway)
            : RecordingHttpHandler.Json("""
                [
                  { "number": 12, "state": "closed", "body": "### Item ID\n\nqb-1.1-001\n\n### Citation ID (if applicable)\n\n_No response_" },
                  { "number": 13, "state": "open", "body": "### Item ID\r\n\r\nlab-d5-01\r\n" },
                  { "number": 14, "state": "open", "body": "no form here" },
                  { "number": 15, "state": "open", "body": "### Item ID\n\nq-1.1", "pull_request": {} }
                ]
                """);
        using var http = EngineHttp.Create(new NetworkPolicy(), "1.0.0", handler);
        var client = new GitHubIssuesClient(http, new ImmediateTimeProvider(DateTimeOffset.UnixEpoch));

        var issues = await client.ConfirmedAsync("ada-lovelace", TestContext.Current.CancellationToken);

        issues.ShouldBe([new ConfirmedIssue(12, "qb-1.1-001", true), new ConfirmedIssue(13, "lab-d5-01", false)]);
        handler.Requests.Count.ShouldBe(2);
        var uri = handler.Requests[1].Uri;
        uri.Host.ShouldBe("api.github.com");
        Uri.UnescapeDataString(uri.Query).ShouldBe("?state=all&per_page=100&creator=ada-lovelace&labels=content-bug:confirmed");

        handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.Forbidden);
        (await Should.ThrowAsync<AscentException>(() => client.ConfirmedAsync("ada", TestContext.Current.CancellationToken))).NextStep!.ShouldContain("try again in an hour");
        handler.Respond = _ => RecordingHttpHandler.Json("{ broken");
        (await Should.ThrowAsync<AscentException>(() => client.ConfirmedAsync("ada", TestContext.Current.CancellationToken))).Message.ShouldBe("GitHub sent something unexpected.");
    }

    [Fact]
    [Trait("Rule", "BUG-02")]
    [Trait("Rule", "SU-05")]
    public void Confirmed_bugs_pay_once_and_resolved_ones_resume_their_cards()
    {
        using var fixture = CurriculumFixture.Create();
        using var game = new GameHarness(fixture);
        game.Reviews.Record(StandUpTests.Info("qb-1.1-001", "1.1"), true, Core.Progress.ReviewContext.StandUp, 0);
        var bugs = game.ContentBugs;

        bugs.Suspend("qb-1.1-001").ShouldBeTrue();
        bugs.Suspend("qb-unknown").ShouldBeFalse();
        game.Cards.Find("qb-1.1-001")!.Suspended.ShouldBeTrue();

        var first = bugs.Apply([new ConfirmedIssue(12, "qb-1.1-001", false)]);
        first.NewlyAwarded.ShouldBe([12]);
        first.Resumed.ShouldBeEmpty();
        game.Cards.Find("qb-1.1-001")!.Suspended.ShouldBeTrue();

        var second = bugs.Apply([new ConfirmedIssue(12, "qb-1.1-001", true)]);
        second.NewlyAwarded.ShouldBeEmpty();
        second.Resumed.ShouldBe(["qb-1.1-001"]);
        game.Cards.Find("qb-1.1-001")!.Suspended.ShouldBeFalse();
        game.Ledger.Events.Single().ShouldBe(game.Ledger.Events.Single() with { Kind = XpKind.ContentBug, RefId = "12", Points = 25 });
        new Ascent.Storage.ContentBugStore(game.Database).All().Single().ItemId.ShouldBe("qb-1.1-001");
    }

    [Fact]
    [Trait("Rule", "PORT-01")]
    [Trait("Rule", "PORT-02")]
    [Trait("Rule", "PORT-03")]
    public void The_portfolio_shows_only_rank_badges_domains_exam_ready_and_chosen_deliverables()
    {
        using var temp = new TempDirectory();
        var exporter = new PortfolioExporter(temp.Path);
        var work = temp.WriteFile("my-work/deliverables/dlv-d3-01.yaml", "inventory: []\n");

        Should.Throw<UsageException>(() => exporter.Include("dlv-x", temp.Combine("my-work", "deliverables", "dlv-x.yaml")));
        var copied = exporter.Include("dlv-d3-01", work);
        copied.ShouldBe(temp.Combine("portfolio", "deliverables", "dlv-d3-01.yaml"));
        File.ReadAllText(copied).ShouldBe("inventory: []\n");

        var path = exporter.Write(new PortfolioFacts(
            "AppSec Engineer",
            ["First Blood", "Bug Hunter"],
            ["D1", "D3"],
            ExamReady: false,
            [new PortfolioDeliverable("dlv-d3-01", "Data [classification] inventory")],
            new DateOnly(2026, 10, 5)));

        File.ReadAllText(path).ShouldBe(
            """
            # AppSec Ascent portfolio

            _Written by the AppSec Ascent engine on 2026-10-05._

            - **Rank:** AppSec Engineer
            - **Exam Ready:** no
            - **Domains cleared:** D1, D3
            - **Badges:** First Blood, Bug Hunter

            ## Deliverables

            - [Data \[classification\] inventory](deliverables/dlv-d3-01.yaml)

            ---

            AppSec Ascent is an unofficial study companion for the ISC2 CSSLP® exam. CSSLP is a registered certification mark of ISC2, Inc. This portfolio isn't affiliated with or endorsed by ISC2.

            """.ReplaceLineEndings("\n"));

        var empty = File.ReadAllText(exporter.Write(new PortfolioFacts("Developer", [], [], true, [], new DateOnly(2026, 10, 5))));
        empty.ShouldContain("- **Badges:** none yet");
        empty.ShouldContain("- **Exam Ready:** yes");
        empty.ShouldNotContain("## Deliverables");
    }

    [Fact]
    [Trait("Rule", "PORT-02")]
    public void The_integrations_assembly_cannot_reach_sealed_content() =>
        typeof(PortfolioExporter).Assembly.GetReferencedAssemblies().Select(a => a.Name).ShouldNotContain("Ascent.Sealing");

    private static DoctorCheck Find(IReadOnlyList<DoctorCheck> checks, string name) => checks.Single(c => c.Name == name);
}
