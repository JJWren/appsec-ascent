using System.Text;
using Ascent.Core.Domain;
using Ascent.Core.Errors;
using Ascent.Deliverables;
using Ascent.Engine.Tests.TestSupport;
using Ascent.Tests.Shared;

namespace Ascent.Engine.Tests.Deliverables;

/// <summary>Deliverables and drills: scaffold, validate, self-score, submit (DLE-01..03).</summary>
public sealed class DeliverableTests
{
    private const string Id = "dlv-d3-01";

    private const string Template = """
        id: dlv-d3-01
        objectiveIds: ["3.3"]
        title: Data classification inventory
        sections:
          - key: inventory
            title: Data inventory
            required: true
            minItems: 2
            fields:
              - { key: asset, type: text, required: true }
              - { key: classification, type: enum, required: true, options: [public, internal, confidential] }
              - { key: records, type: number, required: false }
              - { key: owners, type: list, required: false }
          - key: review
            title: Review
            required: true
            fields:
              - { key: reviewed, type: date, required: true }
              - { key: notes, type: markdown, required: false }
              - { key: lab, type: reference, required: false }
          - key: extras
            title: Extras
            required: false
            fields:
              - { key: note, type: text, required: false }
        rubricId: rub-d3-01
        referenceRef: dlv-d3-01.reference
        portfolioEligible: true
        """;

    private const string ValidWork = """
        inventory:
          - asset: Appointments table
            classification: confidential
            records: 1200
            owners: [scheduling, support]
          - asset: Public FAQ
            classification: public
        review:
          - reviewed: "2026-10-05"
            notes: Checked with the DPO.
            lab: lab-d5-01
        """;

    [Fact]
    [Trait("Rule", "DLE-01")]
    public void The_scaffold_lists_every_section_and_field_and_starts_out_incomplete()
    {
        using var fixture = Fixture();
        using var game = new GameHarness(fixture);
        var template = game.DeliverableCatalog.Template(Id);

        var scaffold = WorkFile.Scaffold(template);

        scaffold.ShouldStartWith("# Data classification inventory (dlv-d3-01)\n# Objectives: 3.3\n");
        scaffold.ShouldContain("# Data inventory (required, at least 2 entries)\ninventory:\n  - asset: \"\"  # text (required)\n");
        scaffold.ShouldContain("    classification: \"\"  # one of: public | internal | confidential (required)\n");
        scaffold.ShouldContain("    records: null  # number (optional)\n");
        scaffold.ShouldContain("    owners: []  # a list, such as [first, second] (optional)\n");
        scaffold.ShouldContain("  - reviewed: \"\"  # a date like 2026-10-05 (required)\n");
        scaffold.ShouldContain("# Extras (optional)\nextras:\n");
        WorkFile.Validate(template, scaffold).ShouldBe(
        [
            "inventory[1].asset is required.",
            "inventory[1].classification is required.",
            "inventory[2].asset is required.",
            "inventory[2].classification is required.",
            "review[1].reviewed is required.",
        ]);
    }

