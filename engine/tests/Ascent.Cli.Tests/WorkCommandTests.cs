using System.Runtime.CompilerServices;
using System.Text;
using Ascent.Cli.Tests.TestSupport;
using Ascent.Core.Domain;
using Ascent.Core.Errors;
using Ascent.Core.Profile;
using Ascent.Deliverables;
using Ascent.Tests.Shared;

namespace Ascent.Cli.Tests;

/// <summary>deliver and review (DLE-01..04, PRV-02).</summary>
public sealed class WorkCommandTests
{
    private const string Id = "dlv-d3-01";

    [Fact]
    [Trait("Rule", "DLE-01")]
    [Trait("Rule", "DLE-02")]
    [Trait("Rule", "DLE-03")]
    public async Task Deliver_scaffolds_then_lists_problems_then_scores_submits_and_shows_the_reference()
    {
        using var fixture = Fixture();
        using var engine = TestEngine.For(fixture);
        var work = Path.Join(fixture.Root, "my-work", "deliverables", Id + ".yaml");
        (await engine.RunAsync("teachback", "q-3.3", "--text", "Classification drives controls.")).ExitCode.ShouldBe(ExitCodes.Ok);

        (await engine.RunAsync("deliver", Id)).Output.ShouldContain("INFO: Created " + work + " from the template.");
        var invalid = await engine.RunAsync("deliver", Id);
        invalid.ExitCode.ShouldBe(ExitCodes.CheckFailed);
        invalid.Output.ShouldContain("  main[1].summary is required.");

        await File.WriteAllTextAsync(work, "main:\n  - summary: Every store is classified.\n", TestContext.Current.CancellationToken);
        var declined = await engine.RunWithInputAsync(["3", "3", "n"], "deliver", Id);
        declined.Output.ShouldContain("Your work is complete. Score it against the rubric (pass mark 70%):");
        declined.Output.ShouldContain("  3) 4: Complete");
        declined.Output.ShouldContain("Not submitted.");

        var submitted = await engine.RunWithInputAsync(["3", "2", "y"], "deliver", Id);
        submitted.Output.ShouldContain("PASS: Submitted at 80%, at or above the pass mark. +50 XP.");
        submitted.Output.ShouldContain("Reference answer");
        submitted.Output.ShouldContain("Classify every data store.");
        submitted.Output.ShouldContain("PASS: Quest complete: q-3.3");
    }

    [Fact]
    public async Task Drills_are_delivered_with_a_markdown_answer()
    {
        using var fixture = Fixture();
        using var engine = TestEngine.For(fixture);
        var answer = Path.Join(fixture.Root, "my-work", "drills", "drl-d3-01.md");

        (await engine.RunAsync("deliver", "drl-d3-01")).Output.ShouldContain("INFO: Created " + answer + " with the drill's prompt.");
        (await engine.RunAsync("deliver", "drl-d3-01")).Output.ShouldContain("FAIL: The drill has no answer yet.");
        await File.AppendAllTextAsync(answer, "Encrypt it.\n", TestContext.Current.CancellationToken);

        var done = await engine.RunAsync("deliver", "drl-d3-01");
        done.Output.ShouldContain("PASS: Drill submitted: +15 XP.");
        done.Output.ShouldContain("Answer key");
        done.Output.ShouldContain("Encrypt data at rest.");
        (await engine.RunAsync("deliver", "nope")).ExitCode.ShouldBe(ExitCodes.Usage);
    }

    [Fact]
    [Trait("Rule", "DLE-04")]
    [Trait("Rule", "PRV-02")]
    public async Task Review_needs_an_endpoint_asks_once_before_sending_work_elsewhere_and_pays_only_the_injection_bonus()
    {
        using var fixture = Fixture();
        using var engine = TestEngine.For(fixture);
        var reviewer = new ScriptedReviewer();
        engine.ReviewerClient = reviewer;

        (await engine.RunAsync("review", Id)).Output.ShouldContain("The AI reviewer isn't set up.");
        await engine.RunAsync("config", "ai.endpoint", "https://models.example.com/v1");
        await engine.RunAsync("config", "ai.model", "big-model");
        (await engine.RunAsync("review", Id)).Output.ShouldContain("There's no work to review yet.");
        await engine.RunAsync("deliver", Id);

        var declined = await engine.RunWithInputAsync(["n"], "review", Id);
        declined.Output.ShouldContain("WARN: Your Deliverable's text will be sent to models.example.com, which isn't on this machine.");
        declined.Output.ShouldContain("Nothing was sent.");
        reviewer.Requests.ShouldBeEmpty();

        var reviewed = await engine.RunWithInputAsync(["y"], "review", Id);
        reviewed.Output.ShouldContain("Review of " + Id + " by big-model");
        reviewed.Output.ShouldContain("Line one.\nLine two.".ReplaceLineEndings());
        reviewed.Output.ShouldContain("The review is advice only");
        reviewed.Output.ShouldNotContain("Prompt Breaker");
        reviewer.Requests.Single().User.ShouldContain("main:");

        reviewer.Leak = true;
        var leaked = await engine.RunAsync("review", Id);
        leaked.Output.ShouldNotContain("will be sent to");
        leaked.Output.ShouldContain("PASS: Prompt Breaker! The reviewer revealed its secret: +30 bonus XP.");
        (await engine.RunAsync("review", Id)).Output.ShouldContain("the bonus is paid once per Deliverable");
        LearnerCommandTests.Profile(engine).Values[ProfileKeys.AiRemoteConfirmed].ShouldBe("https://models.example.com");
    }

    internal static CurriculumFixture Fixture()
    {
        var fixture = CurriculumFixture.Create()
            .Quest("q-3.3", "3.3", "D3", deliverables: [Id])
            .Deliverable(Id, "3.3")
            .Rubric("rub-" + Id, 70, ("completeness", 60), ("accuracy", 40))
            .Drill("drl-d3-01", "3.3");
        fixture.Seal(Id + ".reference", "reference", SealTier.Submitted, Encoding.UTF8.GetBytes("Classify every data store.\nThen review it yearly."));
        fixture.Seal("drl-d3-01.answer", "drill-answer", SealTier.Submitted, Encoding.UTF8.GetBytes("Encrypt data at rest."));
        return fixture;
    }

    private sealed class ScriptedReviewer : IReviewerClient
    {
        public List<ReviewRequest> Requests { get; } = [];

        public bool Leak { get; set; }

        public async IAsyncEnumerable<string> StreamAsync(ReviewRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Requests.Add(request);
            await Task.Yield();
            yield return "Line one.\nLine ";
            yield return "two.";
            if (Leak)
            {
                yield return " " + request.System[request.System.IndexOf("PB-", StringComparison.Ordinal)..][..19];
            }
        }
    }
}
