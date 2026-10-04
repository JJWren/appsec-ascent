using System.Text.Json;
using Ascent.Core.Progress;
using FsrsSharp.Configuration;
using FsrsSharp.Core;
using FsrsSharp.Models;

namespace Ascent.Assessment.Scheduling;

/// <summary>The scheduler's card state, persisted as JSON in <c>review_cards.fsrs_state</c>.</summary>
/// <param name="State">New, Learning, Review or Relearning.</param>
/// <param name="Step">The learning step, if any.</param>
/// <param name="Stability">Memory stability in days.</param>
/// <param name="Difficulty">Item difficulty.</param>
/// <param name="Due">When it's due.</param>
/// <param name="LastReview">The last review.</param>
public sealed record CardState(string State, int? Step, double? Stability, double? Difficulty, DateTimeOffset Due, DateTimeOffset? LastReview);

/// <summary>
/// FSRS-6 through Fsrs.Sharp (SU-04): correct = Good, incorrect = Again, desired retention 0.90. Interval fuzzing is
/// off, so schedules are deterministic (P26), and there are no minute-scale learning steps, because review happens once
/// a day in the Stand-up.
/// </summary>
public sealed class FsrsScheduler
{
    /// <summary>The desired retention (SU-04).</summary>
    public const double DesiredRetention = 0.90;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly Scheduler scheduler = new(new FsrsConfig
    {
        DesiredRetention = DesiredRetention,
        EnableFuzzing = false,
        LearningSteps = [],
        RelearningSteps = [],
    });

    /// <summary>A new card, due now.</summary>
    public static CardState NewCard(DateTimeOffset now) => new(nameof(State.Learning), null, null, null, now, null);

    /// <summary>Reviews a card.</summary>
    public CardState Review(CardState card, bool correct, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(card);
        var fsrsCard = new Card(
            Guid.Empty,
            Enum.Parse<State>(card.State),
            card.Step,
            card.Stability,
            card.Difficulty,
            card.Due,
            card.LastReview);
        var result = scheduler.ReviewCard(fsrsCard, correct ? Rating.Good : Rating.Again, now, null);
        var next = result.Card;
        return new CardState(next.State.ToString(), next.Step, next.Stability, next.Difficulty, next.Due, next.LastReview);
    }

    /// <summary>Serializes a card state.</summary>
    public static string ToJson(CardState state) => JsonSerializer.Serialize(state, JsonOptions);

    /// <summary>Reads a card state.</summary>
    public static CardState FromJson(string json) => JsonSerializer.Deserialize<CardState>(json, JsonOptions)
        ?? throw new JsonException("The card state is empty.");

    /// <summary>
    /// Applies an answer to an item's review card, creating the card if it's new, and logs the review (SU-04, BF-06).
    /// </summary>
    public ReviewCardRecord Apply(ReviewCardRecord? existing, string itemId, string objectiveId, string examDomain, bool correct, DateTimeOffset now)
    {
        var state = existing is null ? NewCard(now) : FromJson(existing.FsrsState);
        var next = Review(state, correct, now);
        return new ReviewCardRecord(
            itemId,
            existing?.ObjectiveId ?? objectiveId,
            existing?.ExamDomain ?? examDomain,
            ToJson(next),
            next.Due,
            existing?.IntroducedUtc ?? now,
            now,
            (existing?.Reps ?? 0) + 1,
            (existing?.Lapses ?? 0) + (correct ? 0 : existing is null ? 0 : 1),
            existing?.Suspended ?? false);
    }
}
