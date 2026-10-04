using System.Net;
using Ascent.Core.Domain;
using Ascent.Core.Errors;
using Ascent.Core.Platform;
using Ascent.Core.Progress;
using Ascent.Engine.Tests.TestSupport;
using Ascent.Labs;
using Ascent.Labs.Cloud;
using Ascent.Tests.Shared;

namespace Ascent.Engine.Tests.Labs;

/// <summary>Cloud Stages: guardrails, estimates, tagged deployments, teardown and overruns (CLD-01..05).</summary>
public sealed class CloudTests
{
    private const string Lab = "lab-d5-01";

    private const string CloudBlock =
        "  cloud:\n    bicep: infra/labs/lab-d5-01.bicep\n    estimateUsd: 0.25\n    freeTier: true\n    paidSideQuest: false\n    teardownWindowHours: 4\n";

    [Fact]
    [Trait("Rule", "CLD-01")]
    public async Task Guardrails_need_az_a_signed_in_subscription_three_budgets_and_the_policy()
    {
        using var fixture = CurriculumFixture.Create();
        using var game = new GameHarness(fixture);
        var guard = new CostGuard(game.Az, game.Workspace);
        var ct = TestContext.Current.CancellationToken;

        (await guard.CheckAsync(ct)).ShouldBe(new GuardrailStatus(false, false, null, CostGuard.BudgetAmounts, false));

        game.Processes.Installed.Add(ExternalTool.Az);
        game.Processes.Respond = _ => RecordingProcessRunner.Fail("Please run 'az login'.");
        (await guard.CheckAsync(ct)).SignedIn.ShouldBeFalse();

        Azure(game, budgets: "[{\"amount\": 5}, {\"amount\": \"10.0\"}]", policies: "[{\"name\": \"other\"}]");
        var partial = await guard.CheckAsync(ct);
        partial.ShouldBe(partial with { AzureCliFound = true, SignedIn = true, Subscription = "Pay-As-You-Go", PolicyAssigned = false });
        partial.MissingBudgets.ShouldBe([20]);
        partial.Ready.ShouldBeFalse();

        Azure(game, budgets: "[{\"amount\": 5}, {\"amount\": 10}, {\"amount\": 20.0}]", policies: "[{\"name\": \"ascent-guardrails\"}]");
        (await guard.CheckAsync(ct)).Ready.ShouldBeTrue();
        game.Processes.Commands.ShouldAllBe(c => c.Arguments.Contains("--only-show-errors") && c.Environment["AZURE_CORE_COLLECT_TELEMETRY"] == "false");
    }

    [Fact]
    [Trait("Rule", "CLD-01")]
    public async Task Applying_guardrails_deploys_the_template_at_subscription_scope()
    {
        using var fixture = CurriculumFixture.Create();
        using var game = new GameHarness(fixture);
        var guard = new CostGuard(game.Az, game.Workspace);
        game.Processes.Installed.Add(ExternalTool.Az);
        var ct = TestContext.Current.CancellationToken;

        guard.Template.ShouldBeNull();
        (await Should.ThrowAsync<AscentException>(() => guard.ApplyAsync("eastus", ct))).Message.ShouldContain("isn't in your workspace");

        fixture.Write("my-work/throughline/" + CostGuard.TemplatePath, "targetScope = 'subscription'");
        await guard.ApplyAsync("westeurope", ct);
        game.Processes.Lines(ExternalTool.Az).Single().ShouldBe(
            "deployment sub create --name ascent-guardrails --location westeurope --template-file " + guard.Template + " --only-show-errors");
        AzureCli.Display(["group", "list", "a b"]).ShouldBe("az group list \"a b\"");

        game.Processes.Respond = _ => RecordingProcessRunner.Fail("ERROR: (AuthorizationFailed) no access\n");
        (await Should.ThrowAsync<AscentException>(() => guard.ApplyAsync("eastus", ct))).Message.ShouldContain("(AuthorizationFailed) no access");
    }

