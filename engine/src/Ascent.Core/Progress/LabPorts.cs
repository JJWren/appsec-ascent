using Ascent.Core.Domain;

namespace Ascent.Core.Progress;

/// <summary>A Lab's progress (table <c>lab_state</c>; the Flag's salt and hash go through <see cref="ILabFlags"/>).</summary>
/// <param name="LabId">The Lab.</param>
/// <param name="Stage">Its stage (LABE-01).</param>
/// <param name="StartedUtc">The first <c>lab up</c>.</param>
/// <param name="FlagCapturedUtc">When the Flag was verified.</param>
/// <param name="FixedUtc">When <c>verify</c> passed.</param>
/// <param name="ExplainedUtc">When the Teach-back was saved.</param>
/// <param name="VerifyAttempts">How many times <c>verify</c> ran.</param>
public sealed record LabStateRecord(
    string LabId,
    LabStage Stage,
    DateTimeOffset? StartedUtc,
    DateTimeOffset? FlagCapturedUtc,
    DateTimeOffset? FixedUtc,
    DateTimeOffset? ExplainedUtc,
    int VerifyAttempts);

/// <summary>Lab progress.</summary>
public interface ILabStateStore
{
    /// <summary>A Lab's progress, or null before its first <c>lab up</c>.</summary>
    LabStateRecord? Find(string labId);

    /// <summary>Every Lab with progress.</summary>
    IReadOnlyList<LabStateRecord> All();

    /// <summary>Moves a Lab to <paramref name="stage"/> and stamps that stage's time.</summary>
    void MoveTo(string labId, LabStage stage, DateTimeOffset at);

    /// <summary>Counts one <c>verify</c> run.</summary>
    void CountVerify(string labId);
}

/// <summary>How a Cloud Stage deployment ended (CLD-04, CLD-05).</summary>
public enum CloudOutcome
{
    /// <summary>Not decided yet, or torn down without a bonus or penalty.</summary>
    None,

    /// <summary>Torn down before <c>expires-on</c>: the Teardown Bonus.</summary>
    Bonus,

    /// <summary>Found after <c>expires-on</c>: the overrun penalty.</summary>
    Penalty,
}

/// <summary>A Cloud Stage deployment (table <c>cloud_deployments</c>).</summary>
/// <param name="Id">The deployment.</param>
/// <param name="LabId">The Lab.</param>
/// <param name="ResourceGroup">The tagged resource group.</param>
/// <param name="DeployedUtc">When it was deployed.</param>
/// <param name="ExpiresUtc">The <c>expires-on</c> tag (CLD-03).</param>
/// <param name="TornDownUtc">When it was confirmed gone.</param>
/// <param name="Outcome">Bonus, penalty or neither.</param>
public sealed record CloudDeploymentRecord(
    long Id,
    string LabId,
    string ResourceGroup,
    DateTimeOffset DeployedUtc,
    DateTimeOffset ExpiresUtc,
    DateTimeOffset? TornDownUtc,
    CloudOutcome Outcome);

/// <summary>Cloud Stage deployments.</summary>
public interface ICloudDeploymentStore
{
    /// <summary>Records a deployment and returns its ID.</summary>
    long Add(string labId, string resourceGroup, DateTimeOffset deployedUtc, DateTimeOffset expiresUtc);

    /// <summary>Deployments not yet confirmed gone, oldest first.</summary>
    IReadOnlyList<CloudDeploymentRecord> Open();

    /// <summary>Records the outcome of a deployment.</summary>
    void SetOutcome(long id, CloudOutcome outcome);

    /// <summary>Records that a deployment's resource group is gone.</summary>
    void Close(long id, DateTimeOffset tornDownUtc);
}

/// <summary>A Release that was unlocked or skipped into (table <c>releases</c>).</summary>
/// <param name="ExamDomain">The Domain the Release belongs to: D1–D8, or CAP for the Capstone.</param>
/// <param name="UnlockedUtc">When.</param>
/// <param name="Skipped">True when the previous Domain was skipped with typed confirmation (REL-02).</param>
public sealed record ReleaseRecord(string ExamDomain, DateTimeOffset UnlockedUtc, bool Skipped);

/// <summary>Unlocked Releases.</summary>
public interface IReleaseStore
{
    /// <summary>Every unlocked Release, oldest first.</summary>
    IReadOnlyList<ReleaseRecord> All();

    /// <summary>Records an unlock; a repeat is ignored.</summary>
    void Unlock(string examDomain, DateTimeOffset unlockedUtc, bool skipped);
}

/// <summary>A Deliverable's progress (table <c>deliverables</c>).</summary>
/// <param name="DeliverableId">The Deliverable.</param>
/// <param name="WorkPath">The work file, relative to the repository (DLE-01).</param>
/// <param name="ValidatedUtc">When it last passed validation.</param>
/// <param name="SelfScorePercent">The weighted self-score.</param>
/// <param name="SubmittedUtc">When it was submitted.</param>
/// <param name="ReferenceReleasedUtc">When the reference answer was released.</param>
/// <param name="Published">True once copied into the portfolio (PORT-03).</param>
public sealed record DeliverableRecord(
    string DeliverableId,
    string WorkPath,
    DateTimeOffset? ValidatedUtc,
    int? SelfScorePercent,
    DateTimeOffset? SubmittedUtc,
    DateTimeOffset? ReferenceReleasedUtc,
    bool Published);

/// <summary>Deliverable progress.</summary>
public interface IDeliverableStore
{
    /// <summary>A Deliverable's progress, or null.</summary>
    DeliverableRecord? Find(string deliverableId);

    /// <summary>Every Deliverable with progress.</summary>
    IReadOnlyList<DeliverableRecord> All();

    /// <summary>Stores a Deliverable's progress.</summary>
    void Save(DeliverableRecord record);
}

/// <summary>A confirmed Content Bug (table <c>content_bugs</c>).</summary>
/// <param name="IssueNumber">The GitHub issue.</param>
/// <param name="ItemId">The item it's about.</param>
/// <param name="ConfirmedUtc">When <c>sync</c> first saw it confirmed.</param>
public sealed record ContentBugRecord(int IssueNumber, string ItemId, DateTimeOffset ConfirmedUtc);

/// <summary>Confirmed Content Bugs.</summary>
public interface IContentBugStore
{
    /// <summary>Records a confirmed bug; returns false when it was already recorded.</summary>
    bool Add(ContentBugRecord record);

    /// <summary>Every confirmed bug.</summary>
    IReadOnlyList<ContentBugRecord> All();
}
