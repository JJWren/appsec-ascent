using System.Text.Json;
using Ascent.Core.Progress;
using Ascent.Core.Time;
using Microsoft.Data.Sqlite;

namespace Ascent.Storage;

/// <summary>The <c>review_cards</c> and <c>reviews</c> tables.</summary>
public sealed class ReviewCardStore(ProgressDatabase database) : IReviewCardStore
{
    private const string CardColumns =
        "item_id, objective_id, exam_domain, fsrs_state, due_utc, introduced_utc, last_review_utc, reps, lapses, suspended";

    /// <inheritdoc />
    public ReviewCardRecord? Find(string itemId)
    {
        using var command = database.Command();
        command.CommandText = "SELECT " + CardColumns + " FROM review_cards WHERE item_id = $item;";
        using var reader = command.With("$item", itemId).ExecuteReader();
        return reader.Read() ? ReadCard(reader) : null;
    }

    /// <inheritdoc />
    public IReadOnlyList<ReviewCardRecord> All()
    {
        using var command = database.Command();
        command.CommandText = "SELECT " + CardColumns + " FROM review_cards ORDER BY due_utc, item_id;";
        using var reader = command.ExecuteReader();
        var cards = new List<ReviewCardRecord>();
        while (reader.Read())
        {
            cards.Add(ReadCard(reader));
        }

        return cards;
    }

    /// <inheritdoc />
    public void Save(ReviewCardRecord card)
    {
        ArgumentNullException.ThrowIfNull(card);
        using var command = database.Command();
        command.CommandText =
            "INSERT INTO review_cards (" + CardColumns + ") VALUES ($item, $objective, $domain, $state, $due, $introduced, $last, $reps, $lapses, $suspended) " +
            "ON CONFLICT (item_id) DO UPDATE SET objective_id = excluded.objective_id, exam_domain = excluded.exam_domain, fsrs_state = excluded.fsrs_state, " +
            "due_utc = excluded.due_utc, last_review_utc = excluded.last_review_utc, reps = excluded.reps, lapses = excluded.lapses, suspended = excluded.suspended;";
        command.With("$item", card.ItemId)
            .With("$objective", card.ObjectiveId)
            .With("$domain", card.ExamDomain)
            .With("$state", card.FsrsState)
            .With("$due", Utc.ToText(card.DueUtc))
            .With("$introduced", Utc.ToText(card.IntroducedUtc))
            .With("$last", card.LastReviewUtc is { } last ? Utc.ToText(last) : null)
            .With("$reps", card.Reps)
            .With("$lapses", card.Lapses)
            .With("$suspended", card.Suspended ? 1 : 0)
            .ExecuteNonQuery();
    }

    /// <inheritdoc />
    public void AddReview(ReviewRecord review)
    {
        ArgumentNullException.ThrowIfNull(review);
        using var command = database.Command();
        command.CommandText =
            "INSERT INTO reviews (item_id, reviewed_utc, context, correct, rating, elapsed_ms) VALUES ($item, $when, $context, $correct, $rating, $elapsed);";
        command.With("$item", review.ItemId)
            .With("$when", Utc.ToText(review.ReviewedUtc))
            .With("$context", review.Context.ToString())
            .With("$correct", review.Correct ? 1 : 0)
            .With("$rating", review.Correct ? "Good" : "Again")
            .With("$elapsed", review.ElapsedMs)
            .ExecuteNonQuery();
    }

    /// <inheritdoc />
    public IReadOnlyList<ReviewRecord> Reviews()
    {
        using var command = database.Command();
        command.CommandText = "SELECT item_id, reviewed_utc, context, correct, elapsed_ms FROM reviews ORDER BY id;";
        using var reader = command.ExecuteReader();
        var reviews = new List<ReviewRecord>();
        while (reader.Read())
        {
            reviews.Add(new ReviewRecord(reader.GetString(0), Utc.Parse(reader.GetString(1)), Enum.Parse<ReviewContext>(reader.GetString(2)), reader.GetInt32(3) == 1, reader.GetInt64(4)));
        }

        return reviews;
    }

