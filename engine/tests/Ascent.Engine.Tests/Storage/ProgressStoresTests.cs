using Ascent.Core.Domain;
using Ascent.Core.Progress;
using Ascent.Engine.Tests.TestSupport;
using Ascent.Storage;
using Ascent.Tests.Shared;
using Microsoft.Data.Sqlite;

namespace Ascent.Engine.Tests.Storage;

/// <summary>The progress tables behind the game services.</summary>
public sealed class ProgressStoresTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Quest_progress_is_stored_per_quest()
    {
        using var fixture = CurriculumFixture.Create();
        using var game = new GameHarness(fixture);
        var store = game.QuestProgress;

        store.Save(new QuestProgressRecord("q-1.2", QuestStatus.InProgress, null, null));
        store.Save(new QuestProgressRecord("q-1.1", QuestStatus.InProgress, At, null));
        store.Save(new QuestProgressRecord("q-1.1", QuestStatus.Complete, At, At.AddHours(1)));

        store.Find("q-1.1").ShouldBe(new QuestProgressRecord("q-1.1", QuestStatus.Complete, At, At.AddHours(1)));
        store.Find("q-9.9").ShouldBeNull();
        store.All().Select(r => r.QuestId).ShouldBe(["q-1.1", "q-1.2"]);
    }

    [Fact]
    public void Ranks_season_two_items_stand_ups_and_teach_backs_are_kept_once()
    {
        using var fixture = CurriculumFixture.Create();
        using var game = new GameHarness(fixture);

        var ranks = new RankStore(game.Database);
        ranks.Highest().ShouldBeNull();
        ranks.Record(Rank.AppSecEngineer, At);
        ranks.Record(Rank.SecurityChampion, At);
        ranks.Record(Rank.AppSecEngineer, At.AddDays(1));
        ranks.Highest().ShouldBe(Rank.AppSecEngineer);

        var season2 = new Season2Store(game.Database);
        season2.Add("dd-2", Season2Reason.SkippedDeepDive, At);
        season2.Add("lab-d5-01", Season2Reason.SkippedLab, At);
        season2.Add("dd-2", Season2Reason.SkippedDeepDive, At.AddDays(1));
        season2.All().ShouldBe([("dd-2", Season2Reason.SkippedDeepDive), ("lab-d5-01", Season2Reason.SkippedLab)]);

        var standUps = new StandUpStore(game.Database);
        standUps.Record(new DateOnly(2026, 10, 5), At, 3);
        standUps.Record(new DateOnly(2026, 10, 5), At.AddHours(2), 4);
        standUps.Record(new DateOnly(2026, 10, 7), At.AddDays(2), 0);
        standUps.Between(new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 6)).ShouldBe([new DateOnly(2026, 10, 5)]);
        Scalar(game, "SELECT items_reviewed FROM standups WHERE local_date = '2026-10-05';").ShouldBe(7L);

        var teachBacks = game.TeachBackStore;
        teachBacks.Exists("q-1.1").ShouldBeFalse();
        teachBacks.Save("q-1.1", "/journal/q-1.1.md", 12, At);
        teachBacks.Save("q-1.1", "/journal/q-1.1.md", 20, At.AddHours(1));
        teachBacks.Exists("q-1.1").ShouldBeTrue();
        Scalar(game, "SELECT word_count FROM teachbacks WHERE ref_id = 'q-1.1';").ShouldBe(20L);
    }

    [Fact]
    public void Answers_replace_earlier_answers_to_the_same_item()
    {
        using var fixture = CurriculumFixture.Create();
        using var game = new GameHarness(fixture);
        var attempts = game.Attempts;
        var id = attempts.StartBoss("D1", BossKind.First, At, At.AddMinutes(45), ["qb-1", "qb-2"]);

        attempts.SaveAnswer(AttemptKind.BossFight, id, "qb-1", "A", At);
        attempts.SaveAnswer(AttemptKind.BossFight, id, "qb-1", "C", At.AddMinutes(1));
        attempts.SaveAnswer(AttemptKind.Diagnostic, id, "qb-1", "B", At);

        attempts.Answers(AttemptKind.BossFight, id).ShouldBe([new AttemptAnswer("qb-1", "C", At.AddMinutes(1))]);
        attempts.Answers(AttemptKind.Diagnostic, id).Single().Answer.ShouldBe("B");
        attempts.BossAttempts("D2").ShouldBeEmpty();
        attempts.ActiveBoss()!.ItemIds.ShouldBe(["qb-1", "qb-2"]);
    }

    private static object? Scalar(GameHarness game, string sql)
    {
        using SqliteCommand command = game.Database.Command();
#pragma warning disable CA2100 // Test-only constant SQL.
        command.CommandText = sql;
#pragma warning restore CA2100
        return command.ExecuteScalar();
    }
}
