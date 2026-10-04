using System.Globalization;
using System.Text.Json;
using Ascent.Assessment.Questions;
using Ascent.Content.Model;
using Ascent.Core.Curriculum;
using Ascent.Core.Errors;
using Ascent.Core.Platform;
using Ascent.Core.Progress;

namespace Ascent.Assessment.Services;

/// <summary>A Domain's Diagnostic result and its share of the plan.</summary>
/// <param name="Domain">D1–D8.</param>
/// <param name="Correct">Correct answers.</param>
/// <param name="Total">Items asked.</param>
/// <param name="Factor">Exam weight × 1.25 when the score is under 60% (DX-02), normalized.</param>
/// <param name="Weeks">Study weeks allocated.</param>
public sealed record DomainPlan(string Domain, int Correct, int Total, double Factor, int Weeks)
{
    /// <summary>The score in percent; 0 when no item was asked.</summary>
    public int ScorePercent => Total == 0 ? 0 : (int)Math.Round(100.0 * Correct / Total, MidpointRounding.AwayFromZero);
}

/// <summary>The study plan from the Diagnostic (DX-02, DX-03).</summary>
/// <param name="Domains">Per-Domain allocations, in exam order.</param>
/// <param name="EstimatedHours">Estimated study hours.</param>
/// <param name="TotalWeeks">Study weeks at the Learner's weekly hours.</param>
/// <param name="Start">The plan's start date.</param>
/// <param name="ExamWindowStart">The plan's end plus the 2 Simulation weeks: the earliest suggested exam date.</param>
/// <param name="ExamWindowEnd">One week after <paramref name="ExamWindowStart"/>.</param>
public sealed record StudyPlan(IReadOnlyList<DomainPlan> Domains, double EstimatedHours, int TotalWeeks, DateOnly Start, DateOnly ExamWindowStart, DateOnly ExamWindowEnd);

/// <summary>Builds the study plan (DX-02, DX-03).</summary>
public static class StudyPlanner
{
    /// <summary>Hours allowed per Lab.</summary>
    public const double LabHours = 1.5;

    /// <summary>Hours per Boss Fight.</summary>
    public const double BossHours = 0.75;

    /// <summary>Hours for the two Simulations.</summary>
    public const double SimulationHours = 2 * 3;

    /// <summary>Plans study weeks per Domain.</summary>
    public static StudyPlan Plan(IReadOnlyDictionary<string, (int Correct, int Total)> perDomain, Outline outline, CurriculumCatalog catalog, int weeklyHours, DateOnly start)
    {
        ArgumentNullException.ThrowIfNull(perDomain);
        ArgumentNullException.ThrowIfNull(outline);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentOutOfRangeException.ThrowIfLessThan(weeklyHours, 1);

        var factors = outline.Domains.ToDictionary(
            d => d.Id,
            d =>
            {
                var (correct, total) = perDomain.GetValueOrDefault(d.Id);
                var weak = total == 0 || 100.0 * correct / total < 60;
                return d.Weight * (weak ? 1.25 : 1.0);
            },
            StringComparer.Ordinal);
        var sum = factors.Values.Sum();

        var hours = catalog.Quests.Sum(q => q.EstimatedMinutes) / 60.0
            + catalog.Labs.Count * LabHours
            + catalog.BossDomains.Count * BossHours
            + SimulationHours;
        var totalWeeks = Math.Max(1, (int)Math.Ceiling(hours / weeklyHours));
        var weeks = Quota.Allocate(totalWeeks, factors);

        var domains = outline.Domains
            .Select(d =>
            {
                var (correct, total) = perDomain.GetValueOrDefault(d.Id);
                return new DomainPlan(d.Id, correct, total, factors[d.Id] / sum, weeks[d.Id]);
            })
            .ToList();
        var examStart = start.AddDays(7 * (totalWeeks + 2));
        return new StudyPlan(domains, Math.Round(hours, 1), totalWeeks, start, examStart, examStart.AddDays(7));
    }
}

/// <summary>The Diagnostic (DX-01): 40 items from the diagnostic pool, untimed, no feedback until the end.</summary>
public sealed class DiagnosticService
{
    /// <summary>The number of items.</summary>
    public const int ItemCount = 40;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly CurriculumCatalog catalog;
    private readonly IAttemptStore attempts;
    private readonly IRandomSource random;
    private readonly TimeProvider time;

