using System.Globalization;
using Ascent.Core.Domain;
using Ascent.Core.Progress;
using Ascent.Core.Time;
using Microsoft.Data.Sqlite;

namespace Ascent.Storage;

/// <summary>The <c>lab_state</c> table's progress columns (LABE-01).</summary>
public sealed class LabStateStore(ProgressDatabase database) : ILabStateStore
{
    private const string Columns = "lab_id, state, started_utc, flag_captured_utc, fixed_utc, explained_utc, verify_attempts";

    /// <inheritdoc />
    public LabStateRecord? Find(string labId)
    {
        using var command = database.Command();
        command.CommandText = "SELECT " + Columns + " FROM lab_state WHERE lab_id = $lab;";
        using var reader = command.With("$lab", labId).ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    /// <inheritdoc />
    public IReadOnlyList<LabStateRecord> All()
    {
        using var command = database.Command();
        command.CommandText = "SELECT " + Columns + " FROM lab_state ORDER BY lab_id;";
        using var reader = command.ExecuteReader();
        var labs = new List<LabStateRecord>();
        while (reader.Read())
        {
            labs.Add(Read(reader));
        }

        return labs;
    }

    /// <inheritdoc />
    public void MoveTo(string labId, LabStage stage, DateTimeOffset at)
    {
        using var command = database.Command();
        switch (stage)
        {
            case LabStage.Started:
                command.CommandText =
                    "INSERT INTO lab_state (lab_id, state, started_utc) VALUES ($lab, 'Started', $when) " +
                    "ON CONFLICT (lab_id) DO UPDATE SET state = 'Started', started_utc = coalesce(lab_state.started_utc, excluded.started_utc);";
                break;
            case LabStage.FlagCaptured:
                command.CommandText = "UPDATE lab_state SET state = 'FlagCaptured', flag_captured_utc = $when WHERE lab_id = $lab;";
                break;
            case LabStage.Fixed:
                command.CommandText = "UPDATE lab_state SET state = 'Fixed', fixed_utc = $when WHERE lab_id = $lab;";
                break;
            case LabStage.Explained:
                command.CommandText = "UPDATE lab_state SET state = 'Explained', explained_utc = $when WHERE lab_id = $lab;";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(stage), stage, "A Lab can't move back to NotStarted.");
        }

        command.With("$lab", labId).With("$when", Utc.ToText(at)).ExecuteNonQuery();
    }

    /// <inheritdoc />
    public void CountVerify(string labId)
    {
        using var command = database.Command();
        command.CommandText = "UPDATE lab_state SET verify_attempts = verify_attempts + 1 WHERE lab_id = $lab;";
        command.With("$lab", labId).ExecuteNonQuery();
    }

    private static LabStateRecord Read(SqliteDataReader reader) => new(
        reader.GetString(0),
        Enum.Parse<LabStage>(reader.GetString(1)),
        Time(reader, 2),
        Time(reader, 3),
        Time(reader, 4),
        Time(reader, 5),
        reader.GetInt32(6));