    /// <inheritdoc />
    public void SetSuspended(string itemId, bool suspended)
    {
        using var command = database.Command();
        command.CommandText = "UPDATE review_cards SET suspended = $suspended WHERE item_id = $item;";
        command.With("$item", itemId).With("$suspended", suspended ? 1 : 0).ExecuteNonQuery();
    }

    /// <inheritdoc />
    public int RemapObjectives(Func<string, string?> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return database.Run(() =>
        {
            var changed = 0;
            foreach (var card in All())
            {
                var mapped = map(card.ObjectiveId) ?? string.Empty;
                if (!string.Equals(mapped, card.ObjectiveId, StringComparison.Ordinal))
                {
                    var domain = mapped.Length > 0 && char.IsAsciiDigit(mapped[0]) ? "D" + mapped[0] : card.ExamDomain;
                    Save(card with { ObjectiveId = mapped, ExamDomain = domain });
                    changed++;
                }
            }

            return changed;
        });
    }

    private static ReviewCardRecord ReadCard(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        Utc.Parse(reader.GetString(4)),
        Utc.Parse(reader.GetString(5)),
        reader.IsDBNull(6) ? null : Utc.Parse(reader.GetString(6)),
        reader.GetInt32(7),
        reader.GetInt32(8),
        reader.GetInt32(9) == 1);
}

/// <summary>Diagnostic, Boss Fight and Simulation attempts, plus write-ahead answers (P15, P17).</summary>
public sealed class AttemptStore(ProgressDatabase database) : IAttemptStore
{
    /// <inheritdoc />
    public DiagnosticRecord? OpenDiagnostic()
    {
        using var command = database.Command();
        command.CommandText = "SELECT id, started_utc, taken_utc, item_ids, per_domain, plan FROM diagnostic WHERE taken_utc IS NULL ORDER BY id DESC LIMIT 1;";
        return ReadDiagnostic(command);
    }

    /// <inheritdoc />
    public DiagnosticRecord? LatestDiagnostic()
    {
        using var command = database.Command();
        command.CommandText = "SELECT id, started_utc, taken_utc, item_ids, per_domain, plan FROM diagnostic WHERE taken_utc IS NOT NULL ORDER BY id DESC LIMIT 1;";
        return ReadDiagnostic(command);
    }

    /// <inheritdoc />
    public long StartDiagnostic(IReadOnlyList<string> itemIds, DateTimeOffset started)
    {
        using var command = database.Command();
        command.CommandText = "INSERT INTO diagnostic (started_utc, item_ids) VALUES ($started, $items) RETURNING id;";
        return (long)command.With("$started", Utc.ToText(started)).With("$items", Json(itemIds)).ExecuteScalar()!;
    }

    /// <inheritdoc />
    public void FinishDiagnostic(long id, DateTimeOffset taken, string perDomainJson, string planJson)
    {
        using var command = database.Command();
        command.CommandText = "UPDATE diagnostic SET taken_utc = $taken, per_domain = $perDomain, plan = $plan WHERE id = $id;";
        command.With("$id", id).With("$taken", Utc.ToText(taken)).With("$perDomain", perDomainJson).With("$plan", planJson).ExecuteNonQuery();
    }

    /// <inheritdoc />
    public BossAttemptRecord? ActiveBoss()
    {
        using var command = database.Command();
        command.CommandText = "SELECT id, exam_domain, kind, started_utc, deadline_utc, finished_utc, item_ids, score_percent, passed FROM boss_attempts WHERE finished_utc IS NULL ORDER BY id;";
        return ReadBosses(command).FirstOrDefault();
    }

