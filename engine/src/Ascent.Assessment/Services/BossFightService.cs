using Ascent.Assessment.Questions;
using Ascent.Core.Curriculum;
using Ascent.Core.Domain;
using Ascent.Core.Errors;
using Ascent.Core.Platform;
using Ascent.Core.Progress;

namespace Ascent.Assessment.Services;

/// <summary>A scored item, shown in the review once the attempt ends.</summary>
/// <param name="ItemId">The question.</param>
/// <param name="Answer">The Learner's answer, or null when unanswered.</param>
/// <param name="Correct">Whether it was right.</param>
public sealed record ScoredItem(string ItemId, string? Answer, bool Correct);

/// <summary>A Boss Fight's result.</summary>
/// <param name="ExamDomain">The Domain.</param>
/// <param name="Correct">Correct answers.</param>
/// <param name="Total">Items.</param>
/// <param name="ScorePercent">The score.</param>
/// <param name="Passed">True at 70% or more.</param>
/// <param name="XpAwarded">XP awarded for this attempt.</param>
/// <param name="Items">Per-item results, in drawn order.</param>
public sealed record BossResult(string ExamDomain, int Correct, int Total, int ScorePercent, bool Passed, int XpAwarded, IReadOnlyList<ScoredItem> Items);

/// <summary>
/// Boss Fights (BF-01..06): 30 practice items spread across the Domain's Objectives, 45 minutes, no feedback until the
/// end, pass at 70%, unlimited retries that never lock anything, and rematch flags that steer the Stand-up.
/// </summary>
public sealed class BossFightService
{
    /// <summary>Items per Boss Fight.</summary>
    public const int ItemCount = 30;

    /// <summary>The pass mark.</summary>
    public const int PassPercent = 70;

    /// <summary>With at least this many items, the previous attempt's items are excluded (BF-04).</summary>
    public const int FreshPoolSize = 60;

    /// <summary>The time limit.</summary>
    public static readonly TimeSpan Duration = TimeSpan.FromMinutes(45);

    private readonly CurriculumCatalog catalog;
    private readonly IAttemptStore attempts;
    private readonly ReviewRecorder reviews;
    private readonly XpLedger ledger;
    private readonly IRandomSource random;
    private readonly TimeProvider time;
    private readonly IProgressTransactions transactions;

