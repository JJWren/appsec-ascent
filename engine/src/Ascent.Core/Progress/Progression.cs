using Ascent.Core.Domain;
using Ascent.Core.Time;

namespace Ascent.Core.Progress;

/// <summary>Awards XP idempotently and totals it (XP-01..03).</summary>
public sealed class XpLedger
{
    private readonly IXpStore store;
    private readonly TimeProvider time;

    /// <summary>Creates the ledger.</summary>
    public XpLedger(IXpStore store, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(time);
        this.store = store;
        this.time = time;
    }

    /// <summary>
    /// The total. Events apply in order and the running total never drops below zero, so a penalty only takes away XP
    /// already earned; it never leaves a debt against XP earned later (XP-03).
    /// </summary>
    public long Total => store.All().Aggregate(0L, (total, e) => Math.Max(0, total + e.Points));

    /// <summary>Every event.</summary>
    public IReadOnlyList<XpEvent> Events => store.All();

    /// <summary>Awards XP once per (kind, refId); returns false for a repeat (XP-01).</summary>
    public bool Award(XpKind kind, string refId, int? points = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refId);
        return store.TryAdd(new XpEvent(time.GetUtcNow(), kind, refId, points ?? XpAwards.Points(kind), XpAwards.IsBonus(kind)));
    }

    /// <summary>True when the award was already made.</summary>
    public bool Has(XpKind kind, string refId) => store.Has(kind, refId);
}

/// <summary>What the Rank gates need (RNK-02).</summary>
/// <param name="AllBossFightsPassed">The best score in every one of the 8 Boss Fights is at least 70%.</param>
/// <param name="CapstoneComplete">The Capstone is complete.</param>
public sealed record RankGates(bool AllBossFightsPassed, bool CapstoneComplete);

/// <summary>Ranks as fractions of <c>CoreXpMax</c>, with gates, never demoting (RNK-01..04).</summary>
public static class RankCalculator
{
    /// <summary>The threshold of a Rank as a percentage of <c>CoreXpMax</c> (RNK-01).</summary>
    public static int Percent(Rank rank) => rank switch
    {
        Rank.Developer => 0,
        Rank.SecurityChampion => 10,
        Rank.AppSecEngineer => 35,
        Rank.SeniorAppSecEngineer => 60,
        Rank.PrincipalAppSecEngineer => 85,
        _ => throw new ArgumentOutOfRangeException(nameof(rank), rank, "Unknown Rank."),
    };

    /// <summary>A Rank's display name.</summary>
    public static string DisplayName(Rank rank) => rank switch
    {
        Rank.Developer => "Developer",
        Rank.SecurityChampion => "Security Champion",
        Rank.AppSecEngineer => "AppSec Engineer",
        Rank.SeniorAppSecEngineer => "Senior AppSec Engineer",
        Rank.PrincipalAppSecEngineer => "Principal AppSec Engineer",
        _ => throw new ArgumentOutOfRangeException(nameof(rank), rank, "Unknown Rank."),
    };

    /// <summary>The XP needed for a Rank: its percentage of <c>CoreXpMax</c>, rounded up in whole numbers (no floating point).</summary>
    public static long Threshold(Rank rank, long coreXpMax) => coreXpMax <= 0 ? 0 : ((Percent(rank) * coreXpMax) + 99) / 100;

    /// <summary>The highest Rank whose threshold and gate hold. With no Curriculum yet, only Developer.</summary>
    public static Rank Candidate(long total, long coreXpMax, RankGates gates)
    {
        ArgumentNullException.ThrowIfNull(gates);
        if (coreXpMax <= 0)
        {
            return Rank.Developer;
        }

        var best = Rank.Developer;
        foreach (var rank in Enum.GetValues<Rank>())
        {
            var gate = rank switch
            {
                Rank.SeniorAppSecEngineer => gates.AllBossFightsPassed,
                Rank.PrincipalAppSecEngineer => gates.AllBossFightsPassed && gates.CapstoneComplete,
                _ => true,
            };
            if (total >= Threshold(rank, coreXpMax) && gate)
            {
                best = rank;
            }
        }

        return best;
    }

