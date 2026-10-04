using Ascent.Core.Domain;

namespace Ascent.Core.Progress;

/// <summary>
/// Runs several progress changes as one transaction (REL-U2-01), such as an XP award together with the state change
/// that earned it. Nested calls join the outer transaction.
/// </summary>
public interface IProgressTransactions
{
    /// <summary>Runs <paramref name="action"/> in one transaction.</summary>
    void Run(Action action);

    /// <summary>Runs <paramref name="action"/> in one transaction and returns its result.</summary>
    T Run<T>(Func<T> action);
}

/// <summary>The Learner profile's key/value settings.</summary>
public interface IProfileStore
{
    /// <summary>Every stored setting.</summary>
    IReadOnlyDictionary<string, string> All();

    /// <summary>Stores a setting.</summary>
    void Write(string key, string value);

    /// <summary>Removes a setting.</summary>
    void Remove(string key);
}

/// <summary>One XP ledger entry (XP-01).</summary>
/// <param name="OccurredUtc">When it was earned.</param>
/// <param name="Kind">What earned it.</param>
/// <param name="RefId">The Quest, Lab, date, week, Domain or issue it was earned for.</param>
/// <param name="Points">Points; negative for penalties.</param>
/// <param name="Bonus">True for bonus-only sources, which are excluded from <c>CoreXpMax</c> (XP-02).</param>
public sealed record XpEvent(DateTimeOffset OccurredUtc, XpKind Kind, string RefId, int Points, bool Bonus);

/// <summary>The XP ledger. (<c>kind</c>, <c>refId</c>) is unique, so awards are idempotent (REL-U2-04).</summary>
public interface IXpStore
{
    /// <summary>Adds an event; returns false when the same kind and reference already exist.</summary>
    bool TryAdd(XpEvent xpEvent);

    /// <summary>True when an event of that kind and reference exists.</summary>
    bool Has(XpKind kind, string refId);

    /// <summary>Every event, oldest first.</summary>
    IReadOnlyList<XpEvent> All();
}

/// <summary>Ranks reached; they never go down (RNK-03).</summary>
public interface IRankStore
{
    /// <summary>The highest Rank reached, or null.</summary>
    Rank? Highest();

    /// <summary>Records reaching a Rank.</summary>
    void Record(Rank rank, DateTimeOffset reachedUtc);
}

/// <summary>Completed Stand-up days (WG-01).</summary>
public interface IStandUpStore
{
    /// <summary>Records a completed Stand-up for a local date.</summary>
    void Record(DateOnly localDate, DateTimeOffset completedUtc, int itemsReviewed);

    /// <summary>The local dates with a completed Stand-up in a range (inclusive).</summary>
    IReadOnlyList<DateOnly> Between(DateOnly first, DateOnly last);
}

/// <summary>A Quest's progress.</summary>
/// <param name="QuestId">The Quest.</param>
/// <param name="Status">NotStarted, InProgress or Complete.</param>
/// <param name="LessonCompletedUtc">When the lesson and its Teach-back were done.</param>
/// <param name="CompletedUtc">When every non-bonus activity was done.</param>
public sealed record QuestProgressRecord(string QuestId, QuestStatus Status, DateTimeOffset? LessonCompletedUtc, DateTimeOffset? CompletedUtc);

/// <summary>Quest progress.</summary>
public interface IQuestProgressStore
{
    /// <summary>The Quest's progress, or null when it hasn't been opened.</summary>
    QuestProgressRecord? Find(string questId);

    /// <summary>Every Quest's progress.</summary>
    IReadOnlyList<QuestProgressRecord> All();

    /// <summary>Stores a Quest's progress.</summary>
    void Save(QuestProgressRecord record);
}

/// <summary>Saved Teach-backs.</summary>
public interface ITeachBackStore
{
    /// <summary>Records a Teach-back.</summary>
    void Save(string refId, string path, int wordCount, DateTimeOffset savedUtc);

    /// <summary>True when a Teach-back exists for the reference.</summary>
    bool Exists(string refId);
}

/// <summary>Items rolled into Season 2 (S2-01).</summary>
public interface ISeason2Store
{
    /// <summary>Adds an item; a repeat is ignored.</summary>
    void Add(string itemId, Season2Reason reason, DateTimeOffset addedUtc);

    /// <summary>Every Season 2 item.</summary>
    IReadOnlyList<(string ItemId, Season2Reason Reason)> All();
}

/// <summary>The outline version that progress is keyed to (E10-06).</summary>
public interface IOutlineVersionStore
{
    /// <summary>The recorded version, or null before the first check.</summary>
    string? Recorded { get; }

    /// <summary>Records the version progress is now keyed to.</summary>
    void Record(string version);
}

/// <summary>Facts about activities that decide Quest completion (E2-01).</summary>
public interface IActivityFacts
{
    /// <summary>The Lab's lifecycle stage.</summary>
    LabStage LabStage(string labId);

    /// <summary>True once the drill was done.</summary>
    bool DrillDone(string drillId);

    /// <summary>True once the Deliverable was submitted.</summary>
    bool DeliverableSubmitted(string deliverableId);
}