    [Fact]
    [Trait("Rule", "CLD-02")]
    [Trait("Rule", "CLD-03")]
    public async Task Deployments_are_tagged_with_an_expiry_and_recorded_before_the_template_runs()
    {
        using var fixture = CurriculumFixture.Create().Lab(Lab, "5.1", cloud: CloudBlock).Lab("lab-local", "5.2");
        using var game = new GameHarness(fixture);
        game.Processes.Installed.Add(ExternalTool.Az);
        var lab = game.Labs.Find(Lab);
        lab.Cloud.ShouldBe(new Core.Curriculum.CloudStageInfo("infra/labs/lab-d5-01.bicep", 0.25m, true, false, 4));

        Should.Throw<AscentException>(() => game.Cloud.Plan(lab, "eastus")).Message.ShouldContain("isn't in your workspace");
        Should.Throw<UsageException>(() => game.Cloud.Plan(game.Labs.Find("lab-local"), "eastus"));
        fixture.Write("my-work/throughline/infra/labs/lab-d5-01.bicep", "param location string");
        var plan = game.Cloud.Plan(lab, "eastus");

        plan.ResourceGroup.ShouldBe("ascent-lab-d5-01-202610050900");
        plan.ExpiresUtc.ShouldBe(game.Clock.GetUtcNow().AddHours(4));
        string.Join(' ', plan.Commands[0]).ShouldBe("group create --name ascent-lab-d5-01-202610050900 --location eastus --tags ascent:lab=lab-d5-01 expires-on=2026-10-05T13:00:00Z");
        plan.Commands[1].ShouldContain(plan.Template);

        game.Processes.Respond = c => c.Arguments[0] == "deployment" ? RecordingProcessRunner.Fail("ERROR: InvalidTemplate") : null;
        await Should.ThrowAsync<AscentException>(() => game.Cloud.DeployAsync(plan, TestContext.Current.CancellationToken));
        game.Deployments.Open().Single().ResourceGroup.ShouldBe(plan.ResourceGroup);
        game.Ledger.Total.ShouldBe(0);

        game.Processes.Respond = null;
        await game.Cloud.DeployAsync(game.Cloud.Plan(lab, "eastus"), TestContext.Current.CancellationToken);
        await game.Cloud.DeployAsync(game.Cloud.Plan(lab, "eastus"), TestContext.Current.CancellationToken);
        game.Ledger.Events.Single().ShouldBe(game.Ledger.Events.Single() with { Kind = XpKind.CloudStage, Points = 30, Bonus = true });
    }

    [Fact]
    [Trait("Rule", "CLD-04")]
    public async Task Teardown_deletes_everything_tagged_checks_it_is_gone_and_pays_the_bonus_in_time()
    {
        using var fixture = CurriculumFixture.Create();
        using var game = new GameHarness(fixture);
        game.Processes.Installed.Add(ExternalTool.Az);
        var now = game.Clock.GetUtcNow();
        var early = game.Deployments.Add(Lab, "ascent-a", now, now.AddHours(4));
        var late = game.Deployments.Add(Lab, "ascent-b", now.AddHours(-10), now.AddHours(-6));
        var deleted = new List<string>();
        game.Processes.Respond = c => c.Arguments[1] switch
        {
            "list" => RecordingProcessRunner.Ok(deleted.Count == 0 ? "[{\"name\": \"ascent-a\"}, {\"name\": \"ascent-b\"}]" : "[]"),
            "delete" => Record(deleted, c.Arguments[3]),
            _ => null,
        };

        var result = await game.Cloud.TeardownAsync(TestContext.Current.CancellationToken);

        result.Deleted.ShouldBe(["ascent-a", "ascent-b"]);
        result.Clean.ShouldBeTrue();
        result.Bonuses.ShouldBe(1);
        game.Deployments.Open().ShouldBeEmpty();
        game.Ledger.Events.Single().ShouldBe(game.Ledger.Events.Single() with { Kind = XpKind.TeardownBonus, RefId = "deploy-" + early, Points = 10 });
        late.ShouldBeGreaterThan(early);
        game.Processes.Lines(ExternalTool.Az).ShouldContain("group delete --name ascent-a --yes --only-show-errors");
    }

    [Fact]
    [Trait("Rule", "CLD-04")]
    public async Task A_teardown_that_leaves_something_behind_says_so()
    {
        using var fixture = CurriculumFixture.Create();
        using var game = new GameHarness(fixture);
        game.Processes.Installed.Add(ExternalTool.Az);
        game.Processes.Respond = c => c.Arguments[1] == "list" ? RecordingProcessRunner.Ok("[{\"name\": \"ascent-stuck\"}]") : null;

        var result = await game.Cloud.TeardownAsync(TestContext.Current.CancellationToken);

        result.Clean.ShouldBeFalse();
        result.Remaining.ShouldBe(["ascent-stuck"]);

        game.Processes.Respond = _ => RecordingProcessRunner.Fail();
        (await Should.ThrowAsync<AscentException>(() => game.Cloud.TeardownAsync(TestContext.Current.CancellationToken))).Message.ShouldBe("Couldn't list your resource groups.");
    }

