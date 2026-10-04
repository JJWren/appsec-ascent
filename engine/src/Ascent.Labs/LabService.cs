using System.Globalization;
using Ascent.Core.Curriculum;
using Ascent.Core.Domain;
using Ascent.Core.Errors;
using Ascent.Core.Platform;
using Ascent.Core.Progress;
using Ascent.Sealing;
using Ascent.Sealing.Flags;
using Ascent.Sealing.KeyRelease;

namespace Ascent.Labs;

/// <summary>What <c>lab up</c> did.</summary>
/// <param name="Lab">The Lab.</param>
/// <param name="FirstStart">True when this was the Lab's first start, so its module was unpacked.</param>
/// <param name="ModuleFiles">Files unpacked from the Lab Module.</param>
/// <param name="Stage">The Local Stage's report.</param>
public sealed record LabUpResult(LabInfo Lab, bool FirstStart, int ModuleFiles, StageResult Stage);

/// <summary>What <c>verify</c> found.</summary>
/// <param name="Run">The test run.</param>
/// <param name="NowFixed">True when this run moved the Lab to Fixed and paid Blue XP.</param>
public sealed record VerifyOutcome(TestRun Run, bool NowFixed);

/// <summary>Everything the Lab service works with, so its constructor stays readable.</summary>
/// <param name="Catalog">The Curriculum.</param>
/// <param name="States">Lab progress.</param>
/// <param name="Flags">Flag generation and checking.</param>
/// <param name="Sealed">The verify-then-decrypt pipeline.</param>
/// <param name="Workspace">The Learner workspace.</param>
/// <param name="Orchestrator">The Local Stage.</param>
/// <param name="Planter">Flag planting.</param>
/// <param name="Runner">The security-test runner.</param>
/// <param name="Ledger">XP.</param>
/// <param name="Facts">Release facts (the rules of engagement).</param>
/// <param name="Releases">Unlocked Releases.</param>
/// <param name="Transactions">One transaction per multi-step change.</param>
/// <param name="Time">The clock.</param>
public sealed record LabDependencies(
    CurriculumCatalog Catalog,
    ILabStateStore States,
    FlagService Flags,
    SealedStore Sealed,
    Workspace Workspace,
    IOrchestrator Orchestrator,
    Planter Planter,
    VerifyRunner Runner,
    XpLedger Ledger,
    IReleaseFacts Facts,
    IReleaseStore Releases,
    IProgressTransactions Transactions,
    TimeProvider Time);

/// <summary>
/// The Lab lifecycle: Red → Blue → Explain (LABE-01..05, FLAG-01..04). Each stage change and its XP are one
/// transaction (REL-U2-01), and each step releases only its own tier (SEAL-03).
/// </summary>
public sealed class LabService
{
    private readonly LabDependencies d;
    private readonly bool windows;

