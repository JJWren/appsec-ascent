using Ascent.Core;
using Ascent.Core.Domain;
using Ascent.Core.Platform;
using Ascent.Engine.Tests.TestSupport;
using Ascent.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;

namespace Ascent.Engine.Tests.Storage;

public sealed class SealingStoresTests : IDisposable
{
    private readonly TempDirectory temp = new();
    private readonly ProgressDatabase database;

    public SealingStoresTests() =>
        database = ProgressDatabase.Open(new EnginePaths(temp.Path), new FakeTimeProvider(), OwnerOnlyFiles.ForCurrentOs());

    [Fact]
    [Trait("Rule", "FLAG-02")]
    public void Lab_flags_store_salts_hashes_and_wrong_attempts()
    {
        var flags = new LabFlagStore(database.Connection);
        flags.Find("lab-d5-01").ShouldBeNull();

        flags.SetFlag("lab-d5-01", [1, 2], [3, 4]);
        var attempt = new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);
        flags.SetWrongAttempts("lab-d5-01", [attempt]);
        var record = flags.Find("lab-d5-01")!;

        record.Salt.ShouldBe(new byte[] { 1, 2 });
        record.Hash.ShouldBe(new byte[] { 3, 4 });
        record.WrongAttempts.ShouldBe([attempt]);

        flags.SetFlag("lab-d5-01", [5], [6]);
        flags.Find("lab-d5-01")!.WrongAttempts.ShouldBeEmpty();
        Scalar("SELECT state FROM lab_state WHERE lab_id = 'lab-d5-01';").ShouldBe("NotStarted");
    }

    [Fact]
    [Trait("Rule", "SEAL-05")]
    public void Key_releases_are_recorded_once()
    {
        var log = new KeyReleaseStore(database.Connection);
        log.IsReleased("qb-1.1-001", SealTier.Practice).ShouldBeFalse();

        log.Record("qb-1.1-001", SealTier.Practice, ReleaseReason.Served, DateTimeOffset.UnixEpoch);
        log.Record("qb-1.1-001", SealTier.Practice, ReleaseReason.Served, DateTimeOffset.UnixEpoch.AddDays(1));

        log.IsReleased("qb-1.1-001", SealTier.Practice).ShouldBeTrue();
        log.IsReleased("qb-1.1-001", SealTier.Simulation).ShouldBeFalse();
        Scalar("SELECT count(*) FROM key_releases;").ShouldBe(1L);
        Scalar("SELECT released_utc FROM key_releases;").ShouldBe("1970-01-01T00:00:00.000Z");
    }

    [Fact]
    [Trait("Rule", "SEAL-03")]
    public void Release_facts_come_from_progress()
    {
        var facts = new ReleaseFactsStore(database.Connection);
        facts.RulesAccepted.ShouldBeFalse();
        facts.LabStage("lab-d5-01").ShouldBe(LabStage.NotStarted);
        facts.WorkSubmitted("dlv-d3-01").ShouldBeFalse();
        facts.SimulationIncludes(1, "qb-1.1-900").ShouldBeFalse();
        facts.ReleaseUnlocked("D1").ShouldBeFalse();

        Execute("INSERT INTO profile (key, value) VALUES ('rulesAcceptedUtc', '2026-10-03T12:00:00.000Z');");
        Execute("INSERT INTO lab_state (lab_id, state) VALUES ('lab-d5-01', 'FlagCaptured');");
        Execute("INSERT INTO deliverables (dlv_id, work_path, submitted_utc) VALUES ('dlv-d3-01', 'x', '2026-10-03T12:00:00.000Z');");
        Execute("INSERT INTO xp_events (occurred_utc, kind, ref_id, points, bonus) VALUES ('2026-10-03T12:00:00.000Z', 'Drill', 'drl-1.2-01', 15, 0);");
        Execute("INSERT INTO simulation_attempts (form, started_utc, deadline_utc, status, item_ids, seen_form) VALUES ('A', 'x', 'y', 'InProgress', '[\"qb-1.1-900\"]', 0);");
        Execute("INSERT INTO releases (exam_domain, unlocked_utc, skipped) VALUES ('D1', 'x', 0);");

        facts.RulesAccepted.ShouldBeTrue();
        facts.LabStage("lab-d5-01").ShouldBe(LabStage.FlagCaptured);
        facts.WorkSubmitted("dlv-d3-01").ShouldBeTrue();
        facts.WorkSubmitted("drl-1.2-01").ShouldBeTrue();
        facts.SimulationIncludes(1, "qb-1.1-900").ShouldBeTrue();
        facts.SimulationIncludes(1, "qb-1.1-901").ShouldBeFalse();
        facts.ReleaseUnlocked("D1").ShouldBeTrue();

        Execute("UPDATE simulation_attempts SET status = 'Finished';");
        facts.SimulationIncludes(1, "qb-1.1-900").ShouldBeFalse();
    }

    public void Dispose()
    {
        database.Dispose();
        temp.Dispose();
    }

    private object? Scalar(string sql)
    {
        using var command = database.Connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private void Execute(string sql)
    {
        using var command = database.Connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