    /// <inheritdoc />
    public IReadOnlyList<BossAttemptRecord> BossAttempts(string? examDomain = null)
    {
        using var command = database.Command();
        if (examDomain is null)
        {
            command.CommandText = "SELECT id, exam_domain, kind, started_utc, deadline_utc, finished_utc, item_ids, score_percent, passed FROM boss_attempts ORDER BY id;";
        }
        else
        {
            command.CommandText = "SELECT id, exam_domain, kind, started_utc, deadline_utc, finished_utc, item_ids, score_percent, passed FROM boss_attempts WHERE exam_domain = $domain ORDER BY id;";
            command.With("$domain", examDomain);
        }

        return ReadBosses(command);
    }

    /// <inheritdoc />
    public long StartBoss(string examDomain, BossKind kind, DateTimeOffset started, DateTimeOffset deadline, IReadOnlyList<string> itemIds) =>
        database.Run(() =>
        {
            EnsureNothingActive();
            using var command = database.Command();
            command.CommandText =
                "INSERT INTO boss_attempts (exam_domain, kind, started_utc, deadline_utc, item_ids) VALUES ($domain, $kind, $started, $deadline, $items) RETURNING id;";
            return (long)command.With("$domain", examDomain)
                .With("$kind", kind.ToString())
                .With("$started", Utc.ToText(started))
                .With("$deadline", Utc.ToText(deadline))
                .With("$items", Json(itemIds))
                .ExecuteScalar()!;
        });

    /// <inheritdoc />
    public void FinishBoss(long id, DateTimeOffset finished, int correct, int total, int scorePercent, bool passed)
    {
        using var command = database.Command();
        command.CommandText =
            "UPDATE boss_attempts SET finished_utc = $finished, correct = $correct, total = $total, score_percent = $score, passed = $passed WHERE id = $id;";
        command.With("$id", id)
            .With("$finished", Utc.ToText(finished))
            .With("$correct", correct)
            .With("$total", total)
            .With("$score", scorePercent)
            .With("$passed", passed ? 1 : 0)
            .ExecuteNonQuery();
    }

    /// <inheritdoc />
    public SimulationAttemptRecord? ActiveSimulation()
    {
        using var command = database.Command();
        command.CommandText = "SELECT id, form, started_utc, deadline_utc, status, item_ids, score_percent, seen_form FROM simulation_attempts WHERE status = 'InProgress' ORDER BY id;";
        return ReadSimulations(command).FirstOrDefault();
    }

    /// <inheritdoc />
    public IReadOnlyList<SimulationAttemptRecord> SimulationAttempts()
    {
        using var command = database.Command();
        command.CommandText = "SELECT id, form, started_utc, deadline_utc, status, item_ids, score_percent, seen_form FROM simulation_attempts ORDER BY id;";
        return ReadSimulations(command);
    }

    /// <inheritdoc />
    public long StartSimulation(string form, DateTimeOffset started, DateTimeOffset deadline, IReadOnlyList<string> itemIds, bool seenForm) =>
        database.Run(() =>
        {
            EnsureNothingActive();
            using var command = database.Command();
            command.CommandText =
                "INSERT INTO simulation_attempts (form, started_utc, deadline_utc, status, item_ids, seen_form) " +
                "VALUES ($form, $started, $deadline, 'InProgress', $items, $seen) RETURNING id;";
            return (long)command.With("$form", form)
                .With("$started", Utc.ToText(started))
                .With("$deadline", Utc.ToText(deadline))
                .With("$items", Json(itemIds))
                .With("$seen", seenForm ? 1 : 0)
                .ExecuteScalar()!;
        });

    /// <inheritdoc />
    public void FinishSimulation(long id, SimulationStatus status, DateTimeOffset finished, int correct, int total, int scorePercent)
    {
        using var command = database.Command();
        command.CommandText =
            "UPDATE simulation_attempts SET status = $status, finished_utc = $finished, correct = $correct, total = $total, score_percent = $score WHERE id = $id;";
        command.With("$id", id)
            .With("$status", status.ToString())
            .With("$finished", Utc.ToText(finished))
            .With("$correct", correct)
            .With("$total", total)
            .With("$score", scorePercent)
            .ExecuteNonQuery();
    }

