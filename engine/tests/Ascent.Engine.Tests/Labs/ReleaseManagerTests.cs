using Ascent.Core.Domain;
using Ascent.Core.Errors;
using Ascent.Core.Platform;
using Ascent.Core.Progress;
using Ascent.Engine.Tests.Curriculum;
using Ascent.Engine.Tests.TestSupport;
using Ascent.Labs;
using Ascent.Storage;
using Ascent.Tests.Shared;

namespace Ascent.Engine.Tests.Labs;

/// <summary>Releases (ADR 0006, REL-01..03, S2-01).</summary>
public sealed class ReleaseManagerTests
{
    [Theory]
    [InlineData(0, "ORI")]
    [InlineData(1, "D1")]
    [InlineData(8, "D8")]
    [InlineData(9, "CAP")]
    public void The_ladder_maps_releases_to_domains(int release, string domain)
    {
        ReleaseLadder.DomainOf(release).ShouldBe(domain);
        ReleaseLadder.NumberOf(domain).ShouldBe(release);
    }

    [Fact]
    public void The_current_release_is_the_highest_unlocked()
    {
        ReleaseLadder.Current([]).ShouldBe(0);
        ReleaseLadder.Current([new ReleaseRecord("D2", DateTimeOffset.UnixEpoch, false), new ReleaseRecord("D1", DateTimeOffset.UnixEpoch, false)]).ShouldBe(2);
        ReleaseLadder.NumberOf("Q9").ShouldBeNull();
        Should.Throw<ArgumentOutOfRangeException>(() => ReleaseLadder.DomainOf(10));
    }

    [Fact]
    [Trait("Rule", "REL-01")]
    public async Task The_next_release_unlocks_once_the_domains_quests_are_done_and_its_boss_fight_attempted()
    {
        using var fixture = Domains();
        using var game = new GameHarness(fixture);

        var orientation = game.Releases.Check();
        orientation.ShouldBe(orientation with { Current = 0, Next = 1, FromDomain = "ORI", NextDomain = "D1", BossRequired = false, Published = true });
        orientation.OpenQuests.ShouldBe(["q-ori-1"]);
        orientation.Ready.ShouldBeFalse();
        Should.Throw<UsageException>(() => ReleaseManager.EnsureCanUnlock(orientation, skip: false)).Message.ShouldBe("Release 1 unlocks once you finish q-ori-1.");

        Complete(game, "q-ori-1");
        game.Releases.Check().Ready.ShouldBeTrue();
        (await game.Releases.UnlockAsync(skip: false, archive: false, TestContext.Current.CancellationToken)).Files.ShouldBe(1);
        game.Releases.Current.ShouldBe(1);

        var d1 = game.Releases.Check();
        d1.ShouldBe(d1 with { FromDomain = "D1", NextDomain = "D2", BossRequired = true, BossAttempted = false, Published = false });
        Complete(game, "q-1.1");
        Should.Throw<AscentException>(() => ReleaseManager.EnsureCanUnlock(game.Releases.Check(), skip: true)).Message.ShouldBe("Release 2 hasn't been published yet.");
        Should.Throw<UsageException>(() => ReleaseManager.EnsureCanUnlock(game.Releases.Check() with { Published = true }, skip: false))
            .Message.ShouldBe("Release 2 unlocks once you attempt the D1 Boss Fight.");
        game.RecordBossScore("D1", 40);
        game.Releases.Check().Ready.ShouldBeTrue();
    }

