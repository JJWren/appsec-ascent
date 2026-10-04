namespace Ascent.Core.Progress;

/// <summary>A question's FSRS review card (SU-04).</summary>
/// <param name="ItemId">The question.</param>
/// <param name="ObjectiveId">Its Objective.</param>
/// <param name="ExamDomain">Its Domain.</param>
/// <param name="FsrsState">The scheduler's card state as JSON.</param>
/// <param name="DueUtc">When it's next due.</param>
/// <param name="IntroducedUtc">When it entered the Stand-up.</param>
/// <param name="LastReviewUtc">The last review.</param>
/// <param name="Reps">Reviews so far.</param>
/// <param name="Lapses">Times it was forgotten.</param>
/// <param name="Suspended">True while a reported Content Bug is open (SU-05).</param>
public sealed record ReviewCardRecord(
    string ItemId,
    string ObjectiveId,
    string ExamDomain,
    string FsrsState,
    DateTimeOffset DueUtc,
    DateTimeOffset IntroducedUtc,
    DateTimeOffset? LastReviewUtc,
    int Reps,
    int Lapses,
    bool Suspended);

/// <summary>Where a review happened.</summary>
public enum ReviewContext
{
    /// <summary>The Stand-up.</summary>
    StandUp,

    /// <summary>A check inside a Quest.</summary>
    Quest,

    /// <summary>A Boss Fight (BF-06).</summary>
    BossFight,
}

/// <summary>One answered review.</summary>
/// <param name="ItemId">The question.</param>
/// <param name="ReviewedUtc">When.</param>
/// <param name="Context">Where.</param>
/// <param name="Correct">Whether the answer was right.</param>
/// <param name="ElapsedMs">How long the answer took.</param>
public sealed record ReviewRecord(string ItemId, DateTimeOffset ReviewedUtc, ReviewContext Context, bool Correct, long ElapsedMs);

/// <summary>Review cards and the review log.</summary>
public interface IReviewCardStore
{
    /// <summary>A card, or null.</summary>
    ReviewCardRecord? Find(string itemId);

    /// <summary>Every card.</summary>
    IReadOnlyList<ReviewCardRecord> All();

    /// <summary>Stores a card.</summary>
    void Save(ReviewCardRecord card);

    /// <summary>Appends a review.</summary>
    void AddReview(ReviewRecord review);

    /// <summary>Every review, oldest first.</summary>
    IReadOnlyList<ReviewRecord> Reviews();

    /// <summary>Suspends or resumes a card (SU-05).</summary>
    void SetSuspended(string itemId, bool suspended);

    /// <summary>Re-keys cards through an outline mapping; null removes the Objective link (E10-06). Returns the cards changed.</summary>
    int RemapObjectives(Func<string, string?> map);
}

/// <summary>The kind of timed or scored attempt.</summary>
public enum AttemptKind
{
    /// <summary>The Diagnostic.</summary>
    Diagnostic,

    /// <summary>A Boss Fight.</summary>
    BossFight,

    /// <summary>A Simulation.</summary>
    Simulation,
}

/// <summary>A Boss Fight's kind (BF-03).</summary>
public enum BossKind
{
    /// <summary>The first attempt for the Domain.</summary>
    First,

    /// <summary>A later attempt.</summary>
    Rematch,
}

/// <summary>A Simulation attempt's status (SIM-03).</summary>
public enum SimulationStatus
{
    /// <summary>The clock is running.</summary>
    InProgress,

    /// <summary>Finished before the deadline.</summary>
    Finished,

    /// <summary>The deadline passed; unanswered items count as incorrect.</summary>
    Expired,
}

/// <summary>An answer saved as it was given (P15, REL-U2-02).</summary>
/// <param name="ItemId">The question.</param>
/// <param name="Answer">The normalized answer.</param>
/// <param name="AnsweredUtc">When.</param>
public sealed record AttemptAnswer(string ItemId, string Answer, DateTimeOffset AnsweredUtc);

/// <summary>A Diagnostic attempt (DX-01).</summary>
/// <param name="Id">The attempt.</param>
/// <param name="StartedUtc">When it started.</param>
/// <param name="TakenUtc">When it finished, or null while open.</param>
/// <param name="ItemIds">Its 40 items.</param>
/// <param name="PerDomain">The per-Domain result as JSON, once finished.</param>
/// <param name="Plan">The study plan as JSON, once finished.</param>
public sealed record DiagnosticRecord(long Id, DateTimeOffset StartedUtc, DateTimeOffset? TakenUtc, IReadOnlyList<string> ItemIds, string? PerDomain, string? Plan);

