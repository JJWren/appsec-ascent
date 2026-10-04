using System.Globalization;
using Ascent.Core.Domain;
using Ascent.Core.Progress;
using Ascent.Sealing.Bundles;

namespace Ascent.Sealing.KeyRelease;

/// <summary>What the caller claims justifies opening a Sealed item; the policy checks the claim against progress.</summary>
/// <param name="Reason">The release reason.</param>
/// <param name="Subject">The Lab, piece of work, serving context, attempt or Domain the claim is about.</param>
/// <param name="Serving">For <see cref="ReleaseReason.Served"/>: where the question is served.</param>
/// <param name="AttemptId">For <see cref="ReleaseReason.SimulationStarted"/>: the active attempt.</param>
public sealed record ReleaseContext(ReleaseReason Reason, string Subject, ServeContext? Serving = null, long? AttemptId = null)
{
    /// <summary><c>lab up</c> for <paramref name="labId"/>.</summary>
    public static ReleaseContext LabStarted(string labId) => new(ReleaseReason.LabStarted, labId);

    /// <summary>The Flag for <paramref name="labId"/> was verified.</summary>
    public static ReleaseContext FlagVerified(string labId) => new(ReleaseReason.FlagVerified, labId);

    /// <summary>The Learner submitted <paramref name="workId"/> (a Deliverable, drill or Deep Dive).</summary>
    public static ReleaseContext WorkSubmitted(string workId) => new(ReleaseReason.DeliverableSubmitted, workId);

    /// <summary>A question is served in <paramref name="context"/>.</summary>
    public static ReleaseContext Served(ServeContext context) => new(ReleaseReason.Served, context.ToString(), context);

    /// <summary>A Simulation attempt is active.</summary>
    public static ReleaseContext Simulation(long attemptId) =>
        new(ReleaseReason.SimulationStarted, attemptId.ToString(CultureInfo.InvariantCulture), AttemptId: attemptId);

    /// <summary>The Release after <paramref name="examDomain"/> was unlocked or skipped.</summary>
    public static ReleaseContext ReleaseUnlocked(string examDomain) => new(ReleaseReason.ReleaseUnlocked, examDomain);
}

/// <summary>Enforces the tier release conditions (SEAL-03). Each tier opens only for its own kind of claim.</summary>
public sealed class KeyReleasePolicy(IReleaseFacts facts)
{
    /// <summary>The item ID of the Release that follows a Domain: <c>D3</c> gives <c>release-3</c>; any other code is kept, so <c>CAP</c> gives <c>release-CAP</c>.</summary>
    public static string ReleaseItemFor(string examDomain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(examDomain);
        return "release-" + (examDomain.StartsWith('D') ? examDomain[1..] : examDomain);
    }

    /// <summary>Returns why the claim doesn't release the item, or null when it does.</summary>
    public string? Check(BundleHeader header, ReleaseContext context)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(context);
        return (header.Tier, context.Reason) switch
        {
            (SealTier.Start, ReleaseReason.LabStarted) =>
                !facts.RulesAccepted ? "the rules of engagement haven't been accepted"
                : !Owns(header.ItemId, context.Subject) ? "the item belongs to another Lab"
                : facts.LabStage(context.Subject) < LabStage.Started ? "the Lab hasn't been started"
                : null,
            (SealTier.Earned, ReleaseReason.FlagVerified) =>
                !Owns(header.ItemId, context.Subject) ? "the item belongs to another Lab"
                : facts.LabStage(context.Subject) < LabStage.FlagCaptured ? "the Lab's Flag hasn't been verified"
                : null,
            (SealTier.Submitted, ReleaseReason.DeliverableSubmitted) =>
                !Owns(header.ItemId, context.Subject) ? "the item belongs to other work"
                : !facts.WorkSubmitted(context.Subject) ? "the work hasn't been submitted"
                : null,
            (SealTier.Practice, ReleaseReason.Served) => ServedPoolProblem(header.Pool, context.Serving),
            (SealTier.Simulation, ReleaseReason.SimulationStarted) =>
                context.AttemptId is { } attempt && facts.SimulationIncludes(attempt, header.ItemId) ? null : "the item isn't part of an active Simulation",
            (SealTier.Release, ReleaseReason.ReleaseUnlocked) =>
                header.ItemId != ReleaseItemFor(context.Subject) ? "the item isn't the Release for that Domain"
                : !facts.ReleaseUnlocked(context.Subject) ? "the Release hasn't been unlocked"
                : null,
            _ => "this kind of claim doesn't release the " + SealTiers.Name(header.Tier) + " tier",
        };
    }

    private static bool Owns(string itemId, string subject) => itemId.StartsWith(subject + ".", StringComparison.Ordinal);

    // The Diagnostic serves only the diagnostic pool; every other context serves only the practice pool (SU-02, DX-01).
    private static string? ServedPoolProblem(string? pool, ServeContext? serving) => (serving, pool) switch
    {
        (null, _) => "the serving context is missing",
        (ServeContext.Diagnostic, "diagnostic") => null,
        (ServeContext.Diagnostic, _) => "the Diagnostic serves only diagnostic-pool items",
        (_, "practice") => null,
        _ => "only practice-pool items are served here",
    };
}