    [Fact]
    [Trait("Rule", "REL-02")]
    [Trait("Rule", "S2-01")]
    public async Task Skipping_ahead_sends_unfinished_labs_and_deep_dives_to_season_two()
    {
        using var fixture = Domains();
        using var game = new GameHarness(fixture);

        var outcome = await game.Releases.UnlockAsync(skip: true, archive: false, TestContext.Current.CancellationToken);

        outcome.Skipped.ShouldBeTrue();
        outcome.Season2.ShouldBe(["lab-ori-01", "dd-1"]);
        new Season2Store(game.Database).All().ShouldBe([("dd-1", Season2Reason.SkippedDeepDive), ("lab-ori-01", Season2Reason.SkippedLab)]);
        new ReleaseStore(game.Database).All().Single().ShouldBe(new ReleaseRecord("D1", game.Clock.GetUtcNow(), true));
        File.ReadAllText(Path.Join(fixture.Root, "my-work", "throughline", "release.txt")).ShouldBe("Release 1");
    }

    [Fact]
    [Trait("Rule", "REL-03")]
    public async Task Moving_on_archives_your_work_to_a_portfolio_branch_and_commits_the_new_baseline()
    {
        using var fixture = Domains();
        using var game = new GameHarness(fixture);
        Complete(game, "q-ori-1");
        game.Processes.Installed.Add(ExternalTool.Git);
        game.Processes.Respond = c => c.Arguments[0] == "status" ? RecordingProcessRunner.Ok("?? throughline/fix.cs\n") : null;

        await game.Releases.UnlockAsync(skip: false, archive: true, TestContext.Current.CancellationToken);

        game.Processes.Lines(ExternalTool.Git).ShouldBe(
        [
            "status --porcelain",
            "add --all",
            "commit --quiet --message My ORI work",
            "branch --force portfolio/orientation",
            "add --all",
            "commit --quiet --allow-empty --message Release 1 baseline",
        ]);
        ReleaseManager.ArchiveSteps(game.Releases.Check() with { FromDomain = "D3" }, hasChanges: false).Single().ToString().ShouldBe("git branch --force portfolio/d3");
    }

    [Fact]
    public void The_capstone_is_the_last_release()
    {
        using var fixture = Domains();
        using var game = new GameHarness(fixture);
        new ReleaseStore(game.Database).Unlock("CAP", game.Clock.GetUtcNow(), skipped: false);

        Should.Throw<UsageException>(() => game.Releases.Check()).Message.ShouldBe("You're on the last Release.");
    }

    // Orientation (with a Lab and a Deep Dive) and D1, with Release 1 sealed.
    private static CurriculumFixture Domains()
    {
        var fixture = CurriculumFixture.Create()
            .Quest("q-ori-1", "ORI-1", "ORI", labs: ["lab-ori-01"])
            .Quest("q-1.1", "1.1", "D1")
            .Lab("lab-ori-01", "ORI-1")
            .Write("curriculum/orientation/q-ori-1.md", QuestWithDeepDive())
            .Write("release-1/release.txt", "Release 1");
        fixture.SealFolder("release-1", "release", SealTier.Release, Path.Join(fixture.Root, "release-1"));
        return fixture;
    }

    private static string QuestWithDeepDive() =>
        "---\nid: q-ori-1\nobjectiveId: \"ORI-1\"\noutlineVersion: \"2023-09-15\"\nexamDomain: ORI\nrelease: 0\ntitle: Quest q-ori-1\nestimatedMinutes: 30\nstatus: draft\n"
        + "citations:\n  - { id: c1, title: Example standard, publisher: NIST, kind: standard, role: primary }\naiLens: null\n"
        + "activities:\n  labs: [lab-ori-01]\n  drills: []\n  deliverables: []\ndeepDive: dd-1\n---\n# Quest q-ori-1\n\nLesson text.\n";

    // Completes a Quest's lesson and its Labs.
    private static void Complete(GameHarness game, string questId)
    {
        new Core.Curriculum.TeachBackService(game.TeachBackStore, game.Fixture.Paths.DefaultJournal, game.Clock).Save(questId, "Done.", forLab: false);
        foreach (var lab in game.Catalog.Quests.Single(q => q.Id == questId).Labs)
        {
            QuestFlowTests.SetLabStage(game, lab, LabStage.Explained);
        }

        game.Quests.CompleteLesson(questId).Status.ShouldBe(QuestStatus.Complete);
    }
}