    /// <summary>The current Rank: never lower than one already reached (RNK-03).</summary>
    public static Rank Current(long total, long coreXpMax, RankGates gates, Rank? reached)
    {
        var candidate = Candidate(total, coreXpMax, gates);
        return reached is { } previous && previous > candidate ? previous : candidate;
    }

    /// <summary>The next Rank, or null at the top.</summary>
    public static Rank? Next(Rank rank) => rank == Rank.PrincipalAppSecEngineer ? null : rank + 1;
}

/// <summary>The badges (domain entities: Badge). They're derived, never stored.</summary>
public enum Badge
{
    /// <summary>The first Flag captured.</summary>
    FirstBlood,

    /// <summary>A Boss Fight scored at 100%.</summary>
    CleanSweep,

    /// <summary>The first confirmed Content Bug.</summary>
    BugHunter,

    /// <summary>5 Teardown Bonuses with no penalties.</summary>
    FrugalEngineer,

    /// <summary>An AI-reviewer injection Flag.</summary>
    PromptBreaker,

    /// <summary>A completed Deep Dive.</summary>
    DeepDiver,

    /// <summary>A week streak of 8.</summary>
    Marathoner,

    /// <summary>The first attempts of Simulation forms A and B both reached 70%.</summary>
    ExamReady,
}

/// <summary>The facts badges are computed from.</summary>
/// <param name="Events">The XP ledger.</param>
/// <param name="BestBossScores">Best Boss Fight score per Domain.</param>
/// <param name="DomainsCleared">Domains whose Quests are complete and whose Boss Fight is passed.</param>
/// <param name="LongestWeekStreak">The longest week streak reached.</param>
/// <param name="ExamReady">True when the first attempts of forms A and B both reached 70%.</param>
public sealed record BadgeFacts(
    IReadOnlyList<XpEvent> Events,
    IReadOnlyDictionary<string, int> BestBossScores,
    IReadOnlyList<string> DomainsCleared,
    int LongestWeekStreak,
    bool ExamReady);

/// <summary>Computes badges from progress.</summary>
public static class Badges
{
    /// <summary>A badge's display name.</summary>
    public static string Name(Badge badge) => badge switch
    {
        Badge.FirstBlood => "First Blood",
        Badge.CleanSweep => "Clean Sweep",
        Badge.BugHunter => "Bug Hunter",
        Badge.FrugalEngineer => "Frugal Engineer",
        Badge.PromptBreaker => "Prompt Breaker",
        Badge.DeepDiver => "Deep Diver",
        Badge.Marathoner => "Marathoner",
        Badge.ExamReady => "Exam Ready",
        _ => throw new ArgumentOutOfRangeException(nameof(badge), badge, "Unknown badge."),
    };

    /// <summary>The earned badges, plus a "Domain Cleared" entry per cleared Domain.</summary>
    public static (IReadOnlyList<Badge> Badges, IReadOnlyList<string> DomainsCleared) Compute(BadgeFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var earned = new List<Badge>();
        bool Any(XpKind kind) => facts.Events.Any(e => e.Kind == kind);
        if (Any(XpKind.LabRed))
        {
            earned.Add(Badge.FirstBlood);
        }

        if (facts.BestBossScores.Values.Any(score => score >= 100))
        {
            earned.Add(Badge.CleanSweep);
        }

        if (Any(XpKind.ContentBug))
        {
            earned.Add(Badge.BugHunter);
        }

        if (facts.Events.Count(e => e.Kind == XpKind.TeardownBonus) >= 5 && !Any(XpKind.TeardownPenalty))
        {
            earned.Add(Badge.FrugalEngineer);
        }

        if (Any(XpKind.ReviewerFlag))
        {
            earned.Add(Badge.PromptBreaker);
        }

        if (Any(XpKind.DeepDive))
        {
            earned.Add(Badge.DeepDiver);
        }

        if (facts.LongestWeekStreak >= 8)
        {
            earned.Add(Badge.Marathoner);
        }

        if (facts.ExamReady)
        {
            earned.Add(Badge.ExamReady);
        }

        return (earned, facts.DomainsCleared);
    }
}