    /// <summary>Creates the service.</summary>
    /// <param name="dependencies">What it works with.</param>
    /// <param name="windows">True on Windows, where Windows-only Labs can run.</param>
    public LabService(LabDependencies dependencies, bool windows)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        d = dependencies;
        this.windows = windows;
    }

    /// <summary>The workspace's current Release.</summary>
    public int CurrentRelease => ReleaseLadder.Current(d.Releases.All());

    /// <summary>Finds a Lab, or throws a usage error naming it.</summary>
    public LabInfo Find(string labId) =>
        d.Catalog.Labs.FirstOrDefault(l => string.Equals(l.Id, labId, StringComparison.Ordinal))
            ?? throw new UsageException("There is no Lab called '" + SafeText.Sanitize(labId, 60) + "'.", "Open a Quest to see its Labs: ascent next");

    /// <summary>A Lab's stage.</summary>
    public LabStage Stage(string labId) => d.States.Find(labId)?.Stage ?? LabStage.NotStarted;

    /// <summary>
    /// <c>lab up</c>: on the first start, unpacks the Lab Module into the workspace; every time, plants a fresh Flag
    /// (FLAG-01) and brings the Local Stage up. A started Lab keeps its stage (LABE-04).
    /// </summary>
    public async Task<LabUpResult> UpAsync(string labId, CancellationToken cancellationToken)
    {
        var lab = Find(labId);
        EnsureRunnable(lab);
        d.Workspace.EnsureCreated();
        var first = Stage(labId) == LabStage.NotStarted;
        var (plant, flag, files) = d.Transactions.Run(() =>
        {
            if (first)
            {
                d.States.MoveTo(labId, LabStage.Started, d.Time.GetUtcNow());
            }

            var extracted = 0;
            if (first)
            {
                using var module = d.Sealed.Open(lab.ModuleRef, ReleaseContext.LabStarted(labId));
                extracted = d.Workspace.ExtractModule(module);
            }

            using var spec = d.Sealed.Open(lab.PlantRef, ReleaseContext.LabStarted(labId));
            return (PlantSpec.Parse(spec.Text()), d.Flags.Generate(labId), extracted);
        });

        var stage = await d.Orchestrator.UpAsync(new StageRequest(lab, d.Workspace.Root, d.Workspace.Throughline), cancellationToken);
        await d.Planter.PlantAsync(plant, flag, d.Workspace.Root, cancellationToken);
        return new LabUpResult(lab, first, files, stage);
    }

    /// <summary><c>lab down</c>: stops the Lab's Local Stage.</summary>
    public Task<StageResult> DownAsync(string labId, CancellationToken cancellationToken)
    {
        var lab = Find(labId);
        return d.Orchestrator.DownAsync(new StageRequest(lab, d.Workspace.Root, d.Workspace.Throughline), cancellationToken);
    }

    /// <summary><c>lab reset</c>: unpacks the Lab Module again, overwriting only its files; the stage is kept (LABE-04).</summary>
    public int Reset(string labId)
    {
        var lab = Find(labId);
        EnsureRunnable(lab);
        if (Stage(labId) == LabStage.NotStarted)
        {
            throw new UsageException("This Lab hasn't started, so there's nothing to reset.", "Run 'ascent lab up " + labId + "'.");
        }

        using var module = d.Sealed.Open(lab.ModuleRef, ReleaseContext.LabStarted(labId));
        return d.Workspace.ExtractModule(module);
    }

    /// <summary><c>flag</c>: checks a captured Flag; a match moves the Lab to FlagCaptured and pays Red XP (FLAG-02, FLAG-03).</summary>
    public FlagCheck SubmitFlag(string labId, string candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var lab = Find(labId);
        var stage = Stage(labId);
        if (stage == LabStage.NotStarted)
        {
            throw new AscentException("This Lab hasn't started.", "Run 'ascent lab up " + labId + "'.");
        }

        if (stage >= LabStage.FlagCaptured)
        {
            throw new UsageException("You've already captured this Lab's Flag.", "Fix the vulnerability, then run 'ascent verify " + labId + "'.");
        }

        var check = d.Flags.Verify(labId, candidate);
        if (check.Outcome == FlagOutcome.Accepted)
        {
            d.Transactions.Run(() =>
            {
                d.States.MoveTo(labId, LabStage.FlagCaptured, d.Time.GetUtcNow());
                d.Ledger.Award(XpKind.LabRed, labId, lab.Red);
            });
        }

        return check;
    }

    /// <summary>
    /// <c>verify</c>: available from FlagCaptured, because the tests are tier <c>earned</c> (LABE-02). When every test
    /// passes, the Lab moves to Fixed and pays Blue XP once.
    /// </summary>
    public async Task<VerifyOutcome> VerifyAsync(string labId, CancellationToken cancellationToken)
    {
        var lab = Find(labId);
        var stage = Stage(labId);
        if (stage < LabStage.FlagCaptured)
        {
            throw new AscentException("verify unlocks once you've captured this Lab's Flag.", "Exploit the vulnerability, then run 'ascent flag " + labId + "'.");
        }

        d.States.CountVerify(labId);
        var run = await d.Runner.RunAsync(lab, d.Workspace.Throughline, cancellationToken);
        if (!run.AllPassed || stage != LabStage.FlagCaptured)
        {
            return new VerifyOutcome(run, false);
        }

        d.Transactions.Run(() =>
        {
            d.States.MoveTo(labId, LabStage.Fixed, d.Time.GetUtcNow());
            d.Ledger.Award(XpKind.LabBlue, labId, lab.Blue);
        });
        return new VerifyOutcome(run, true);
    }

    /// <summary>
    /// The Explain step: a Teach-back of 30–150 words once the fix passes. It moves the Lab to Explained and pays
    /// Explain XP once (LABE-03). Returns the Teach-back's file.
    /// </summary>
    public string Explain(string labId, string text, TeachBackService teachBacks)
    {
        ArgumentNullException.ThrowIfNull(teachBacks);
        var lab = Find(labId);
        var stage = Stage(labId);
        if (stage < LabStage.Fixed)
        {
            throw new AscentException("The Explain step comes after your fix passes.", "Run 'ascent verify " + labId + "'.");
        }

        var path = teachBacks.Save(labId, text, forLab: true);
        if (stage == LabStage.Fixed)
        {
            d.Transactions.Run(() =>
            {
                d.States.MoveTo(labId, LabStage.Explained, d.Time.GetUtcNow());
                d.Ledger.Award(XpKind.LabExplain, labId, lab.Explain);
            });
        }

        return path;
    }

    private void EnsureRunnable(LabInfo lab)
    {
        if (!d.Facts.RulesAccepted)
        {
            throw new AscentException("Accept the rules of engagement before any Lab.", "Run 'ascent rules'.");
        }

        if (lab.WindowsOnly && !windows)
        {
            throw new AscentException(
                "This Lab needs Windows. It's a bonus Lab, so skipping it never blocks a Quest or a Rank.",
                "Carry on with 'ascent next'.");
        }

        var current = CurrentRelease;
        if (lab.Release != current)
        {
            throw new UsageException(
                string.Create(CultureInfo.InvariantCulture, $"This Lab runs on Release {lab.Release}, and your workspace is on Release {current}."),
                lab.Release > current
                    ? "Finish the current Domain, then move on with 'ascent release next'."
                    : "Labs from earlier Releases that you skipped are listed under Season 2 in 'ascent status'.");
        }
    }
}
