using Ascent.Core.Domain;

namespace Ascent.Core.Progress;

/// <summary>A Lab's stored Flag: a salt and SHA-256(salt ‖ Flag), never the Flag itself (FLAG-02).</summary>
/// <param name="Salt">16 random bytes.</param>
/// <param name="Hash">SHA-256 of the salt followed by the Flag's UTF-8 bytes.</param>
/// <param name="WrongAttempts">The most recent wrong submissions, oldest first (FLAG-03).</param>
public sealed record LabFlagRecord(byte[] Salt, byte[] Hash, IReadOnlyList<DateTimeOffset> WrongAttempts);

/// <summary>Stores Lab Flags and wrong-attempt times.</summary>
public interface ILabFlags
{
    /// <summary>The Lab's current Flag record, or null when no Flag has been planted.</summary>
    LabFlagRecord? Find(string labId);

    /// <summary>Replaces the Lab's Flag and clears its wrong attempts (FLAG-01).</summary>
    void SetFlag(string labId, byte[] salt, byte[] hash);

    /// <summary>Stores the recent wrong-attempt times.</summary>
    void SetWrongAttempts(string labId, IReadOnlyList<DateTimeOffset> attempts);
}

/// <summary>Records key releases, once per item and tier (SEAL-05).</summary>
public interface IKeyReleaseLog
{
    /// <summary>Records a release; a repeat for the same item and tier is ignored.</summary>
    void Record(string itemId, SealTier tier, ReleaseReason reason, DateTimeOffset releasedAt);

    /// <summary>True once the item's tier has been released.</summary>
    bool IsReleased(string itemId, SealTier tier);
}

/// <summary>The progress facts the key-release policy checks (SEAL-03).</summary>
public interface IReleaseFacts
{
    /// <summary>True once the Learner accepted the rules of engagement (E1-04).</summary>
    bool RulesAccepted { get; }

    /// <summary>The Lab's lifecycle stage.</summary>
    LabStage LabStage(string labId);

    /// <summary>True once the Learner submitted the work (a Deliverable, a drill or a Deep Dive).</summary>
    bool WorkSubmitted(string workId);

    /// <summary>True when the Simulation attempt is in progress and includes the item.</summary>
    bool SimulationIncludes(long attemptId, string itemId);

    /// <summary>True once the Release after <paramref name="examDomain"/> was unlocked or skipped.</summary>
    bool ReleaseUnlocked(string examDomain);
}
