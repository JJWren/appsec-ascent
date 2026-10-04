using Ascent.Core.Curriculum;
using Ascent.Core.Progress;

namespace Ascent.Assessment.Services;

/// <summary>Today's Stand-up queue.</summary>
/// <param name="Due">Due cards, oldest due first (rematch Domains first).</param>
/// <param name="New">New cards introduced today.</param>
public sealed record StandUpPlan(IReadOnlyList<string> Due, IReadOnlyList<string> New)
{
    /// <summary>Every item, due cards first.</summary>
    public IReadOnlyList<string> All => [.. Due, .. New];
}

/// <summary>
/// Composes the Stand-up (SU-01, SU-02, BF-05): up to 20 due cards, oldest due first, then up to 5 new cards a day
/// from Objectives whose lesson is complete. Domains flagged for a rematch come first. Only the practice pool is used.
/// </summary>
public static class StandUpPlanner
{
    /// <summary>The most due cards per Stand-up.</summary>
    public const int MaxDue = 20;

    /// <summary>The most new cards per local day.</summary>
    public const int MaxNewPerDay = 5;

    /// <summary>Plans today's Stand-up.</summary>
    /// <param name="now">The current instant.</param>
    /// <param name="cards">Every review card.</param>
    /// <param name="questions">The Question Bank, from bundle headers.</param>
    /// <param name="lessonObjectives">Objectives whose lesson is complete.</param>
    /// <param name="rematchDomains">Domains flagged for a rematch.</param>
    /// <param name="introducedToday">New cards already introduced today.</param>
    public static StandUpPlan Plan(
        DateTimeOffset now,
        IReadOnlyList<ReviewCardRecord> cards,
        IReadOnlyList<QuestionInfo> questions,
        IReadOnlySet<string> lessonObjectives,
        IReadOnlySet<string> rematchDomains,
        int introducedToday)
    {
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(questions);
        ArgumentNullException.ThrowIfNull(lessonObjectives);
        ArgumentNullException.ThrowIfNull(rematchDomains);

        var practice = questions.Where(q => q.Pool == "practice").ToDictionary(q => q.ItemId, StringComparer.Ordinal);
        var due = cards
            .Where(c => !c.Suspended && c.DueUtc <= now && practice.ContainsKey(c.ItemId))
            .OrderBy(c => rematchDomains.Contains(c.ExamDomain) ? 0 : 1)
            .ThenBy(c => c.DueUtc)
            .ThenBy(c => c.ItemId, StringComparer.Ordinal)
            .Take(MaxDue)
            .Select(c => c.ItemId)
            .ToList();

        var known = cards.Select(c => c.ItemId).ToHashSet(StringComparer.Ordinal);
        var fresh = practice.Values
            .Where(q => !known.Contains(q.ItemId) && lessonObjectives.Contains(q.ObjectiveId))
            .OrderBy(q => rematchDomains.Contains(q.ExamDomain) ? 0 : 1)
            .ThenBy(q => CurriculumCatalog.ObjectiveKey(q.ObjectiveId))
            .ThenBy(q => q.ItemId, StringComparer.Ordinal)
            .Take(Math.Max(0, MaxNewPerDay - introducedToday))
            .Select(q => q.ItemId)
            .ToList();

        return new StandUpPlan(due, fresh);
    }
}
