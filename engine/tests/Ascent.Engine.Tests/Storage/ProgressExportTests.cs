using System.Text.Json.Nodes;
using Ascent.Core.Domain;
using Ascent.Core.Errors;
using Ascent.Core.Profile;
using Ascent.Engine.Tests.TestSupport;
using Ascent.Storage;
using Ascent.Tests.Shared;

namespace Ascent.Engine.Tests.Storage;

/// <summary>Progress export and import (BAK-01, P31).</summary>
public sealed class ProgressExportTests
{
    [Fact]
    [Trait("Rule", "BAK-01")]
    public void An_export_round_trips_every_table_without_flag_secrets()
    {
        using var fixture = CurriculumFixture.Create();
        using var game = new GameHarness(fixture);
        Seed(game);
        var exporter = Exporter(game);
        var file = exporter.DefaultPath();
        file.ShouldStartWith(fixture.Paths.Exports);

        var counts = exporter.Export(file);

        counts["profile"].ShouldBe(2);
        counts["lab_state"].ShouldBe(1);
        counts["xp_events"].ShouldBe(1);
        var text = File.ReadAllText(file);
        text.ShouldNotContain("flag_salt");
        text.ShouldNotContain("flag_hash");
        text.ShouldNotContain(ProfileKeys.AiRemoteConfirmed);
        var document = JsonNode.Parse(text)!.AsObject();
        document["format"]!.GetValue<string>().ShouldBe(ProgressFile.Format);
        document["schemaVersion"]!.GetValue<int>().ShouldBe(Migrator.LatestVersion);
        document["tables"]!["lab_state"]![0]!["state"]!.GetValue<string>().ShouldBe("FlagCaptured");
        if (!OperatingSystem.IsWindows())
        {
            File.GetUnixFileMode(file).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        // Import into a fresh repository.
        using var other = CurriculumFixture.Create();
        using var restored = new GameHarness(other);
        var read = ProgressFile.Read(file, other.Root);
        var summary = Exporter(restored).Import(read);

        summary.Rows.ShouldBe(counts, ignoreOrder: true);
        summary.ExportedUtc.ShouldBe(game.Clock.GetUtcNow());
        File.Exists(summary.BackupPath).ShouldBeTrue();
        restored.Ledger.Total.ShouldBe(10);
        new ProfileStore(restored.Database).All()[ProfileKeys.WeeklyGoal].ShouldBe("3");
        new ProfileStore(restored.Database).All().ShouldNotContainKey(ProfileKeys.AiRemoteConfirmed);
        new LabFlagStore(restored.Database).Find("lab-d5-01").ShouldBeNull();
        restored.Facts.LabStage("lab-d5-01").ShouldBe(LabStage.FlagCaptured);
    }

    [Fact]
    [Trait("Rule", "BAK-01")]
    public void An_import_replaces_existing_progress_and_takes_a_backup_first()
    {
        using var fixture = CurriculumFixture.Create();
        using var game = new GameHarness(fixture);
        game.Ledger.Award(XpKind.Lesson, "q-1.1");
        var file = Path.Join(fixture.Root, "empty.json");
        Exporter(game).Export(file);
        game.Ledger.Award(XpKind.Lesson, "q-1.2");
        game.Ledger.Total.ShouldBe(20);

        var summary = Exporter(game).Import(ProgressFile.Read(file, fixture.Root));

        game.Ledger.Total.ShouldBe(10);
        new DatabaseBackups(fixture.Paths.Backups, game.Clock, game.Files).List().ShouldContain(summary.BackupPath);
    }

    [Fact]
    public void An_existing_export_file_is_never_overwritten()
    {
        using var fixture = CurriculumFixture.Create();
        using var game = new GameHarness(fixture);
        var file = fixture.Paths.Exports + "/progress.json";
        Exporter(game).Export(file);

        Should.Throw<UsageException>(() => Exporter(game).Export(file)).Message.ShouldContain("already exists");
    }

    [Theory]
    [InlineData("[]", "it isn't a JSON object")]
    [InlineData("{\"format\":\"other\"}", "its format isn't appsec-ascent/progress")]
    [InlineData("{\"format\":\"appsec-ascent/progress\",\"exportedUtc\":\"2026-10-05T09:00:00.000Z\",\"tables\":{}}", "schemaVersion is missing")]
    [InlineData("{\"format\":\"appsec-ascent/progress\",\"schemaVersion\":1,\"tables\":{}}", "exportedUtc is missing")]
    [InlineData("{\"format\":\"appsec-ascent/progress\",\"schemaVersion\":1,\"exportedUtc\":\"2026-10-05T09:00:00.000Z\"}", "tables is missing")]
    [InlineData("{\"format\":\"appsec-ascent/progress\",\"schemaVersion\":1,\"exportedUtc\":\"2026-10-05T09:00:00.000Z\",\"tables\":{\"secrets\":[]}}", "unknown table 'secrets'")]
    [InlineData("{\"format\":\"appsec-ascent/progress\",\"schemaVersion\":1,\"exportedUtc\":\"2026-10-05T09:00:00.000Z\",\"tables\":{\"profile\":{}}}", "profile isn't a list")]
    [InlineData("{\"format\":\"appsec-ascent/progress\",\"schemaVersion\":1,\"exportedUtc\":\"2026-10-05T09:00:00.000Z\",\"tables\":{\"profile\":[1]}}", "profile[0] isn't an object")]
    [InlineData("{\"format\":\"appsec-ascent/progress\",\"schemaVersion\":1,\"exportedUtc\":\"2026-10-05T09:00:00.000Z\",\"tables\":{\"lab_state\":[{\"lab_id\":\"x\",\"state\":\"Started\",\"flag_hash\":\"AA==\"}]}}", "lab_state[0] has an unknown column 'flag_hash'")]
    [InlineData("{\"format\":\"appsec-ascent/progress\",\"schemaVersion\":1,\"exportedUtc\":\"2026-10-05T09:00:00.000Z\",\"tables\":{\"xp_events\":[{\"points\":\"ten\"}]}}", "xp_events[0].points must be a whole number")]
    [InlineData("{\"format\":\"appsec-ascent/progress\",\"schemaVersion\":1,\"exportedUtc\":\"2026-10-05T09:00:00.000Z\",\"tables\":{\"profile\":[{\"key\":1}]}}", "profile[0].key must be text")]
    [InlineData("{\"format\":\"appsec-ascent/progress\",\"schemaVersion\":1,\"exportedUtc\":\"2026-10-05T09:00:00.000Z\",\"tables\":{\"profile\":[{\"key\":\"a\\u001bb\"}]}}", "profile[0].key is too long or has control characters")]
    [InlineData("{\"format\":\"appsec-ascent/progress\",\"schemaVersion\":1,\"exportedUtc\":\"2026-10-05T09:00:00.000Z\",\"tables\":{\"deliverables\":[{\"dlv_id\":\"d\",\"work_path\":\"../../etc/passwd\"}]}}", "deliverables[0].work_path must stay inside the repository")]
    public void Invalid_files_are_refused_before_anything_changes(string json, string problem)
    {
        using var fixture = CurriculumFixture.Create();
        var file = fixture.Write("bad.json", json).Root + "/bad.json";

        var error = Should.Throw<AscentException>(() => ProgressFile.Read(file, fixture.Root));

        error.Message.ShouldBe("That file can't be imported: " + problem + ".");
        error.ExitCode.ShouldBe(ExitCodes.CheckFailed);
    }

    [Fact]
    public void Unreadable_missing_and_newer_files_are_explained()
    {
        using var fixture = CurriculumFixture.Create();

        Should.Throw<UsageException>(() => ProgressFile.Read(Path.Join(fixture.Root, "missing.json"), fixture.Root)).Message.ShouldContain("doesn't exist");
        fixture.Write("broken.json", "{ not json");
        Should.Throw<AscentException>(() => ProgressFile.Read(Path.Join(fixture.Root, "broken.json"), fixture.Root)).Message.ShouldContain("isn't valid JSON");
        fixture.Write("newer.json", "{\"format\":\"appsec-ascent/progress\",\"schemaVersion\":99,\"exportedUtc\":\"2026-10-05T09:00:00.000Z\",\"tables\":{}}");
        Should.Throw<NewerDatabaseException>(() => ProgressFile.Read(Path.Join(fixture.Root, "newer.json"), fixture.Root)).NextStep!.ShouldContain("Update the Engine");
        ProgressFile.Columns("lab_state").Select(c => c.Name).ShouldNotContain("flag_salt");
        Should.Throw<ArgumentException>(() => ProgressFile.Columns("meta"));
    }

    [Fact]
    public void A_row_that_breaks_the_schema_rolls_the_whole_import_back()
    {
        using var fixture = CurriculumFixture.Create();
        using var game = new GameHarness(fixture);
        game.Ledger.Award(XpKind.Lesson, "q-1.1");
        var document = JsonNode.Parse("{\"format\":\"appsec-ascent/progress\",\"schemaVersion\":1,\"exportedUtc\":\"2026-10-05T09:00:00.000Z\",\"tables\":{"
            + "\"profile\":[{\"key\":\"weeklyGoal\",\"value\":\"4\"}],"
            + "\"quest_progress\":[{\"quest_id\":\"q-1.1\",\"status\":\"Bogus\"}]}}")!.AsObject();

        var error = Should.Throw<AscentException>(() => Exporter(game).Import(document));

        error.Message.ShouldContain("nothing was changed");
        game.Ledger.Total.ShouldBe(10);
        new ProfileStore(game.Database).All().ShouldNotContainKey("weeklyGoal");
    }

    [Fact]
    public void A_damaged_database_is_moved_aside_for_a_fresh_one()
    {
        using var fixture = CurriculumFixture.Create();
        var paths = fixture.Paths;
        Directory.CreateDirectory(paths.StateDirectory);
        File.WriteAllText(paths.Database, "this is not a database, just text that is long enough to look like a header");
        File.WriteAllText(paths.Database + "-wal", "wal");

        var moved = ProgressDatabase.MoveAside(paths, TimeProvider.System, Ascent.Core.Platform.OwnerOnlyFiles.ForCurrentOs());

        File.Exists(paths.Database).ShouldBeFalse();
        File.Exists(moved).ShouldBeTrue();
        File.Exists(moved + "-wal").ShouldBeTrue();
        Path.GetFileName(moved).ShouldStartWith("damaged-progress-");
        new DatabaseBackups(paths.Backups, TimeProvider.System, Ascent.Core.Platform.OwnerOnlyFiles.ForCurrentOs()).List().ShouldBeEmpty();
    }

    private static ProgressExporter Exporter(GameHarness game) => new(game.Database, game.Fixture.Paths, game.Clock, game.Files);

    private static void Seed(GameHarness game)
    {
        var profile = new ProfileStore(game.Database);
        profile.Write(ProfileKeys.WeeklyGoal, "3");
        profile.Write(ProfileKeys.AiEndpoint, "https://models.example.com/v1");
        profile.Write(ProfileKeys.AiRemoteConfirmed, "https://models.example.com");
        game.Ledger.Award(XpKind.Lesson, "q-1.1");
        new LabFlagStore(game.Database).SetFlag("lab-d5-01", [1, 2, 3], [4, 5, 6]);
        Curriculum.QuestFlowTests.SetLabStage(game, "lab-d5-01", LabStage.FlagCaptured);
    }
}