/// <summary>A Boss Fight attempt (BF-01..06).</summary>
/// <param name="Id">The attempt.</param>
/// <param name="ExamDomain">Its Domain.</param>
/// <param name="Kind">First or rematch.</param>
/// <param name="StartedUtc">When it started.</param>
/// <param name="DeadlineUtc">Start + 45 minutes.</param>
/// <param name="FinishedUtc">When it was scored, or null while open.</param>
/// <param name="ItemIds">Its 30 items.</param>
/// <param name="ScorePercent">The score, once finished.</param>
/// <param name="Passed">Whether it passed, once finished.</param>
public sealed record BossAttemptRecord(
    long Id,
    string ExamDomain,
    BossKind Kind,
    DateTimeOffset StartedUtc,
    DateTimeOffset DeadlineUtc,
    DateTimeOffset? FinishedUtc,
    IReadOnlyList<string> ItemIds,
    int? ScorePercent,
    bool? Passed);

/// <summary>A Simulation attempt (SIM-01..06).</summary>
/// <param name="Id">The attempt.</param>
/// <param name="Form">A or B.</param>
/// <param name="StartedUtc">When it started.</param>
/// <param name="DeadlineUtc">Start + 3 hours.</param>
/// <param name="Status">Its status.</param>
/// <param name="ItemIds">Its 125 items.</param>
/// <param name="ScorePercent">The score, once finished or expired.</param>
/// <param name="SeenForm">True when this form had already been attempted (SIM-05).</param>
public sealed record SimulationAttemptRecord(
    long Id,
    string Form,
    DateTimeOffset StartedUtc,
    DateTimeOffset DeadlineUtc,
    SimulationStatus Status,
    IReadOnlyList<string> ItemIds,
    int? ScorePercent,
    bool SeenForm);

/// <summary>Diagnostic, Boss Fight and Simulation attempts, plus write-ahead answers.</summary>
public interface IAttemptStore
{
    /// <summary>The unfinished Diagnostic, if any.</summary>
    DiagnosticRecord? OpenDiagnostic();

    /// <summary>The most recent finished Diagnostic, if any.</summary>
    DiagnosticRecord? LatestDiagnostic();

    /// <summary>Starts a Diagnostic.</summary>
    long StartDiagnostic(IReadOnlyList<string> itemIds, DateTimeOffset started);

    /// <summary>Finishes a Diagnostic.</summary>
    void FinishDiagnostic(long id, DateTimeOffset taken, string perDomainJson, string planJson);

    /// <summary>The unfinished Boss Fight, if any.</summary>
    BossAttemptRecord? ActiveBoss();

    /// <summary>Boss Fight attempts, oldest first, optionally for one Domain.</summary>
    IReadOnlyList<BossAttemptRecord> BossAttempts(string? examDomain = null);

    /// <summary>
    /// Starts a Boss Fight. Throws <see cref="InvalidOperationException"/> when another Boss Fight or Simulation is
    /// unfinished (P17).
    /// </summary>
    long StartBoss(string examDomain, BossKind kind, DateTimeOffset started, DateTimeOffset deadline, IReadOnlyList<string> itemIds);

    /// <summary>Scores a Boss Fight.</summary>
    void FinishBoss(long id, DateTimeOffset finished, int correct, int total, int scorePercent, bool passed);

    /// <summary>The in-progress Simulation, if any.</summary>
    SimulationAttemptRecord? ActiveSimulation();

    /// <summary>Simulation attempts, oldest first.</summary>
    IReadOnlyList<SimulationAttemptRecord> SimulationAttempts();

    /// <summary>
    /// Starts a Simulation. Throws <see cref="InvalidOperationException"/> when another Boss Fight or Simulation is
    /// unfinished (P17).
    /// </summary>
    long StartSimulation(string form, DateTimeOffset started, DateTimeOffset deadline, IReadOnlyList<string> itemIds, bool seenForm);

    /// <summary>Scores a Simulation as finished or expired.</summary>
    void FinishSimulation(long id, SimulationStatus status, DateTimeOffset finished, int correct, int total, int scorePercent);

    /// <summary>Saves (or replaces) one answer immediately (P15).</summary>
    void SaveAnswer(AttemptKind kind, long attemptId, string itemId, string answer, DateTimeOffset answered);

    /// <summary>The answers saved for an attempt.</summary>
    IReadOnlyList<AttemptAnswer> Answers(AttemptKind kind, long attemptId);
}
