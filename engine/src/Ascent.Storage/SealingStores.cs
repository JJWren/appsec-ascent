using System.Globalization;
using System.Text.Json;
using Ascent.Core.Domain;
using Ascent.Core.Progress;
using Ascent.Core.Time;
using Microsoft.Data.Sqlite;

namespace Ascent.Storage;

/// <summary>Lab Flags in <c>lab_state</c> (FLAG-01..03): salts and hashes only.</summary>
public sealed class LabFlagStore(SqliteConnection connection) : ILabFlags
{
    /// <inheritdoc />
    public LabFlagRecord? Find(string labId)
    {
        using var command = Statements.Command(connection);
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
        using var command = Statements.Command(connection);
        command.CommandText =
            "INSERT INTO lab_state (lab_id, state, flag_salt, flag_hash, wrong_flag_times) VALUES ($lab, 'NotStarted', $salt, $hash, '[]') " +
            "ON CONFLICT (lab_id) DO UPDATE SET flag_salt = excluded.flag_salt, flag_hash = excluded.flag_hash, wrong_flag_times = '[]';";
        command.With("$lab", labId).With("$salt", salt).With("$hash", hash).ExecuteNonQuery();
    }

    /// <inheritdoc />
    public void SetWrongAttempts(string labId, IReadOnlyList<DateTimeOffset> attempts)
    {
        ArgumentNullException.ThrowIfNull(attempts);
        using var command = Statements.Command(connection);
        command.CommandText = "UPDATE lab_state SET wrong_flag_times = $times WHERE lab_id = $lab;";
        command.With("$lab", labId).With("$times", JsonSerializer.Serialize(attempts.Select(Utc.ToText).ToList())).ExecuteNonQuery();
    }
}

/// <summary>The <c>key_releases</c> table (SEAL-05).</summary>
public sealed class KeyReleaseStore(SqliteConnection connection) : IKeyReleaseLog
{
    /// <inheritdoc />
    public void Record(string itemId, SealTier tier, ReleaseReason reason, DateTimeOffset releasedAt)
    {
        using var command = Statements.Command(connection);
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
        using var command = Statements.Command(connection);
        command.CommandText = "SELECT count(*) FROM key_releases WHERE item_id = $item AND tier = $tier;";
        return Convert.ToInt64(command.With("$item", itemId).With("$tier", SealTiers.Name(tier)).ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
    }
}

/// <summary>Answers the key-release policy's questions from the progress store (SEAL-03).</summary>
public sealed class ReleaseFactsStore(SqliteConnection connection) : IReleaseFacts
{
    /// <summary>The profile key recording when the rules of engagement were accepted.</summary>
    public const string RulesAcceptedKey = "rulesAcceptedUtc";

    /// <inheritdoc />
    public bool RulesAccepted
    {
        get
        {
            using var command = Statements.Command(connection);
            command.CommandText = "SELECT count(*) FROM profile WHERE key = $key;";
            return Count(command.With("$key", RulesAcceptedKey)) > 0;
        }
    }

    /// <inheritdoc />
    public LabStage LabStage(string labId)
    {
        using var command = Statements.Command(connection);
        command.CommandText = "SELECT state FROM lab_state WHERE lab_id = $lab;";
        return command.With("$lab", labId).ExecuteScalar() is string state && Enum.TryParse<LabStage>(state, out var stage)
            ? stage
            : Core.Domain.LabStage.NotStarted;
    }

    /// <inheritdoc />
    public bool WorkSubmitted(string workId)
    {
        using var command = Statements.Command(connection);
        command.CommandText =
            "SELECT (SELECT count(*) FROM deliverables WHERE dlv_id = $id AND submitted_utc IS NOT NULL) " +
            "+ (SELECT count(*) FROM xp_events WHERE kind IN ('Drill', 'DeepDive') AND ref_id = $id);";
        return Count(command.With("$id", workId)) > 0;
    }

    /// <inheritdoc />
    public bool SimulationIncludes(long attemptId, string itemId)
    {
        using var command = Statements.Command(connection);
        command.CommandText =
            "SELECT count(*) FROM simulation_attempts, json_each(simulation_attempts.item_ids) " +
            "WHERE simulation_attempts.id = $attempt AND simulation_attempts.status = 'InProgress' AND json_each.value = $item;";
        return Count(command.With("$attempt", attemptId).With("$item", itemId)) > 0;
    }

    /// <inheritdoc />
    public bool ReleaseUnlocked(string examDomain)
    {
        using var command = Statements.Command(connection);
        command.CommandText = "SELECT count(*) FROM releases WHERE exam_domain = $domain;";
        return Count(command.With("$domain", examDomain)) > 0;
    }

    private static long Count(SqliteCommand command) => Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
}
