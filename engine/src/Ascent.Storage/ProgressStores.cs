using System.Globalization;
using Ascent.Core.Domain;
using Ascent.Core.Progress;
using Ascent.Core.Time;
using Microsoft.Data.Sqlite;

namespace Ascent.Storage;

/// <summary>The <c>profile</c> table.</summary>
public sealed class ProfileStore(ProgressDatabase database) : IProfileStore
{
    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> All()
    {
        using var command = database.Command();
        command.CommandText = "SELECT key, value FROM profile ORDER BY key;";
        using var reader = command.ExecuteReader();
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        while (reader.Read())
        {
            values[reader.GetString(0)] = reader.GetString(1);
        }

        return values;
    }

    /// <inheritdoc />
    public void Write(string key, string value)
    {
        using var command = database.Command();
        command.CommandText = "INSERT INTO profile (key, value) VALUES ($key, $value) ON CONFLICT (key) DO UPDATE SET value = excluded.value;";
        command.With("$key", key).With("$value", value).ExecuteNonQuery();
    }

    /// <inheritdoc />
    public void Remove(string key)
    {
        using var command = database.Command();
        command.CommandText = "DELETE FROM profile WHERE key = $key;";
        command.With("$key", key).ExecuteNonQuery();
    }
}

/// <summary>The <c>xp_events</c> ledger.</summary>
public sealed class XpStore(ProgressDatabase database) : IXpStore
{
    /// <inheritdoc />
    public bool TryAdd(XpEvent xpEvent)
    {
        ArgumentNullException.ThrowIfNull(xpEvent);
        using var command = database.Command();
        command.CommandText =
            "INSERT INTO xp_events (occurred_utc, kind, ref_id, points, bonus) VALUES ($when, $kind, $ref, $points, $bonus) " +
            "ON CONFLICT (kind, ref_id) DO NOTHING;";
        return command.With("$when", Utc.ToText(xpEvent.OccurredUtc))
            .With("$kind", xpEvent.Kind.ToString())
            .With("$ref", xpEvent.RefId)
            .With("$points", xpEvent.Points)
            .With("$bonus", xpEvent.Bonus ? 1 : 0)
            .ExecuteNonQuery() == 1;
    }

    /// <inheritdoc />
    public bool Has(XpKind kind, string refId)
    {
        using var command = database.Command();
        command.CommandText = "SELECT count(*) FROM xp_events WHERE kind = $kind AND ref_id = $ref;";
        return KeyReleaseStore.Count(command.With("$kind", kind.ToString()).With("$ref", refId)) > 0;
    }

    /// <inheritdoc />
    public IReadOnlyList<XpEvent> All()
    {
        using var command = database.Command();
        command.CommandText = "SELECT occurred_utc, kind, ref_id, points, bonus FROM xp_events ORDER BY id;";
        using var reader = command.ExecuteReader();
        var events = new List<XpEvent>();
        while (reader.Read())
        {
            events.Add(new XpEvent(Utc.Parse(reader.GetString(0)), Enum.Parse<XpKind>(reader.GetString(1)), reader.GetString(2), reader.GetInt32(3), reader.GetInt32(4) == 1));
        }

        return events;
    }
}

/// <summary>The <c>rank_history</c> table.</summary>
public sealed class RankStore(ProgressDatabase database) : IRankStore
{
    /// <inheritdoc />
    public Rank? Highest()
    {
        using var command = database.Command();
        command.CommandText = "SELECT rank FROM rank_history;";
        using var reader = command.ExecuteReader();
        Rank? highest = null;
        while (reader.Read())
        {
            if (Enum.TryParse<Rank>(reader.GetString(0), out var rank) && (highest is null || rank > highest))
            {
                highest = rank;
            }
        }

        return highest;
    }

    /// <inheritdoc />
    public void Record(Rank rank, DateTimeOffset reachedUtc)
    {
        using var command = database.Command();
        command.CommandText = "INSERT INTO rank_history (rank, reached_utc) VALUES ($rank, $when) ON CONFLICT (rank) DO NOTHING;";
        command.With("$rank", rank.ToString()).With("$when", Utc.ToText(reachedUtc)).ExecuteNonQuery();
    }
}

/// <summary>The <c>standups</c> table.</summary>
public sealed class StandUpStore(ProgressDatabase database) : IStandUpStore
{
    /// <inheritdoc />
    public void Record(DateOnly localDate, DateTimeOffset completedUtc, int itemsReviewed)
    {
        using var command = database.Command();
        command.CommandText =
            "INSERT INTO standups (local_date, completed_utc, items_reviewed) VALUES ($date, $when, $items) " +
            "ON CONFLICT (local_date) DO UPDATE SET items_reviewed = standups.items_reviewed + excluded.items_reviewed;";
        command.With("$date", Date(localDate)).With("$when", Utc.ToText(completedUtc)).With("$items", itemsReviewed).ExecuteNonQuery();
    }

