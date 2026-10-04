using System.Text.Json;
using Ascent.Assessment.Questions;
using Ascent.Assessment.Scheduling;
using Ascent.Core.Curriculum;
using Ascent.Core.Domain;
using Ascent.Core.Errors;
using Ascent.Core.Progress;
using Ascent.Sealing;
using Ascent.Sealing.KeyRelease;

namespace Ascent.Assessment.Services;

/// <summary>Serves questions through the sealing pipeline, so each one is released only in its context (SEAL-03).</summary>
public sealed class QuestionService
{
    private readonly SealedStore store;

    /// <summary>Creates the service.</summary>
    public QuestionService(SealedStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        this.store = store;
    }

    /// <summary>Serves a practice or diagnostic question in a context (SU-02, DX-01).</summary>
    public Question Serve(string itemId, ServeContext context) => Open(itemId, ReleaseContext.Served(context));

    /// <summary>Serves a Simulation question inside its active attempt (SIM-06).</summary>
    public Question ServeInSimulation(string itemId, long attemptId) => Open(itemId, ReleaseContext.Simulation(attemptId));

    private Question Open(string itemId, ReleaseContext context)
    {
        using var item = store.Open(itemId, context);
        try
        {
            return Question.Parse(item.Text());
        }
        catch (JsonException ex)
        {
            throw new AscentException(
                "The question '" + itemId + "' couldn't be read.",
                "Report it with 'ascent bug " + itemId + "'.",
                ExitCodes.CheckFailed,
                ex);
        }
    }
}

/// <summary>Applies answers to FSRS review cards and logs them (SU-04, BF-06).</summary>
public sealed class ReviewRecorder
{
    private readonly IReviewCardStore cards;
    private readonly FsrsScheduler scheduler;
    private readonly TimeProvider time;
    private readonly IProgressTransactions transactions;

    /// <summary>Creates the recorder.</summary>
    public ReviewRecorder(IReviewCardStore cards, FsrsScheduler scheduler, TimeProvider time, IProgressTransactions transactions)
    {
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(transactions);
        this.cards = cards;
        this.scheduler = scheduler;
        this.time = time;
        this.transactions = transactions;
    }

    /// <summary>Records one answer, creating the card the first time an item is seen; the card and the log entry are one transaction.</summary>
    public ReviewCardRecord Record(QuestionInfo question, bool correct, ReviewContext context, long elapsedMs)
    {
        ArgumentNullException.ThrowIfNull(question);
        if (question.Pool != "practice")
        {
            // Diagnostic and Simulation items never become review cards (DX-01, SIM-06).
            throw new InvalidOperationException("Only practice-pool items have review cards.");
        }

        var now = time.GetUtcNow();
        return transactions.Run(() =>
        {
            var card = scheduler.Apply(cards.Find(question.ItemId), question.ItemId, question.ObjectiveId, question.ExamDomain, correct, now);
            cards.Save(card);
            cards.AddReview(new ReviewRecord(question.ItemId, now, context, correct, elapsedMs));
            return card;
        });
    }

    /// <summary>Per-Objective mastery: the share of correct reviews, or 0 when an Objective has none (BF-01).</summary>
    public IReadOnlyDictionary<string, double> Mastery(IReadOnlyList<QuestionInfo> questions)
    {
        ArgumentNullException.ThrowIfNull(questions);
        var objectiveOf = questions.ToDictionary(q => q.ItemId, q => q.ObjectiveId, StringComparer.Ordinal);
        return cards.Reviews()
            .Where(r => objectiveOf.ContainsKey(r.ItemId))
            .GroupBy(r => objectiveOf[r.ItemId], StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(r => r.Correct) / (double)g.Count(), StringComparer.Ordinal);
    }
}

/// <summary>Allocates whole numbers in proportion to weights (largest remainder; ties go to the earlier key).</summary>
public static class Quota
{
    /// <summary>Splits <paramref name="total"/> across keys in proportion to their weights.</summary>
    public static IReadOnlyDictionary<string, int> Allocate(int total, IReadOnlyDictionary<string, double> weights)
    {
        ArgumentNullException.ThrowIfNull(weights);
        var keys = weights.Keys.Order(StringComparer.Ordinal).ToList();
        var sum = keys.Sum(k => Math.Max(0, weights[k]));
        if (keys.Count == 0 || sum <= 0)
        {
            return keys.ToDictionary(k => k, _ => 0, StringComparer.Ordinal);
        }

        var exact = keys.ToDictionary(k => k, k => total * Math.Max(0, weights[k]) / sum, StringComparer.Ordinal);
        var result = exact.ToDictionary(p => p.Key, p => (int)Math.Floor(p.Value), StringComparer.Ordinal);
        var remaining = total - result.Values.Sum();
        foreach (var key in keys.OrderByDescending(k => exact[k] - Math.Floor(exact[k])).ThenBy(k => k, StringComparer.Ordinal).Take(remaining))
        {
            result[key]++;
        }

        return result;
    }
}
