using System.Globalization;
using Ascent.Core;
using Ascent.Core.Platform;
using Ascent.Engine.Tests.TestSupport;
using Ascent.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;

namespace Ascent.Engine.Tests.Storage;

public sealed class ProgressDatabaseTests
{
    private static readonly string[] ExpectedTables =
    [
        "attempt_answers", "boss_attempts", "cloud_deployments", "content_bugs", "deliverables", "diagnostic",
        "key_releases", "lab_state", "meta", "profile", "quest_progress", "rank_history", "releases", "review_cards",
        "reviews", "season2", "simulation_attempts", "standups", "teachbacks", "xp_events",
    ];

    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero));
    private readonly IOwnerOnlyFiles files = OwnerOnlyFiles.ForCurrentOs();

    [Fact]
    public void A_new_database_is_created_owner_only_with_every_table()
    {
        using var temp = new TempDirectory();
        var paths = new EnginePaths(temp.Path);

        using var database = ProgressDatabase.Open(paths, clock, files);

        database.Migration.ShouldBe(new MigrationReport(0, Migrator.LatestVersion, null));
        Tables(database.Connection).ShouldBe(ExpectedTables);
        Pragma(database.Connection, "journal_mode").ShouldBe("wal");
        Pragma(database.Connection, "foreign_keys").ShouldBe("1");
        Pragma(database.Connection, "synchronous").ShouldBe("2");
        if (!OperatingSystem.IsWindows())
        {
            File.GetUnixFileMode(paths.Database).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.GetUnixFileMode(paths.StateDirectory).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public void Reopening_an_up_to_date_database_changes_nothing()
    {
        using var temp = new TempDirectory();
        var paths = new EnginePaths(temp.Path);
        ProgressDatabase.Open(paths, clock, files).Dispose();

        using var again = ProgressDatabase.Open(paths, clock, files);

        again.Migration.ShouldBe(new MigrationReport(Migrator.LatestVersion, Migrator.LatestVersion, null));
        new DatabaseBackups(paths.Backups, clock, files).List().ShouldBeEmpty();
    }

    [Fact]
    public void A_pending_migration_backs_up_first_and_then_applies()
    {
        using var temp = new TempDirectory();
        var paths = new EnginePaths(temp.Path);
        ProgressDatabase.Open(paths, clock, files).Dispose();
        var extra = new Migration(2, "add_notes", "CREATE TABLE notes (id INTEGER PRIMARY KEY, body TEXT NOT NULL) STRICT;");

        using var upgraded = ProgressDatabase.Open(paths, clock, files, [.. Migrator.All, extra]);

        upgraded.Migration.FromVersion.ShouldBe(1);
        upgraded.Migration.ToVersion.ShouldBe(2);
        upgraded.Migration.BackupPath.ShouldNotBeNull();
        Path.GetFileName(upgraded.Migration.BackupPath).ShouldBe("progress-20261003T120000Z-v1.db");
        Tables(upgraded.Connection).ShouldContain("notes");
        Migrator.ReadVersion(upgraded.Connection).ShouldBe(2);

        using var backup = new SqliteConnection(ProgressDatabase.ConnectionString(upgraded.Migration.BackupPath!, SqliteOpenMode.ReadOnly));
        backup.Open();
        Migrator.ReadVersion(backup).ShouldBe(1);
        Tables(backup).ShouldNotContain("notes");
    }

    [Fact]
    public void A_failing_migration_rolls_back_and_leaves_the_version()
    {
        using var temp = new TempDirectory();
        var paths = new EnginePaths(temp.Path);
        ProgressDatabase.Open(paths, clock, files).Dispose();
        var broken = new Migration(2, "broken", "CREATE TABLE half (id INTEGER PRIMARY KEY) STRICT; THIS IS NOT SQL;");

        Should.Throw<SqliteException>(() => ProgressDatabase.Open(paths, clock, files, [.. Migrator.All, broken]));

        using var database = ProgressDatabase.Open(paths, clock, files);
        Migrator.ReadVersion(database.Connection).ShouldBe(1);
        Tables(database.Connection).ShouldNotContain("half");
    }

    [Fact]
    public void A_database_from_a_newer_engine_is_refused()
    {
        using var temp = new TempDirectory();
        var paths = new EnginePaths(temp.Path);
        using (var database = ProgressDatabase.Open(paths, clock, files))
        {
            MetaStore.Set(database.Connection, MetaStore.SchemaVersion, "99");
        }

        var error = Should.Throw<NewerDatabaseException>(() => ProgressDatabase.Open(paths, clock, files));
        error.Message.ShouldContain("v99");
    }

    [Fact]
    public void A_file_that_is_not_a_database_is_refused_without_being_touched()
    {
        using var temp = new TempDirectory();
        var paths = new EnginePaths(temp.Path);
        Directory.CreateDirectory(paths.StateDirectory);
        var garbage = string.Concat(Enumerable.Repeat("this is definitely not a sqlite database file. ", 200));
        File.WriteAllText(paths.Database, garbage);

        var error = Should.Throw<CorruptDatabaseException>(() => ProgressDatabase.Open(paths, clock, files));

        error.Message.ShouldContain("damaged");
        error.NextStep!.ShouldContain("progress import");
        File.ReadAllText(paths.Database).ShouldBe(garbage);
        StorageErrors.IsCorruption(error.InnerException!).ShouldBeTrue();
        StorageErrors.IsCorruption(new InvalidOperationException()).ShouldBeFalse();
    }

    [Fact]
    public void Units_of_work_commit_or_roll_back()
    {
        using var temp = new TempDirectory();
        using var database = ProgressDatabase.Open(new EnginePaths(temp.Path), clock, files);

        using (var work = database.Begin())
        {
            InsertProfile(work, "kept", "yes");
            work.Commit();
        }

        using (var work = database.Begin())
        {
            InsertProfile(work, "dropped", "no");
        }

        ProfileValue(database.Connection, "kept").ShouldBe("yes");
        ProfileValue(database.Connection, "dropped").ShouldBeNull();
    }

    [Fact]
    public void Only_one_boss_fight_can_be_unfinished()
    {
        using var temp = new TempDirectory();
        using var database = ProgressDatabase.Open(new EnginePaths(temp.Path), clock, files);
        InsertBossAttempt(database.Connection, finished: null);

        Should.Throw<SqliteException>(() => InsertBossAttempt(database.Connection, finished: null));
        Should.NotThrow(() => InsertBossAttempt(database.Connection, finished: "2026-10-03T12:45:00.000Z"));
    }

    [Fact]
    public void Profile_peek_reads_plain_mode_without_creating_anything()
    {
        using var temp = new TempDirectory();
        var paths = new EnginePaths(temp.Path);

        ProfilePeek.PlainMode(paths).ShouldBeNull();
        Directory.Exists(paths.StateDirectory).ShouldBeFalse();

        using (var database = ProgressDatabase.Open(paths, clock, files))
        {
            ProfilePeek.PlainMode(paths).ShouldBeNull();
            using var work = database.Begin();
            InsertProfile(work, ProfilePeek.PlainModeKey, "true");
            work.Commit();
        }

        ProfilePeek.PlainMode(paths).ShouldBe(true);
    }

    [Fact]
    public void Profile_peek_ignores_unreadable_files()
    {
        using var temp = new TempDirectory();
        var paths = new EnginePaths(temp.Path);
        Directory.CreateDirectory(paths.StateDirectory);
        File.WriteAllText(paths.Database, string.Concat(Enumerable.Repeat("garbage ", 500)));

        ProfilePeek.PlainMode(paths).ShouldBeNull();
    }

    [Fact]
    public void Backups_keep_the_newest_ten()
    {
        using var temp = new TempDirectory();
        var paths = new EnginePaths(temp.Path);
        using var database = ProgressDatabase.Open(paths, clock, files);
        var backups = new DatabaseBackups(paths.Backups, clock, files);

        for (var i = 0; i < 12; i++)
        {
            backups.Create(database.Connection, 1);
            clock.Advance(TimeSpan.FromSeconds(i % 2)); // some share a timestamp, which gets a numeric suffix
        }

        var kept = backups.List();
        kept.Count.ShouldBe(DatabaseBackups.Keep);
        kept.ShouldBe(kept.OrderDescending(StringComparer.Ordinal).ToList());
    }

    [Fact]
    public void The_embedded_migrations_are_numbered_without_gaps()
    {
        Migrator.All.Select(m => m.Version).ShouldBe(Enumerable.Range(1, Migrator.All.Count));
        Migrator.All[0].Name.ShouldBe("initial");
        Migrator.LatestVersion.ShouldBe(Migrator.All.Count);
    }

    private static List<string> Tables(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name;";
        using var reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static string Pragma(SqliteConnection connection, string name)
    {
        using var command = connection.CreateCommand();
        command.CommandText = name switch
        {
            "journal_mode" => "PRAGMA journal_mode;",
            "foreign_keys" => "PRAGMA foreign_keys;",
            "synchronous" => "PRAGMA synchronous;",
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };
        return Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture)!;
    }

    private static void InsertProfile(UnitOfWork work, string key, string value)
    {
        using var command = work.Command();
        command.CommandText = "INSERT INTO profile (key, value) VALUES ($key, $value);";
        command.With("$key", key).With("$value", value).ExecuteNonQuery();
    }

    private static string? ProfileValue(SqliteConnection connection, string key)
    {
        using var command = Statements.Command(connection);
        command.CommandText = "SELECT value FROM profile WHERE key = $key;";
        return command.With("$key", key).ExecuteScalar() as string;
    }

    private static void InsertBossAttempt(SqliteConnection connection, string? finished)
    {
        using var command = Statements.Command(connection);
        command.CommandText =
            "INSERT INTO boss_attempts (exam_domain, kind, started_utc, deadline_utc, finished_utc, item_ids) " +
            "VALUES ('D1', 'First', '2026-10-03T12:00:00.000Z', '2026-10-03T12:45:00.000Z', $finished, '[]');";
        command.With("$finished", finished).ExecuteNonQuery();
    }
}