    internal static DateTimeOffset? Time(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : Utc.Parse(reader.GetString(ordinal));
}

/// <summary>The <c>cloud_deployments</c> table (CLD-03..05).</summary>
public sealed class CloudDeploymentStore(ProgressDatabase database) : ICloudDeploymentStore
{
    /// <inheritdoc />
    public long Add(string labId, string resourceGroup, DateTimeOffset deployedUtc, DateTimeOffset expiresUtc)
    {
        using var command = database.Command();
        command.CommandText =
            "INSERT INTO cloud_deployments (lab_id, resource_group, deployed_utc, expires_utc) VALUES ($lab, $group, $deployed, $expires) RETURNING id;";
        return Convert.ToInt64(
            command.With("$lab", labId).With("$group", resourceGroup).With("$deployed", Utc.ToText(deployedUtc)).With("$expires", Utc.ToText(expiresUtc)).ExecuteScalar(),
            CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public IReadOnlyList<CloudDeploymentRecord> Open()
    {
        using var command = database.Command();
        command.CommandText =
            "SELECT id, lab_id, resource_group, deployed_utc, expires_utc, torn_down_utc, outcome FROM cloud_deployments WHERE torn_down_utc IS NULL ORDER BY id;";
        using var reader = command.ExecuteReader();
        var deployments = new List<CloudDeploymentRecord>();
        while (reader.Read())
        {
            deployments.Add(new CloudDeploymentRecord(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                Utc.Parse(reader.GetString(3)),
                Utc.Parse(reader.GetString(4)),
                LabStateStore.Time(reader, 5),
                Enum.Parse<CloudOutcome>(reader.GetString(6))));
        }

        return deployments;
    }

    /// <inheritdoc />
    public void SetOutcome(long id, CloudOutcome outcome)
    {
        using var command = database.Command();
        command.CommandText = "UPDATE cloud_deployments SET outcome = $outcome WHERE id = $id;";
        command.With("$id", id).With("$outcome", outcome.ToString()).ExecuteNonQuery();
    }

    /// <inheritdoc />
    public void Close(long id, DateTimeOffset tornDownUtc)
    {
        using var command = database.Command();
        command.CommandText = "UPDATE cloud_deployments SET torn_down_utc = $when WHERE id = $id;";
        command.With("$id", id).With("$when", Utc.ToText(tornDownUtc)).ExecuteNonQuery();
    }
}

/// <summary>The <c>releases</c> table (REL-01, REL-02).</summary>
public sealed class ReleaseStore(ProgressDatabase database) : IReleaseStore
{
    /// <inheritdoc />
    public IReadOnlyList<ReleaseRecord> All()
    {
        using var command = database.Command();
        command.CommandText = "SELECT exam_domain, unlocked_utc, skipped FROM releases ORDER BY unlocked_utc, exam_domain;";
        using var reader = command.ExecuteReader();
        var releases = new List<ReleaseRecord>();
        while (reader.Read())
        {
            releases.Add(new ReleaseRecord(reader.GetString(0), Utc.Parse(reader.GetString(1)), reader.GetInt32(2) == 1));
        }

        return releases;
    }

    /// <inheritdoc />
    public void Unlock(string examDomain, DateTimeOffset unlockedUtc, bool skipped)
    {
        using var command = database.Command();
        command.CommandText = "INSERT INTO releases (exam_domain, unlocked_utc, skipped) VALUES ($domain, $when, $skipped) ON CONFLICT (exam_domain) DO NOTHING;";
        command.With("$domain", examDomain).With("$when", Utc.ToText(unlockedUtc)).With("$skipped", skipped ? 1 : 0).ExecuteNonQuery();
    }
}

/// <summary>The <c>deliverables</c> table (DLE-01..03).</summary>
public sealed class DeliverableStore(ProgressDatabase database) : IDeliverableStore
{
    private const string Columns = "dlv_id, work_path, validated_utc, self_score_percent, submitted_utc, reference_released_utc, published";

    /// <inheritdoc />
    public DeliverableRecord? Find(string deliverableId)
    {
        using var command = database.Command();
        command.CommandText = "SELECT " + Columns + " FROM deliverables WHERE dlv_id = $id;";
        using var reader = command.With("$id", deliverableId).ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    /// <inheritdoc />
    public IReadOnlyList<DeliverableRecord> All()
    {
        using var command = database.Command();
        command.CommandText = "SELECT " + Columns + " FROM deliverables ORDER BY dlv_id;";
        using var reader = command.ExecuteReader();
        var records = new List<DeliverableRecord>();
        while (reader.Read())
        {
            records.Add(Read(reader));
        }

        return records;
    }

    /// <inheritdoc />
    public void Save(DeliverableRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        using var command = database.Command();
        command.CommandText =
            "INSERT INTO deliverables (" + Columns + ") VALUES ($id, $path, $validated, $score, $submitted, $reference, $published) " +
            "ON CONFLICT (dlv_id) DO UPDATE SET work_path = excluded.work_path, validated_utc = excluded.validated_utc, " +
            "self_score_percent = excluded.self_score_percent, submitted_utc = excluded.submitted_utc, " +
            "reference_released_utc = excluded.reference_released_utc, published = excluded.published;";
        command.With("$id", record.DeliverableId)
            .With("$path", record.WorkPath)
            .With("$validated", record.ValidatedUtc is { } validated ? Utc.ToText(validated) : null)
            .With("$score", record.SelfScorePercent)
            .With("$submitted", record.SubmittedUtc is { } submitted ? Utc.ToText(submitted) : null)
            .With("$reference", record.ReferenceReleasedUtc is { } reference ? Utc.ToText(reference) : null)
            .With("$published", record.Published ? 1 : 0)
            .ExecuteNonQuery();
    }

    private static DeliverableRecord Read(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        LabStateStore.Time(reader, 2),
        reader.IsDBNull(3) ? null : reader.GetInt32(3),
        LabStateStore.Time(reader, 4),
        LabStateStore.Time(reader, 5),
        reader.GetInt32(6) == 1);
}

/// <summary>The <c>content_bugs</c> table (BUG-02).</summary>
public sealed class ContentBugStore(ProgressDatabase database) : IContentBugStore
{
    /// <inheritdoc />
    public bool Add(ContentBugRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        using var command = database.Command();
        command.CommandText = "INSERT INTO content_bugs (issue_number, item_id, confirmed_utc) VALUES ($issue, $item, $when) ON CONFLICT (issue_number) DO NOTHING;";
        return command.With("$issue", record.IssueNumber).With("$item", record.ItemId).With("$when", Utc.ToText(record.ConfirmedUtc)).ExecuteNonQuery() == 1;
    }

    /// <inheritdoc />
    public IReadOnlyList<ContentBugRecord> All()
    {
        using var command = database.Command();
        command.CommandText = "SELECT issue_number, item_id, confirmed_utc FROM content_bugs ORDER BY issue_number;";
        using var reader = command.ExecuteReader();
        var bugs = new List<ContentBugRecord>();
        while (reader.Read())
        {
            bugs.Add(new ContentBugRecord(reader.GetInt32(0), reader.GetString(1), Utc.Parse(reader.GetString(2))));
        }

        return bugs;
    }
}
