using System.Globalization;
using Ascent.Assessment.Questions;
using Ascent.Core.Curriculum;
using Ascent.Core.Errors;
using Ascent.Core.Progress;

namespace Ascent.Assessment.Services;

/// <summary>A Simulation's result.</summary>
/// <param name="Form">A or B.</param>
/// <param name="Status">Finished or expired.</param>
/// <param name="Correct">Correct answers.</param>
/// <param name="Total">Items.</param>
/// <param name="ScorePercent">The raw percentage (SIM-04: no imitation of scaled scoring).</param>
/// <param name="Items">Per-item results for the review.</param>
public sealed record SimulationResult(string Form, SimulationStatus Status, int Correct, int Total, int ScorePercent, IReadOnlyList<ScoredItem> Items)
{
    /// <summary>True at the 70% target.</summary>
    public bool ReachedTarget => ScorePercent >= SimulationService.TargetPercent;
}

/// <summary>
/// Full mock exams (SIM-01..06): unlocked by 70% in every Boss Fight; two fixed 125-item forms built from the reserved
/// pool by Domain weight; a 3-hour clock that keeps running if the Engine exits; no feedback until the end.
/// </summary>
public sealed class SimulationService
{
    /// <summary>Items per form.</summary>
    public const int FormSize = 125;

    /// <summary>The score target.</summary>
    public const int TargetPercent = 70;

    /// <summary>The time limit.</summary>
    public static readonly TimeSpan Duration = TimeSpan.FromHours(3);

    private readonly CurriculumCatalog catalog;
    private readonly IAttemptStore attempts;
    private readonly TimeProvider time;

    /// <summary>Creates the service.</summary>
    public SimulationService(CurriculumCatalog catalog, IAttemptStore attempts, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(attempts);
        ArgumentNullException.ThrowIfNull(time);
        this.catalog = catalog;
        this.attempts = attempts;
        this.time = time;
    }

    /// <summary>Domains whose best Boss Fight score is under 70%; the Simulation is locked while any remain (SIM-01).</summary>
    public IReadOnlyList<string> LockedBy(IReadOnlyDictionary<string, int> bestBossScores)
    {
        ArgumentNullException.ThrowIfNull(bestBossScores);
        var domains = catalog.Outline?.Domains.Select(d => d.Id) ?? [];
        return domains.Where(d => bestBossScores.GetValueOrDefault(d) < BossFightService.PassPercent).ToList();
    }

    /// <summary>
    /// The two fixed forms (SIM-02): each Domain's reserved items, sorted by ID, are split into A and B by its weight's
    /// share of 125.
    /// </summary>
    public (IReadOnlyList<string> A, IReadOnlyList<string> B) Forms()
    {
        var outline = catalog.Outline ?? throw new AscentException("No exam outline was found.", "Run 'ascent lint' for details.");
        var quotas = Quota.Allocate(FormSize, outline.Domains.ToDictionary(d => d.Id, d => (double)d.Weight, StringComparer.Ordinal));
        var reserved = catalog.Questions.Where(q => q.Pool == "simulation").ToList();
        var a = new List<string>();
        var b = new List<string>();
        var shortfall = 0;
        foreach (var (domain, quota) in quotas.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var items = reserved.Where(q => q.ExamDomain == domain).Select(q => q.ItemId).Order(StringComparer.Ordinal).ToList();
            shortfall += Math.Max(0, 2 * quota - items.Count);
            a.AddRange(items.Take(quota));
            b.AddRange(items.Skip(quota).Take(quota));
        }

        return shortfall == 0
            ? (a, b)
            : throw new AscentException(
                string.Create(CultureInfo.InvariantCulture, $"The Simulation pool needs {shortfall} more item(s) before its forms are complete."),
                "The Simulations are part of the final pack; come back once it's published.");
    }

    /// <summary>The in-progress attempt, if any.</summary>
    public SimulationAttemptRecord? Active() => attempts.ActiveSimulation();

    /// <summary>
    /// Starts a form: the first one not attempted yet, otherwise the one requested (marked as seen, SIM-05).
    /// </summary>
    public SimulationAttemptRecord Start(string? requestedForm)
    {
        var (a, b) = Forms();
        var attempted = attempts.SimulationAttempts().Select(s => s.Form).ToHashSet(StringComparer.Ordinal);
        var form = requestedForm?.ToUpperInvariant() switch
        {
            "A" or "B" => requestedForm.ToUpperInvariant(),
            null => !attempted.Contains("A") ? "A" : !attempted.Contains("B") ? "B" : throw new UsageException("Both forms have been taken.", "Choose one to retake with 'ascent sim A' or 'ascent sim B'."),
            _ => throw new UsageException("There are only forms A and B.", "Use 'ascent sim A' or 'ascent sim B'."),
        };
        var now = time.GetUtcNow();
        attempts.StartSimulation(form, now, now + Duration, form == "A" ? a : b, attempted.Contains(form));
        return attempts.ActiveSimulation()!;
    }

    /// <summary>Saves an answer if the deadline hasn't passed (SIM-03, P15).</summary>
    public bool Answer(SimulationAttemptRecord attempt, string itemId, string normalized, DateTimeOffset effectiveNow)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        if (effectiveNow > attempt.DeadlineUtc)
        {
            return false;
        }

        attempts.SaveAnswer(AttemptKind.Simulation, attempt.Id, itemId, normalized, time.GetUtcNow());
        return true;
    }

    /// <summary>The items already answered.</summary>
    public IReadOnlySet<string> Answered(SimulationAttemptRecord attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        return attempts.Answers(AttemptKind.Simulation, attempt.Id).Select(a => a.ItemId).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Scores the attempt. It must still be in progress, because its items are only released inside an active attempt;
    /// the review is shown from this result. Items never become review cards (SIM-06).
    /// </summary>
    public SimulationResult Finish(SimulationAttemptRecord attempt, Func<string, Question> question, bool expired)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(question);
        var answers = attempts.Answers(AttemptKind.Simulation, attempt.Id).ToDictionary(a => a.ItemId, a => a.Answer, StringComparer.Ordinal);
        var scored = attempt.ItemIds
            .Select(id =>
            {
                var answered = answers.TryGetValue(id, out var answer);
                return new ScoredItem(id, answered ? answer : null, answered && question(id).IsCorrect(answer!));
            })
            .ToList();
        var correct = scored.Count(s => s.Correct);
        var score = scored.Count == 0 ? 0 : (int)Math.Round(100.0 * correct / scored.Count, MidpointRounding.AwayFromZero);
        var status = expired ? SimulationStatus.Expired : SimulationStatus.Finished;
        attempts.FinishSimulation(attempt.Id, status, time.GetUtcNow(), correct, scored.Count, score);
        return new SimulationResult(attempt.Form, status, correct, scored.Count, score, scored);
    }

    /// <summary>True when the first attempts of both forms reached 70% (SIM-05: Exam Ready).</summary>
    public bool ExamReady()
    {
        var firsts = attempts.SimulationAttempts().Where(s => !s.SeenForm && s.Status != SimulationStatus.InProgress).ToList();
        return firsts.Any(s => s.Form == "A" && s.ScorePercent >= TargetPercent)
            && firsts.Any(s => s.Form == "B" && s.ScorePercent >= TargetPercent);
    }
}