    /// <summary>Creates the service.</summary>
    public DiagnosticService(CurriculumCatalog catalog, IAttemptStore attempts, IRandomSource random, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(attempts);
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(time);
        this.catalog = catalog;
        this.attempts = attempts;
        this.random = random;
        this.time = time;
    }

    /// <summary>Resumes the open Diagnostic, or starts one with items spread by exam weight.</summary>
    public DiagnosticRecord StartOrResume()
    {
        if (attempts.OpenDiagnostic() is { } open)
        {
            return open;
        }

        var outline = catalog.Outline ?? throw new AscentException("No exam outline was found.", "Run 'ascent lint' for details.");
        var pool = catalog.Questions.Where(q => q.Pool == "diagnostic").ToList();
        if (pool.Count == 0)
        {
            throw new AscentException("The Diagnostic has no questions yet.", "It's part of the Orientation pack; come back once that pack is published.");
        }

        var weights = outline.Domains.ToDictionary(d => d.Id, d => (double)d.Weight, StringComparer.Ordinal);
        var items = Draw(pool, weights, Math.Min(ItemCount, pool.Count));
        attempts.StartDiagnostic(items, time.GetUtcNow());
        return attempts.OpenDiagnostic()!;
    }

    /// <summary>Saves one answer immediately (P15).</summary>
    public void Answer(DiagnosticRecord attempt, string itemId, string normalized)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        attempts.SaveAnswer(AttemptKind.Diagnostic, attempt.Id, itemId, normalized, time.GetUtcNow());
    }

    /// <summary>The items already answered.</summary>
    public IReadOnlySet<string> Answered(DiagnosticRecord attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        return attempts.Answers(AttemptKind.Diagnostic, attempt.Id).Select(a => a.ItemId).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Scores the Diagnostic, builds the plan and records both (DX-01..03). Items never become review cards.</summary>
    public StudyPlan Finish(DiagnosticRecord attempt, Func<string, Question> question, int weeklyHours, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(question);
        var outline = catalog.Outline ?? throw new AscentException("No exam outline was found.", "Run 'ascent lint' for details.");
        var answers = attempts.Answers(AttemptKind.Diagnostic, attempt.Id).ToDictionary(a => a.ItemId, a => a.Answer, StringComparer.Ordinal);
        var domainOf = catalog.Questions.ToDictionary(q => q.ItemId, q => q.ExamDomain, StringComparer.Ordinal);
        var perDomain = new Dictionary<string, (int Correct, int Total)>(StringComparer.Ordinal);
        foreach (var itemId in attempt.ItemIds)
        {
            var domain = domainOf.GetValueOrDefault(itemId, string.Empty);
            var correct = answers.TryGetValue(itemId, out var answer) && question(itemId).IsCorrect(answer);
            var (c, t) = perDomain.GetValueOrDefault(domain);
            perDomain[domain] = (c + (correct ? 1 : 0), t + 1);
        }

        var plan = StudyPlanner.Plan(perDomain, outline, catalog, weeklyHours, today);
        attempts.FinishDiagnostic(
            attempt.Id,
            time.GetUtcNow(),
            JsonSerializer.Serialize(perDomain.ToDictionary(p => p.Key, p => new[] { p.Value.Correct, p.Value.Total }), JsonOptions),
            JsonSerializer.Serialize(plan, JsonOptions));
        return plan;
    }

    private List<string> Draw(List<QuestionInfo> pool, IReadOnlyDictionary<string, double> weights, int count)
    {
        var quotas = Quota.Allocate(count, weights);
        var byDomain = pool.GroupBy(q => q.ExamDomain, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => Shuffled(g.Select(q => q.ItemId)), StringComparer.Ordinal);
        var chosen = new List<string>();
        foreach (var (domain, quota) in quotas.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (byDomain.TryGetValue(domain, out var items))
            {
                chosen.AddRange(items.Take(quota));
            }
        }

        // Domains without enough items give their share to the rest.
        var spare = Shuffled(pool.Select(q => q.ItemId).Where(id => !chosen.Contains(id)));
        chosen.AddRange(spare.Take(count - chosen.Count));
        return chosen;
    }

    private List<string> Shuffled(IEnumerable<string> items)
    {
        var list = items.Order(StringComparer.Ordinal).ToList();
        random.Shuffle(list);
        return list;
    }
}

/// <summary>Formatting for plans.</summary>
public static class PlanText
{
    /// <summary>A date as yyyy-MM-dd.</summary>
    public static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
