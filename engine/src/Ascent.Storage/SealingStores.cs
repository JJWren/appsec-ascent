using System.Globalization;
using System.Text.Json;
using Ascent.Core.Domain;
using Ascent.Core.Progress;
using Ascent.Core.Time;
using Microsoft.Data.Sqlite;

namespace Ascent.Storage;

/// <summary>Lab Flags in <c>lab_state</c> (FLAG-01..03): salts and hashes only.</summary>
public sealed class LabFlagStore(ProgressDatabase database) : ILabFlags
{
    /// <inheritdoc />
    public LabFlagRecord? Find(string labId)
    {
        using var command = database.Command();
        command.CommandText = "SELECT flag_salt, flag_hash, wrong_flag_times FROM lab_state WHERE lab_id = $lab;";
        using var reader = command.With("$lab", labId).ExecuteReader();
        if (!reader.Read() || reader.IsDBNull(0) || reader.IsDBNull(1))
        {
            return null;
        }

        var times = JsonSerializer.Deserialize<List<string>>(reader.GetString(2)) ?? [];
        return new LabFlagRecord(
            (byte[])reader.GetValue(0),
            (byte[])reader.GetValue(1),
            times.Select(Utc.Parse).ToList());
    }

    /// <inheritdoc />
    public void SetFlag(string labId, byte[] salt, byte[] hash)
    {
        using var command = database.Command();
        command.CommandText =
            "INSERT INTO lab_state (lab_id, state, flag_salt, flag_hash, wrong_flag_times) VALUES ($lab, 'NotStarted', $salt, $hash, '[]') " +
            "ON CONFLICT (lab_id) DO UPDATE SET flag_salt = excluded.flag_salt, flag_hash = excluded.flag_hash, wrong_flag_times = '[]';";
        command.With("$lab", labId).With("$salt", salt).With("$hash", hash).ExecuteNonQuery();
    }

    /// <inheritdoc />
    public void SetWrongAttempts(string labId, IReadOnlyList<DateTimeOffset> attempts)
    {
        ArgumentNullException.ThrowIfNull(attempts);
        using var command = database.Command();
        command.CommandText = "UPDATE lab_state SET wrong_flag_times = $times WHERE lab_id = $lab;";
        command.With("$lab", labId).With("$times", JsonSerializer.Serialize(attempts.Select(Utc.ToText).ToList())).ExecuteNonQuery();
    }
}

/// <summary>The <c>key_releases</c> table (SEAL-05).</summary>
public sealed class KeyReleaseStore(ProgressDatabase database) : IKeyReleaseLog
{
    /// <inheritdoc />
    public void Record(string itemId, SealTier tier, ReleaseReason reason, DateTimeOffset releasedAt)
    {
        using var command = database.Command();
        command.CommandText =
            "INSERT INTO key_releases (item_id, tier, released_utc, reason) VALUES ($item, $tier, $when, $reason) " +
            "ON CONFLICT (item_id, tier) DO NOTHING;";
        command.With("$item", itemId)
            .With("$tier", SealTiers.Name(tier))
            .With("$when", Utc.ToText(releasedAt))
            .With("$reason", reason.ToString())
            .ExecuteNonQuery();
    }

    /// <inheritdoc />
    public bool IsReleased(string itemId, SealTier tier)
    {
        using var command = database.Command();
        command.CommandText = "SELECT count(*) FROM key_releases WHERE item_id = $item AND tier = $tier;";
        return Count(command.With("$item", itemId).With("$tier", SealTiers.Name(tier))) > 0;
    }

    internal static long Count(SqliteCommand command) => Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
}

/// <summary>Answers the key-release policy's and the Quest flow's questions from progress (SEAL-03, E2-01).</summary>
public sealed class ProgressFactsStore(ProgressDatabase database) : IReleaseFacts, IActivityFacts
{
    /// <inheritdoc />
    public bool RulesAccepted
    {
        get
        {
            using var command = database.Command();
            command.CommandText = "SELECT count(*) FROM profile WHERE key = 'rulesAcceptedUtc';";
            return KeyReleaseStore.Count(command) > 0;
        }
    }

    /// <inheritdoc />
    public LabStage LabStage(string labId)
    {
        using var command = database.Command();
        command.CommandText = "SELECT state FROM lab_state WHERE lab_id = $lab;";
        return command.With("$lab", labId).ExecuteScalar() is string state && Enum.TryParse<LabStage>(state, out var stage)
            ? stage
            : Core.Domain.LabStage.NotStarted;
    }

    /// <inheritdoc />
    public bool WorkSubmitted(string workId)
    {
        using var command = database.Command();
        command.CommandText = "SELECT count(*) FROM xp_events WHERE kind IN ('Drill', 'DeepDive') AND ref_id = $id;";
        return DeliverableSubmitted(workId) || KeyReleaseStore.Count(command.With("$id", workId)) > 0;
    }

    /// <inheritdoc />
    public bool DrillDone(string drillId)
    {
        using var command = database.Command();
        command.CommandText = "SELECT count(*) FROM xp_events WHERE kind = 'Drill' AND ref_id = $id;";
        return KeyReleaseStore.Count(command.With("$id", drillId)) > 0;
    }

    /// <inheritdoc />
    public bool DeliverableSubmitted(string deliverableId)
    {
        using var command = database.Command();
        command.CommandText = "SELECT count(*) FROM deliverables WHERE dlv_id = $id AND submitted_utc IS NOT NULL;";
        return KeyReleaseStore.Count(command.With("$id", deliverableId)) > 0;
    }

    /// <inheritdoc />
    public bool SimulationIncludes(long attemptId, string itemId)
    {
        using var command = database.Command();
        command.CommandText =
            "SELECT count(*) FROM simulation_attempts, json_each(simulation_attempts.item_ids) " +
            "WHERE simulation_attempts.id = $attempt AND simulation_attempts.status = 'InProgress' AND json_each.value = $item;";
        return KeyReleaseStore.Count(command.With("$attempt", attemptId).With("$item", itemId)) > 0;
    }

    /// <inheritdoc />
    public bool ReleaseUnlocked(string examDomain)
    {
        using var command = database.Command();
        command.CommandText = "SELECT count(*) FROM releases WHERE exam_domain = $domain;";
        return KeyReleaseStore.Count(command.With("$domain", examDomain)) > 0;
    }
}