    [Theory]
    [Trait("Rule", "DLE-02")]
    [InlineData("inventory: oops\nreview: []\n", "Section 'inventory' must be a list of entries, each starting with '- '.")]
    [InlineData("inventory:\n  - asset: a\n    classification: public\nreview:\n  - reviewed: \"2026-10-05\"\n", "Section 'inventory' needs at least 2 entries; it has 1.")]
    [InlineData("review:\n  - reviewed: \"2026-10-05\"\n", "Section 'inventory' (Data inventory) is missing.")]
    [InlineData("inventory:\n  - asset: a\n    classification: secret\n  - asset: b\n    classification: public\nreview:\n  - reviewed: \"2026-10-05\"\n", "inventory[1].classification must be one of: public, internal, confidential.")]
    [InlineData("inventory:\n  - asset: a\n    classification: public\n    records: lots\n  - asset: b\n    classification: public\nreview:\n  - reviewed: \"2026-10-05\"\n", "inventory[1].records must be a number.")]
    [InlineData("inventory:\n  - asset: a\n    classification: public\n    owners: [x, \"\"]\n  - asset: b\n    classification: public\nreview:\n  - reviewed: \"2026-10-05\"\n", "inventory[1].owners must be a list of text items.")]
    [InlineData("inventory:\n  - asset: a\n    classification: public\n  - asset: b\n    classification: public\nreview:\n  - reviewed: 05/10/2026\n", "review[1].reviewed must be a date like 2026-10-05.")]
    [InlineData("inventory:\n  - asset: [a]\n    classification: public\n  - asset: b\n    classification: public\nreview:\n  - reviewed: \"2026-10-05\"\n", "inventory[1].asset must be text.")]
    [InlineData("inventory:\n  - asset: a\n    classification: public\n    colour: red\n  - asset: b\n    classification: public\nreview:\n  - reviewed: \"2026-10-05\"\n", "inventory[1] has an unknown field 'colour'.")]
    [InlineData("inventory:\n  - plain\n  - asset: b\n    classification: public\nreview:\n  - reviewed: \"2026-10-05\"\n", "inventory[1] must be an entry with fields.")]
    [InlineData("notes: hi\ninventory:\n  - asset: a\n    classification: public\n  - asset: b\n    classification: public\nreview:\n  - reviewed: \"2026-10-05\"\n", "Unknown section 'notes'.")]
    [InlineData("- just a list\n", "The file must list the template's sections by key.")]
    [InlineData("inventory: [\n", "The file isn't valid YAML")]
    [InlineData("a: 1\na: 2\n", "The file isn't valid YAML")]
    public void Validation_lists_exactly_what_is_wrong(string yaml, string problem)
    {
        using var fixture = Fixture();
        using var game = new GameHarness(fixture);

        WorkFile.Validate(game.DeliverableCatalog.Template(Id), yaml).ShouldContain(p => p.StartsWith(problem, StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Rule", "DLE-02")]
    public void Valid_work_has_no_problems()
    {
        using var fixture = Fixture();
        using var game = new GameHarness(fixture);

        WorkFile.Validate(game.DeliverableCatalog.Template(Id), ValidWork).ShouldBeEmpty();
    }

    [Theory]
    [Trait("Rule", "DLE-03")]
    [InlineData(4, 4, 100)]
    [InlineData(2, 4, 70)]
    [InlineData(2, 2, 50)]
    [InlineData(0, 4, 40)]
    public void The_self_score_weights_each_criterion(int completeness, int accuracy, int expected)
    {
        var rubric = new Rubric("r", 70,
        [
            new RubricCriterion("completeness", "Complete", 60, [new(0, "No"), new(2, "Some"), new(4, "All")]),
            new RubricCriterion("accuracy", "Accurate", 40, [new(0, "No"), new(2, "Some"), new(4, "All")]),
        ]);

        DeliverableService.Score(rubric, new Dictionary<string, int> { ["completeness"] = completeness, ["accuracy"] = accuracy }).ShouldBe(expected);
        Should.Throw<ArgumentException>(() => DeliverableService.Score(rubric, new Dictionary<string, int> { ["completeness"] = 3, ["accuracy"] = 0 }));
        Should.Throw<ArgumentException>(() => DeliverableService.Score(rubric, new Dictionary<string, int> { ["completeness"] = 4 }));
    }

    [Fact]
    [Trait("Rule", "DLE-01")]
    [Trait("Rule", "DLE-02")]
    [Trait("Rule", "DLE-03")]
    public void Submitting_valid_work_pays_xp_once_and_releases_the_reference_answer()
    {
        using var fixture = Fixture();
        using var game = new GameHarness(fixture);
        var service = game.Deliverables;

        var created = service.Check(Id);
        created.State.ShouldBe(WorkState.Scaffolded);
        created.Path.ShouldBe(Path.Join(fixture.Root, "my-work", "deliverables", Id + ".yaml"));
        service.Check(Id).State.ShouldBe(WorkState.Invalid);
        Should.Throw<AscentException>(() => service.Submit(Id, Full())).NextStep!.ShouldContain("ascent deliver " + Id);
        game.Ledger.Total.ShouldBe(0);

        File.WriteAllText(created.Path, ValidWork);
        service.Check(Id).State.ShouldBe(WorkState.Ready);
        var submission = service.Submit(Id, Full());

        submission.ShouldBe(new Submission(100, true, 50, "The reference inventory."));
        var record = game.DeliverableStore.Find(Id)!;
        record.WorkPath.ShouldBe("my-work/deliverables/" + Id + ".yaml");
        record.SelfScorePercent.ShouldBe(100);
        record.SubmittedUtc.ShouldNotBeNull();
        record.ReferenceReleasedUtc.ShouldNotBeNull();
        game.Facts.DeliverableSubmitted(Id).ShouldBeTrue();

        // A resubmission rescores but pays nothing more.
        service.Submit(Id, new Dictionary<string, int> { ["completeness"] = 2, ["accuracy"] = 2 }).ShouldBe(new Submission(50, false, 0, "The reference inventory."));
        game.Ledger.Total.ShouldBe(50);
        Should.Throw<UsageException>(() => service.Check("dlv-nope"));
    }

    [Fact]
    [Trait("Rule", "DLE-03")]
    public void Work_below_the_pass_mark_still_earns_the_submission_xp()
    {
        using var fixture = Fixture();
        using var game = new GameHarness(fixture);
        var service = game.Deliverables;
        service.Check(Id);
        File.WriteAllText(service.WorkPath(Id), ValidWork);

        service.Submit(Id, new Dictionary<string, int> { ["completeness"] = 2, ["accuracy"] = 0 }).ShouldBe(new Submission(30, false, 40, "The reference inventory."));
        game.Ledger.Events.Select(e => e.Kind).ShouldBe([XpKind.Deliverable]);
    }

    [Fact]
    public void Drills_take_an_answer_then_pay_once_and_show_the_answer_key()
    {
        using var fixture = Fixture();
        using var game = new GameHarness(fixture);
        var service = game.Deliverables;
        game.DeliverableCatalog.IsDrill("drl-d1-01").ShouldBeTrue();

        service.OpenDrill("drl-d1-01", out var path).ShouldBeTrue();
        service.OpenDrill("drl-d1-01", out _).ShouldBeFalse();
        File.ReadAllText(path).ShouldContain("> Classify each control.");
        Should.Throw<AscentException>(() => service.SubmitDrill("drl-d1-01")).Message.ShouldBe("The drill has no answer yet.");

        File.AppendAllText(path, "Encryption protects confidentiality.\n");
        service.SubmitDrill("drl-d1-01").ShouldBe(new Submission(null, true, 15, "Encryption, checksums, backups."));
        service.SubmitDrill("drl-d1-01").XpAwarded.ShouldBe(0);
        game.Facts.DrillDone("drl-d1-01").ShouldBeTrue();
        Should.Throw<UsageException>(() => service.OpenDrill("drl-nope", out _));
    }

    [Fact]
    public void Templates_without_their_rubric_are_reported()
    {
        using var fixture = CurriculumFixture.Create().Write("deliverables/templates/" + Id + ".yaml", Template);
        using var game = new GameHarness(fixture);
        var catalog = game.DeliverableCatalog;

        Should.Throw<AscentException>(() => catalog.RubricFor(catalog.Template(Id))).Message.ShouldBe("The rubric 'rub-d3-01' is missing.");
        catalog.Templates.Single().PortfolioEligible.ShouldBeTrue();
    }

    internal static CurriculumFixture Fixture()
    {
        var fixture = CurriculumFixture.Create()
            .Write("deliverables/templates/" + Id + ".yaml", Template)
            .Rubric("rub-d3-01", 70, ("completeness", 60), ("accuracy", 40))
            .Drill("drl-d1-01", "1.1");
        fixture.Seal(Id + ".reference", "reference", SealTier.Submitted, Encoding.UTF8.GetBytes("The reference inventory."));
        fixture.Seal("drl-d1-01.answer", "drill-answer", SealTier.Submitted, Encoding.UTF8.GetBytes("Encryption, checksums, backups."));
        return fixture;
    }

    internal static Dictionary<string, int> Full() => new() { ["completeness"] = 4, ["accuracy"] = 4 };
}
