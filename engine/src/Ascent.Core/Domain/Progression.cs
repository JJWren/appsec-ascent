namespace Ascent.Core.Domain;

/// <summary>What earned XP (XP-01).</summary>
public enum XpKind
{
    /// <summary>A lesson with its Teach-back.</summary>
    Lesson,

    /// <summary>A drill.</summary>
    Drill,

    /// <summary>A Lab's Red step (Flag captured).</summary>
    LabRed,

    /// <summary>A Lab's Blue step (fix verified).</summary>
    LabBlue,

    /// <summary>A Lab's Explain step (Teach-back).</summary>
    LabExplain,

    /// <summary>A submitted Deliverable.</summary>
    Deliverable,

    /// <summary>A Deliverable self-scored at or above its pass mark.</summary>
    DeliverablePass,

    /// <summary>A Stand-up day.</summary>
    StandUp,

    /// <summary>A week that met the weekly goal.</summary>
    WeeklyGoal,

    /// <summary>The week-streak bonus.</summary>
    WeekStreak,

    /// <summary>A Boss Fight passed on the first attempt.</summary>
    BossFirstPass,

    /// <summary>A Boss Fight passed after a failed attempt.</summary>
    BossRematchPass,

    /// <summary>A confirmed Content Bug.</summary>
    ContentBug,

    /// <summary>A completed Cloud Stage (bonus).</summary>
    CloudStage,

    /// <summary>A timely teardown (bonus).</summary>
    TeardownBonus,

    /// <summary>A teardown overrun (bonus penalty).</summary>
    TeardownPenalty,

    /// <summary>A completed Deep Dive (bonus).</summary>
    DeepDive,

    /// <summary>The AI-reviewer injection Flag (bonus).</summary>
    ReviewerFlag,
}

/// <summary>The XP table (XP-01, XP-02).</summary>
public static class XpAwards
{
    /// <summary>The standard points for a kind. The week streak is computed by <see cref="StreakBonus"/>.</summary>
    public static int Points(XpKind kind) => kind switch
    {
        XpKind.Lesson => 10,
        XpKind.Drill => 15,
        XpKind.LabRed => 25,
        XpKind.LabBlue => 40,
        XpKind.LabExplain => 10,
        XpKind.Deliverable => 40,
        XpKind.DeliverablePass => 10,
        XpKind.StandUp => 5,
        XpKind.WeeklyGoal => 20,
        XpKind.WeekStreak => 0,
        XpKind.BossFirstPass => 100,
        XpKind.BossRematchPass => 60,
        XpKind.ContentBug => 25,
        XpKind.CloudStage => 30,
        XpKind.TeardownBonus => 10,
        XpKind.TeardownPenalty => -20,
        XpKind.DeepDive => 75,
        XpKind.ReviewerFlag => 30,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown XP kind."),
    };

    /// <summary>True for bonus-only sources, excluded from <c>CoreXpMax</c> (XP-02).</summary>
    public static bool IsBonus(XpKind kind) =>
        kind is XpKind.CloudStage or XpKind.TeardownBonus or XpKind.TeardownPenalty or XpKind.DeepDive or XpKind.ReviewerFlag;

    /// <summary>The week-streak bonus: +5 × min(streak − 1, 4), and nothing for a streak of 1 (WG-03).</summary>
    public static int StreakBonus(int streak) => streak <= 1 ? 0 : 5 * Math.Min(streak - 1, 4);
}

/// <summary>The career ladder (RNK-01).</summary>
public enum Rank
{
    /// <summary>The starting Rank.</summary>
    Developer,

    /// <summary>10% of <c>CoreXpMax</c>.</summary>
    SecurityChampion,

    /// <summary>35% of <c>CoreXpMax</c>.</summary>
    AppSecEngineer,

    /// <summary>60% of <c>CoreXpMax</c>, plus every Boss Fight at 70% or more.</summary>
    SeniorAppSecEngineer,

    /// <summary>85% of <c>CoreXpMax</c>, plus the Senior gate and a complete Capstone.</summary>
    PrincipalAppSecEngineer,
}

/// <summary>A Quest's status.</summary>
public enum QuestStatus
{
    /// <summary>Not opened yet.</summary>
    NotStarted,

    /// <summary>Opened.</summary>
    InProgress,

    /// <summary>Lesson, Teach-back and every non-bonus activity done.</summary>
    Complete,
}

/// <summary>Why an item is in Season 2 (S2-01).</summary>
public enum Season2Reason
{
    /// <summary>A Deep Dive the Learner skipped.</summary>
    SkippedDeepDive,

    /// <summary>A Lab skipped with <c>release next --skip</c>.</summary>
    SkippedLab,
}