    /// <inheritdoc />
    public void SaveAnswer(AttemptKind kind, long attemptId, string itemId, string answer, DateTimeOffset answered)
    {
        using var command = database.Command();
        command.CommandText =
            "INSERT INTO attempt_answers (attempt_kind, attempt_id, item_id, answer, answered_utc) VALUES ($kind, $attempt, $item, $answer, $when) " +
            "ON CONFLICT (attempt_kind, attempt_id, item_id) DO UPDATE SET answer = excluded.answer, answered_utc = excluded.answered_utc;";
        command.With("$kind", kind.ToString())
            .With("$attempt", attemptId)
            .With("$item", itemId)
            .With("$answer", answer)
            .With("$when", Utc.ToText(answered))
            .ExecuteNonQuery();
    }

    /// <inheritdoc />
    public IReadOnlyList<AttemptAnswer> Answers(AttemptKind kind, long attemptId)
    {
        using var command = database.Command();
        command.CommandText =
            "SELECT item_id, answer, answered_utc FROM attempt_answers WHERE attempt_kind = $kind AND attempt_id = $attempt ORDER BY answered_utc, item_id;";
        using var reader = command.With("$kind", kind.ToString()).With("$attempt", attemptId).ExecuteReader();
        var answers = new List<AttemptAnswer>();
        while (reader.Read())
        {
            answers.Add(new AttemptAnswer(reader.GetString(0), reader.GetString(1), Utc.Parse(reader.GetString(2))));
        }

        return answers;
    }

    private static string Json(IReadOnlyList<string> items) => JsonSerializer.Serialize(items);

    private static List<string> Items(string json) => JsonSerializer.Deserialize<List<string>>(json) ?? [];

    private void EnsureNothingActive()
    {
        if (ActiveBoss() is not null || ActiveSimulation() is not null)
        {
            throw new InvalidOperationException("Another Boss Fight or Simulation is still in progress.");
        }
    }

    private static DiagnosticRecord? ReadDiagnostic(SqliteCommand command)
    {
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new DiagnosticRecord(
                reader.GetInt64(0),
                Utc.Parse(reader.GetString(1)),
                reader.IsDBNull(2) ? null : Utc.Parse(reader.GetString(2)),
                Items(reader.GetString(3)),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5))
            : null;
    }

    private static List<BossAttemptRecord> ReadBosses(SqliteCommand command)
    {
        using var reader = command.ExecuteReader();
        var attempts = new List<BossAttemptRecord>();
        while (reader.Read())
        {
            attempts.Add(new BossAttemptRecord(
                reader.GetInt64(0),
                reader.GetString(1),
                Enum.Parse<BossKind>(reader.GetString(2)),
                Utc.Parse(reader.GetString(3)),
                Utc.Parse(reader.GetString(4)),
                reader.IsDBNull(5) ? null : Utc.Parse(reader.GetString(5)),
                Items(reader.GetString(6)),
                reader.IsDBNull(7) ? null : reader.GetInt32(7),
                reader.IsDBNull(8) ? null : reader.GetInt32(8) == 1));
        }

        return attempts;
    }

    private static List<SimulationAttemptRecord> ReadSimulations(SqliteCommand command)
    {
        using var reader = command.ExecuteReader();
        var attempts = new List<SimulationAttemptRecord>();
        while (reader.Read())
        {
            attempts.Add(new SimulationAttemptRecord(
                reader.GetInt64(0),
                reader.GetString(1),
                Utc.Parse(reader.GetString(2)),
                Utc.Parse(reader.GetString(3)),
                Enum.Parse<SimulationStatus>(reader.GetString(4)),
                Items(reader.GetString(5)),
                reader.IsDBNull(6) ? null : reader.GetInt32(6),
                reader.GetInt32(7) == 1));
        }

        return attempts;
    }
}