    /// <summary>Creates the service.</summary>
    public BossFightService(CurriculumCatalog catalog, IAttemptStore attempts, ReviewRecorder reviews, XpLedger ledger, IRandomSource random, TimeProvider time, IProgressTransactions transactions)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(attempts);
        ArgumentNullException.ThrowIfNull(reviews);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(transactions);
        this.catalog = catalog;
        this.attempts = attempts;
        this.reviews = reviews;
        this.ledger = ledger;
        this.random = random;
        this.time = time;
        this.transactions = transactions;
    }

    /// <summary>The best score per Domain.</summary>
    public IReadOnlyDictionary<string, int> BestScores() =>
        attempts.BossAttempts()
            .Where(a => a.ScorePercent is not null)
            .GroupBy(a => a.ExamDomain, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Max(a => a.ScorePercent!.Value), StringComparer.Ordinal);

    /// <summary>Domains flagged for a rematch: attempted, failed, and not yet passed (BF-05).</summary>
    public IReadOnlySet<string> RematchDomains() =>
        attempts.BossAttempts()
            .Where(a => a.Passed is not null)
            .GroupBy(a => a.ExamDomain, StringComparer.Ordinal)
            .Where(g => g.All(a => a.Passed == false))
            .Select(g => g.Key)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>Resumes the Domain's unfinished Boss Fight, or starts one (BF-01, BF-04, P17).</summary>
    public BossAttemptRecord StartOrResume(string examDomain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(examDomain);
        if (attempts.ActiveBoss() is { } active)
        {
            return active.ExamDomain == examDomain
                ? active
                : throw new UsageException("The " + active.ExamDomain + " Boss Fight is still in progress.", "Finish it first with 'ascent boss " + active.ExamDomain + "'.");
        }

        if (attempts.ActiveSimulation() is not null)
        {
            throw new UsageException("A Simulation is in progress.", "Finish it first with 'ascent sim'.");
        }

        var pool = catalog.Questions.Where(q => q.Pool == "practice" && q.ExamDomain == examDomain).ToList();
        if (pool.Count == 0)
        {
            throw new AscentException("The " + examDomain + " Boss Fight has no questions yet.", "Its Domain pack hasn't been published.");
        }

        var history = attempts.BossAttempts(examDomain);
        var previous = history.LastOrDefault(a => a.FinishedUtc is not null);
        var candidates = pool.Count >= FreshPoolSize && previous is not null
            ? pool.Where(q => !previous.ItemIds.Contains(q.ItemId)).ToList()
            : pool;
        var items = Draw(candidates, reviews.Mastery(pool));
        var now = time.GetUtcNow();
        attempts.StartBoss(examDomain, history.Count == 0 ? BossKind.First : BossKind.Rematch, now, now + Duration, items);
        return attempts.ActiveBoss()!;
    }

    /// <summary>Saves an answer if the deadline hasn't passed (BF-02, P15).</summary>
    public bool Answer(BossAttemptRecord attempt, string itemId, string normalized, DateTimeOffset effectiveNow)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        if (effectiveNow > attempt.DeadlineUtc)
        {
            return false;
        }

        attempts.SaveAnswer(AttemptKind.BossFight, attempt.Id, itemId, normalized, time.GetUtcNow());
        return true;
    }

    /// <summary>The items already answered.</summary>
    public IReadOnlySet<string> Answered(BossAttemptRecord attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        return attempts.Answers(AttemptKind.BossFight, attempt.Id).Select(a => a.ItemId).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Scores the attempt: unanswered items are incorrect, 70% passes, a first-attempt pass earns 100 XP and a later
    /// pass after a failure 60 XP once (BF-02, BF-03). Answers update the FSRS cards (BF-06). The score, XP and cards
    /// are written in one transaction (REL-U2-01).
    /// </summary>
    public BossResult Finish(BossAttemptRecord attempt, Func<string, Question> question)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(question);
        var answers = attempts.Answers(AttemptKind.BossFight, attempt.Id).ToDictionary(a => a.ItemId, a => a.Answer, StringComparer.Ordinal);
        var info = catalog.Questions.ToDictionary(q => q.ItemId, StringComparer.Ordinal);
        var scored = attempt.ItemIds
            .Select(id =>
            {
                var answered = answers.TryGetValue(id, out var answer);
                return new ScoredItem(id, answered ? answer : null, answered && question(id).IsCorrect(answer!));
            })
            .ToList();
        var correct = scored.Count(s => s.Correct);
        var total = scored.Count;
        var score = total == 0 ? 0 : (int)Math.Round(100.0 * correct / total, MidpointRounding.AwayFromZero);
        var passed = score >= PassPercent;

        var xp = transactions.Run(() =>
        {
            var alreadyPassed = attempts.BossAttempts(attempt.ExamDomain).Any(a => a.Id != attempt.Id && a.Passed == true);
            attempts.FinishBoss(attempt.Id, time.GetUtcNow(), correct, total, score, passed);
            var awarded = 0;
            if (passed && !alreadyPassed)
            {
                var kind = attempt.Kind == BossKind.First ? XpKind.BossFirstPass : XpKind.BossRematchPass;
                if (ledger.Award(kind, attempt.ExamDomain))
                {
                    awarded = XpAwards.Points(kind);
                }
            }

            foreach (var item in scored.Where(s => s.Answer is not null && info.ContainsKey(s.ItemId)))
            {
                reviews.Record(info[item.ItemId], item.Correct, ReviewContext.BossFight, 0);
            }

            return awarded;
        });

        return new BossResult(attempt.ExamDomain, correct, total, score, passed, xp, scored);
    }

    // Equal shares per Objective; leftovers go to the Objectives with the lowest mastery (BF-01).
    private List<string> Draw(List<QuestionInfo> candidates, IReadOnlyDictionary<string, double> mastery)
    {
        var byObjective = candidates.GroupBy(q => q.ObjectiveId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => Shuffled(g.Select(q => q.ItemId)), StringComparer.Ordinal);
        var target = Math.Min(ItemCount, candidates.Count);
        var objectives = byObjective.Keys
            .OrderBy(o => mastery.GetValueOrDefault(o, 0))
            .ThenBy(CurriculumCatalog.ObjectiveKey)
            .ToList();
        var chosen = new List<string>();
        var taken = objectives.ToDictionary(o => o, _ => 0, StringComparer.Ordinal);
        while (chosen.Count < target)
        {
            var progress = false;
            foreach (var objective in objectives)
            {
                if (chosen.Count >= target)
                {
                    break;
                }

                if (taken[objective] < byObjective[objective].Count)
                {
                    chosen.Add(byObjective[objective][taken[objective]]);
                    taken[objective]++;
                    progress = true;
                }
            }

            if (!progress)
            {
                break;
            }
        }

        return chosen;
    }

    private List<string> Shuffled(IEnumerable<string> items)
    {
        var list = items.Order(StringComparer.Ordinal).ToList();
        random.Shuffle(list);
        return list;
    }
}
