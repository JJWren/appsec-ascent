namespace Ascent.Core.Domain;

/// <summary>Key-release tiers of Sealed items (SEAL-03, ADR 0005).</summary>
public enum SealTier
{
    /// <summary>Lab Module code and the Flag-plant spec: released by <c>lab up</c>.</summary>
    Start,

    /// <summary>Lab security tests, fixes and reference notes: released once the Flag is verified.</summary>
    Earned,

    /// <summary>Reference answers: released once the Learner's own work is submitted.</summary>
    Submitted,

    /// <summary>Question Bank items: released when served in a Stand-up, Quest check, Boss Fight or the Diagnostic.</summary>
    Practice,

    /// <summary>The Simulation pool: released only inside an active Simulation attempt.</summary>
    Simulation,

    /// <summary>Releases after the first: released when the previous Domain is unlocked or skipped.</summary>
    Release,
}

/// <summary>Why a key was released; recorded once per item and tier (SEAL-05).</summary>
public enum ReleaseReason
{
    /// <summary><c>lab up</c> with the rules of engagement accepted.</summary>
    LabStarted,

    /// <summary>The Lab's Flag was verified.</summary>
    FlagVerified,

    /// <summary>The Learner's Deliverable (or drill or Deep Dive) was submitted.</summary>
    DeliverableSubmitted,

    /// <summary>A question was served.</summary>
    Served,

    /// <summary>A Simulation attempt is active.</summary>
    SimulationStarted,

    /// <summary>A Release was unlocked or skipped.</summary>
    ReleaseUnlocked,
}

/// <summary>Where a question is being served (SU-02, DX-01, BF-01).</summary>
public enum ServeContext
{
    /// <summary>The daily Stand-up.</summary>
    StandUp,

    /// <summary>A check inside a Quest.</summary>
    Quest,

    /// <summary>A Boss Fight.</summary>
    BossFight,

    /// <summary>The Diagnostic.</summary>
    Diagnostic,
}

/// <summary>The Lab lifecycle (LABE-01).</summary>
public enum LabStage
{
    /// <summary>Not started.</summary>
    NotStarted,

    /// <summary>Started with <c>lab up</c>.</summary>
    Started,

    /// <summary>The Flag was captured (Red).</summary>
    FlagCaptured,

    /// <summary>The fix passes the security tests (Blue).</summary>
    Fixed,

    /// <summary>The Teach-back is written (Explain).</summary>
    Explained,
}

/// <summary>Text names for tiers, as they appear in bundle headers and the database.</summary>
public static class SealTiers
{
    /// <summary>The header name of a tier, such as <c>earned</c>.</summary>
    public static string Name(SealTier tier) => tier switch
    {
        SealTier.Start => "start",
        SealTier.Earned => "earned",
        SealTier.Submitted => "submitted",
        SealTier.Practice => "practice",
        SealTier.Simulation => "simulation",
        SealTier.Release => "release",
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown tier."),
    };

    /// <summary>Parses a header name; returns false for anything else.</summary>
    public static bool TryParse(string? name, out SealTier tier)
    {
        foreach (var candidate in Enum.GetValues<SealTier>())
        {
            if (string.Equals(Name(candidate), name, StringComparison.Ordinal))
            {
                tier = candidate;
                return true;
            }
        }

        tier = default;
        return false;
    }
}