/// <summary>This week's progress toward the weekly goal (WG-04).</summary>
/// <param name="Week">The ISO week.</param>
/// <param name="Days">Stand-up days so far this week.</param>
/// <param name="Goal">The goal.</param>
/// <param name="Met">True once the goal is met.</param>
/// <param name="Streak">Consecutive weeks meeting the goal, including this one when met.</param>
public sealed record WeekStatus(IsoWeek Week, int Days, int Goal, bool Met, int Streak);

/// <summary>Stand-up days, the weekly goal and the week streak (WG-01..04).</summary>
public sealed class WeekGoal
{
    private readonly IStandUpStore standUps;
    private readonly XpLedger ledger;
    private readonly LocalCalendar calendar;
    private readonly TimeProvider time;
    private readonly IProgressTransactions transactions;

    /// <summary>Creates the service.</summary>
    public WeekGoal(IStandUpStore standUps, XpLedger ledger, LocalCalendar calendar, TimeProvider time, IProgressTransactions transactions)
    {
        ArgumentNullException.ThrowIfNull(standUps);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(transactions);
        this.standUps = standUps;
        this.ledger = ledger;
        this.calendar = calendar;
        this.time = time;
        this.transactions = transactions;
    }

    /// <summary>This week's status.</summary>
    public WeekStatus Status(int goal)
    {
        var week = calendar.CurrentWeek;
        var days = DaysIn(week);
        var met = ledger.Has(XpKind.WeeklyGoal, week.ToString());
        return new WeekStatus(week, days, goal, met, Streak(week, includeCurrent: met));
    }

    /// <summary>The longest run of consecutive weeks that met the goal.</summary>
    public int LongestStreak()
    {
        var weeks = MetWeeks().Order().ToList();
        var longest = 0;
        var run = 0;
        IsoWeek? previous = null;
        foreach (var week in weeks)
        {
            run = previous is { } last && last == week.Previous() ? run + 1 : 1;
            longest = Math.Max(longest, run);
            previous = week;
        }

        return longest;
    }

    /// <summary>Records today's Stand-up and awards day, goal and streak XP, all in one transaction (WG-02, WG-03).</summary>
    public WeekStatus RecordStandUp(int itemsReviewed, int goal) => transactions.Run(() =>
    {
        var today = calendar.Today;
        standUps.Record(today, time.GetUtcNow(), itemsReviewed);
        ledger.Award(XpKind.StandUp, today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));

        var week = IsoWeek.Of(today);
        if (DaysIn(week) >= goal && ledger.Award(XpKind.WeeklyGoal, week.ToString()))
        {
            var streak = Streak(week, includeCurrent: true);
            if (streak > 1)
            {
                ledger.Award(XpKind.WeekStreak, week.ToString(), XpAwards.StreakBonus(streak));
            }
        }

        return Status(goal);
    });

    private int DaysIn(IsoWeek week) => standUps.Between(week.Monday, week.Monday.AddDays(6)).Count;

    private int Streak(IsoWeek week, bool includeCurrent)
    {
        var met = MetWeeks().ToHashSet();
        var streak = includeCurrent ? 1 : 0;
        for (var previous = week.Previous(); met.Contains(previous); previous = previous.Previous())
        {
            streak++;
        }

        return includeCurrent || streak > 0 ? streak : 0;
    }

    private IEnumerable<IsoWeek> MetWeeks() =>
        ledger.Events.Where(e => e.Kind == XpKind.WeeklyGoal).Select(e => ParseWeek(e.RefId)).OfType<IsoWeek>();

    private static IsoWeek? ParseWeek(string text)
    {
        var parts = text.Split("-W");
        return parts.Length == 2
            && int.TryParse(parts[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var year)
            && int.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number)
            ? new IsoWeek(year, number)
            : null;
    }
}