    [Fact]
    [Trait("Rule", "CLD-05")]
    public async Task Overruns_cost_twenty_xp_once_and_deployments_already_gone_are_closed()
    {
        using var fixture = CurriculumFixture.Create();
        using var game = new GameHarness(fixture);
        var now = game.Clock.GetUtcNow();
        game.Ledger.Award(XpKind.Lesson, "q-1.1");
        var running = game.Deployments.Add(Lab, "ascent-running", now.AddHours(-8), now.AddHours(-4));
        game.Deployments.Add(Lab, "ascent-gone", now.AddHours(-8), now.AddHours(-4));
        game.Deployments.Add(Lab, "ascent-fresh", now, now.AddHours(4));
        var ct = TestContext.Current.CancellationToken;

        (await game.Cloud.CheckOverrunsAsync(ct)).ShouldBeEmpty();

        game.Processes.Installed.Add(ExternalTool.Az);
        game.Processes.Respond = c => c.Arguments[1] == "exists" ? RecordingProcessRunner.Ok(c.Arguments[3] == "ascent-running" ? "true" : "false") : null;
        var overruns = await game.Cloud.CheckOverrunsAsync(ct);
        (await game.Cloud.CheckOverrunsAsync(ct)).Single().Id.ShouldBe(running);

        overruns.Single().ResourceGroup.ShouldBe("ascent-running");
        game.Ledger.Events.Count(e => e.Kind == XpKind.TeardownPenalty).ShouldBe(1);
        game.Ledger.Total.ShouldBe(0);
        game.Deployments.Open().Select(d => (d.ResourceGroup, d.Outcome)).ShouldBe([("ascent-running", CloudOutcome.Penalty), ("ascent-fresh", CloudOutcome.None)]);
    }

    [Fact]
    [Trait("Rule", "CLD-02")]
    public async Task Retail_prices_refresh_an_estimate_with_one_retry_and_nothing_but_get()
    {
        using var fixture = CurriculumFixture.Create();
        var template = fixture.Write("infra/x.bicep", "x").Root + "/infra/x.bicep";
        PriceSheet.Read(template).ShouldBeNull();
        fixture.Write("infra/x.prices.json", "{\"items\": [{\"filter\": \"serviceName eq 'Azure Container Apps'\", \"quantity\": 100}, {\"filter\": \"skuName eq 'Free'\", \"quantity\": 2}]}");
        var sheet = PriceSheet.Read(template)!;
        var handler = new RecordingHttpHandler();
        var calls = 0;
        handler.Respond = request => ++calls == 1
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : RecordingHttpHandler.Json("{\"Items\": [{\"type\": \"Reservation\", \"retailPrice\": 9.9}, {\"type\": \"Consumption\", \"retailPrice\": 0.0125}]}");
        using var http = EngineHttp.Create(new NetworkPolicy(), "1.0.0", handler);
        var time = new ImmediateTimeProvider(DateTimeOffset.UnixEpoch);
        var client = new RetailPricesClient(http, time);

        var estimate = await client.EstimateAsync(sheet, TestContext.Current.CancellationToken);

        estimate.ShouldBe(1.28m);
        time.Delays.ShouldBe(1);
        handler.Requests.Count.ShouldBe(3);
        handler.Requests.ShouldAllBe(r => r.Method == HttpMethod.Get && r.Uri.Host == "prices.azure.com");
        Uri.UnescapeDataString(handler.Requests[0].Uri.Query).ShouldBe("?currencyCode='USD'&$filter=serviceName eq 'Azure Container Apps'");

        handler.Respond = _ => RecordingHttpHandler.Json("{\"Items\": []}");
        (await Should.ThrowAsync<AscentException>(() => client.UnitPriceAsync("x", TestContext.Current.CancellationToken))).Message.ShouldContain("No retail price");
        handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.BadRequest);
        (await Should.ThrowAsync<AscentException>(() => client.UnitPriceAsync("x", TestContext.Current.CancellationToken))).Message.ShouldContain("(400)");
        fixture.Write("infra/x.prices.json", "{\"items\": [{\"filter\": 3}]}");
        Should.Throw<AscentException>(() => PriceSheet.Read(template));
        fixture.Write("infra/x.prices.json", "{");
        Should.Throw<AscentException>(() => PriceSheet.Read(template));
    }

    private static ProcessResult? Record(List<string> deleted, string group)
    {
        deleted.Add(group);
        return null;
    }

    private static void Azure(GameHarness game, string budgets, string policies) =>
        game.Processes.Respond = c => (c.Arguments[0], c.Arguments[1]) switch
        {
            ("account", "show") => RecordingProcessRunner.Ok("{\"name\": \"Pay-As-You-Go\"}"),
            ("consumption", "budget") => RecordingProcessRunner.Ok(budgets),
            ("policy", "assignment") => RecordingProcessRunner.Ok(policies),
            _ => null,
        };
}