    /// <inheritdoc />
    public IReadOnlyList<DateOnly> Between(DateOnly first, DateOnly last)
    {
        using var command = database.Command();
        command.CommandText = "SELECT local_date FROM standups WHERE local_date >= $first AND local_date <= $last ORDER BY local_date;";
        using var reader = command.With("$first", Date(first)).With("$last", Date(last)).ExecuteReader();
        var days = new List<DateOnly>();
        while (reader.Read())
        {
            days.Add(DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        return days;
    }

    internal static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary>The <c>quest_progress</c> table.</summary>
public sealed class QuestProgressStore(ProgressDatabase database) : IQuestProgressStore
{
    /// <inheritdoc />
    public QuestProgressRecord? Find(string questId)
    {
        using var command = database.Command();
        command.CommandText = "SELECT quest_id, status, lesson_completed_utc, completed_utc FROM quest_progress WHERE quest_id = $id;";
        using var reader = command.With("$id", questId).ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    /// <inheritdoc />
    public IReadOnlyList<QuestProgressRecord> All()
    {
        using var command = database.Command();
        command.CommandText = "SELECT quest_id, status, lesson_completed_utc, completed_utc FROM quest_progress ORDER BY quest_id;";
        using var reader = command.ExecuteReader();
        var records = new List<QuestProgressRecord>();
        while (reader.Read())
        {
            records.Add(Read(reader));
        }

        return records;
    }

    /// <inheritdoc />
    public void Save(QuestProgressRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        using var command = database.Command();
        command.CommandText =
            "INSERT INTO quest_progress (quest_id, status, lesson_completed_utc, completed_utc) VALUES ($id, $status, $lesson, $done) " +
            "ON CONFLICT (quest_id) DO UPDATE SET status = excluded.status, lesson_completed_utc = excluded.lesson_completed_utc, completed_utc = excluded.completed_utc;";
        command.With("$id", record.QuestId)
            .With("$status", record.Status.ToString())
            .With("$lesson", record.LessonCompletedUtc is { } lesson ? Utc.ToText(lesson) : null)
            .With("$done", record.CompletedUtc is { } done ? Utc.ToText(done) : null)
            .ExecuteNonQuery();
    }

    private static QuestProgressRecord Read(SqliteDataReader reader) => new(
        reader.GetString(0),
        Enum.Parse<QuestStatus>(reader.GetString(1)),
        reader.IsDBNull(2) ? null : Utc.Parse(reader.GetString(2)),
        reader.IsDBNull(3) ? null : Utc.Parse(reader.GetString(3)));
}

/// <summary>The <c>teachbacks</c> table.</summary>
public sealed class TeachBackStore(ProgressDatabase database) : ITeachBackStore
{
    /// <inheritdoc />
    public void Save(string refId, string path, int wordCount, DateTimeOffset savedUtc)
    {
        using var command = database.Command();
        command.CommandText =
            "INSERT INTO teachbacks (ref_id, path, word_count, saved_utc) VALUES ($ref, $path, $words, $when) " +
            "ON CONFLICT (ref_id) DO UPDATE SET path = excluded.path, word_count = excluded.word_count, saved_utc = excluded.saved_utc;";
        command.With("$ref", refId).With("$path", path).With("$words", wordCount).With("$when", Utc.ToText(savedUtc)).ExecuteNonQuery();
    }

    /// <inheritdoc />
    public bool Exists(string refId)
    {
        using var command = database.Command();
        command.CommandText = "SELECT count(*) FROM teachbacks WHERE ref_id = $ref;";
        return KeyReleaseStore.Count(command.With("$ref", refId)) > 0;
    }
}

/// <summary>The outline version in the <c>meta</c> table (E10-06).</summary>
public sealed class OutlineVersionStore(ProgressDatabase database) : IOutlineVersionStore
{
    /// <inheritdoc />
    public string? Recorded
    {
        get
        {
            using var command = database.Command();
            command.CommandText = "SELECT value FROM meta WHERE key = $key;";
            return command.With("$key", MetaStore.OutlineVersion).ExecuteScalar() as string;
        }
    }

    /// <inheritdoc />
    public void Record(string version)
    {
        using var command = database.Command();
        command.CommandText = "INSERT INTO meta (key, value) VALUES ($key, $value) ON CONFLICT (key) DO UPDATE SET value = excluded.value;";
        command.With("$key", MetaStore.OutlineVersion).With("$value", version).ExecuteNonQuery();
    }
}

/// <summary>The <c>season2</c> table (S2-01).</summary>
public sealed class Season2Store(ProgressDatabase database) : ISeason2Store
{
    /// <inheritdoc />
    public void Add(string itemId, Season2Reason reason, DateTimeOffset addedUtc)
    {
        using var command = database.Command();
        command.CommandText = "INSERT INTO season2 (item_id, reason, added_utc) VALUES ($item, $reason, $when) ON CONFLICT (item_id) DO NOTHING;";
        command.With("$item", itemId).With("$reason", reason.ToString()).With("$when", Utc.ToText(addedUtc)).ExecuteNonQuery();
    }

    /// <inheritdoc />
    public IReadOnlyList<(string ItemId, Season2Reason Reason)> All()
    {
        using var command = database.Command();
        command.CommandText = "SELECT item_id, reason FROM season2 ORDER BY item_id;";
        using var reader = command.ExecuteReader();
        var items = new List<(string, Season2Reason)>();
        while (reader.Read())
        {
            items.Add((reader.GetString(0), Enum.Parse<Season2Reason>(reader.GetString(1))));
        }

        return items;
    }
}
