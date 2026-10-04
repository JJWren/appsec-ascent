using Ascent.Content.Loading;
using Ascent.Core.Curriculum;
using Ascent.Core.Progress;
using Ascent.Engine.Tests.TestSupport;
using Ascent.Storage;
using Ascent.Tests.Shared;

namespace Ascent.Engine.Tests.Curriculum;

/// <summary>Progress carries over to a new outline through mapping files (US-E10-06).</summary>
public sealed class OutlineRefreshTests
{
    private static readonly OutlineMapping First = new("2023-09-15", "2027-01-01",
    [
        new MappingEntry("1.1", ["1.1"], "same"),
        new MappingEntry("1.2", ["1.3", "1.4"], "split"),
        new MappingEntry("2.6", [], "removed"),
        new MappingEntry(null, ["8.9"], "new"),
    ]);

    private static readonly OutlineMapping Second = new("2027-01-01", "2029-06-01", [new MappingEntry("1.3", ["1.5"], "merged")]);

    [Fact]
    public void Mappings_chain_from_one_version_to_another()
    {
        OutlineMapping.Chain([Second, First], "2023-09-15", "2029-06-01").ShouldBe([First, Second]);
        OutlineMapping.Chain([First], "2023-09-15", "2023-09-15").ShouldBeEmpty();
        OutlineMapping.Chain([First], "2023-09-15", "2029-06-01").ShouldBeNull();

        var loop = new OutlineMapping("2027-01-01", "2023-09-15", []);
        OutlineMapping.Chain([First, loop], "2023-09-15", "2029-06-01").ShouldBeNull();
    }

    [Theory]
    [InlineData("1.1", "1.1")]
    [InlineData("1.2", "1.3")]
    [InlineData("2.6", null)]
    [InlineData("4.4", "4.4")]
    public void Each_objective_maps_to_its_first_target(string from, string? to) => First.Map(from).ShouldBe(to);

    [Fact]
    public void Mapping_files_are_loaded_from_the_curriculum()
    {
        using var fixture = CurriculumFixture.Create().Mapping("2023-09-15", "2027-01-01", ("1.2", ["1.3", "1.4"], "split"), (null, ["8.9"], "new"));

        var mapping = OutlineMapping.Load(ContentLoader.Load(fixture.Root)).ShouldHaveSingleItem();

        mapping.From.ShouldBe("2023-09-15");
        mapping.To.ShouldBe("2027-01-01");
        mapping.Entries.Select(e => (e.From, string.Join(",", e.To), e.Kind)).ShouldBe([("1.2", "1.3,1.4", "split"), (null, "8.9", "new")]);
    }

    [Fact]
    public void Review_cards_follow_the_chain_and_the_new_version_is_recorded()
    {
        using var fixture = CurriculumFixture.Create();
        using var game = new GameHarness(fixture);
        var cards = game.Cards;
        var versions = new OutlineVersionStore(game.Database);
        var refresh = new OutlineRefresh(versions, cards, game.Database);
        var now = game.Clock.GetUtcNow();
        foreach (var (item, objective) in new[] { ("qb-a", "1.1"), ("qb-b", "1.2"), ("qb-c", "2.6") })
        {
            cards.Save(new ReviewCardRecord(item, objective, "D" + objective[0], "{}", now, now, null, 0, 0, false));
        }

        // The first check only records the version.
        refresh.Apply("2023-09-15", []).ShouldBeNull();
        versions.Recorded.ShouldBe("2023-09-15");
        refresh.Apply("2023-09-15", []).ShouldBeNull();
        refresh.Apply(null, []).ShouldBeNull();

        // Without a mapping nothing moves, and nothing is recorded, so a mapping added later still applies.
        refresh.Apply("2029-06-01", [First]).ShouldBe(new OutlineChange("2023-09-15", "2029-06-01", 0, MappingMissing: true));
        versions.Recorded.ShouldBe("2023-09-15");

        refresh.Apply("2029-06-01", [First, Second]).ShouldBe(new OutlineChange("2023-09-15", "2029-06-01", 2, MappingMissing: false));
        versions.Recorded.ShouldBe("2029-06-01");
        cards.Find("qb-a")!.ObjectiveId.ShouldBe("1.1");
        cards.Find("qb-b")!.ObjectiveId.ShouldBe("1.5");
        cards.Find("qb-c")!.ObjectiveId.ShouldBeEmpty();
        cards.Find("qb-c")!.ExamDomain.ShouldBe("D2");
    }
}
