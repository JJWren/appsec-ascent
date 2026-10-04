using System.Text;
using Ascent.Core;
using Ascent.Core.Domain;
using Ascent.Core.Errors;
using Ascent.Core.Platform;
using Ascent.Core.Progress;
using Ascent.Sealing;
using Ascent.Sealing.KeyRelease;

namespace Ascent.Deliverables;

/// <summary>Where a piece of work stands when the Learner runs <c>deliver</c>.</summary>
public enum WorkState
{
    /// <summary>The work file was just created; the Learner fills it in next.</summary>
    Scaffolded,

    /// <summary>The work file has problems, listed (DLE-02).</summary>
    Invalid,

    /// <summary>The work file is valid and ready to self-score and submit.</summary>
    Ready,
}

/// <summary>The result of opening a Deliverable's work.</summary>
/// <param name="State">Where it stands.</param>
/// <param name="Path">The work file.</param>
/// <param name="Problems">For <see cref="WorkState.Invalid"/>: what's missing or wrong.</param>
public sealed record WorkCheck(WorkState State, string Path, IReadOnlyList<string> Problems);

/// <summary>The result of submitting a Deliverable or a drill.</summary>
/// <param name="ScorePercent">The weighted self-score (Deliverables only).</param>
/// <param name="Passed">True at or above the rubric's pass mark.</param>
/// <param name="XpAwarded">XP paid by this submission.</param>
/// <param name="Reference">The reference answer or answer key, sanitized.</param>
public sealed record Submission(int? ScorePercent, bool Passed, int XpAwarded, string Reference);

/// <summary>
/// Deliverables (DLE-01..03) and drills: scaffold the work file, validate it against the template, self-score it
/// against the rubric, then submit it, which pays XP and releases the Sealed reference answer (tier
/// <c>submitted</c>). The XP and the submission record are one transaction (REL-U2-01).
/// </summary>
public sealed class DeliverableService
{
    private const string DrillAnswerMarker = "<!-- Write your answer below this line. -->";

    private readonly DeliverableCatalog catalog;
    private readonly IDeliverableStore store;
    private readonly SealedStore sealedStore;
    private readonly XpLedger ledger;
    private readonly EnginePaths paths;
    private readonly IProgressTransactions transactions;
    private readonly TimeProvider time;

    /// <summary>Creates the service.</summary>
    public DeliverableService(DeliverableCatalog catalog, IDeliverableStore store, SealedStore sealedStore, XpLedger ledger, EnginePaths paths, IProgressTransactions transactions, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(sealedStore);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(transactions);
        ArgumentNullException.ThrowIfNull(time);
        this.catalog = catalog;
        this.store = store;
        this.sealedStore = sealedStore;
        this.ledger = ledger;
        this.paths = paths;
        this.transactions = transactions;
        this.time = time;
    }

    /// <summary>The work file of a Deliverable, under <c>my-work/deliverables/</c> (DLE-01).</summary>
    public string WorkPath(string deliverableId) => SafePath.Resolve(paths.Workspace, "deliverables/" + deliverableId + ".yaml");

    /// <summary>The answer file of a drill, under <c>my-work/drills/</c>.</summary>
    public string DrillPath(string drillId) => SafePath.Resolve(paths.Workspace, "drills/" + drillId + ".md");

    /// <summary>Scaffolds the work file the first time, and validates it afterwards (DLE-01, DLE-02).</summary>
    public WorkCheck Check(string deliverableId)
    {
        var template = catalog.Template(deliverableId);
        var path = WorkPath(deliverableId);
        if (!File.Exists(path))
        {
            Write(path, WorkFile.Scaffold(template));
            return new WorkCheck(WorkState.Scaffolded, path, []);
        }

        var problems = WorkFile.Validate(template, Read(path));
        if (problems.Count == 0)
        {
            var record = Record(deliverableId);
            store.Save(record with { ValidatedUtc = time.GetUtcNow() });
        }

        return new WorkCheck(problems.Count == 0 ? WorkState.Ready : WorkState.Invalid, path, problems);
    }

    /// <summary>
    /// Self-scores and submits a valid Deliverable (DLE-03): 40 XP, plus 10 at or above the pass mark, then the
    /// reference answer is released. <paramref name="levels"/> holds the chosen score for each rubric criterion.
    /// </summary>
    public Submission Submit(string deliverableId, IReadOnlyDictionary<string, int> levels)
    {
        ArgumentNullException.ThrowIfNull(levels);
        var check = Check(deliverableId);
        if (check.State != WorkState.Ready)
        {
            throw new AscentException("The work file isn't ready to submit.", "Run 'ascent deliver " + deliverableId + "' to see what's missing.");
        }

        var template = catalog.Template(deliverableId);
        var rubric = catalog.RubricFor(template);
        var score = Score(rubric, levels);
        var passed = score >= rubric.PassThreshold;
        var xp = transactions.Run(() =>
        {
            var now = time.GetUtcNow();
            var record = Record(deliverableId);
            store.Save(record with { SelfScorePercent = score, SubmittedUtc = record.SubmittedUtc ?? now });
            var awarded = ledger.Award(XpKind.Deliverable, deliverableId) ? XpAwards.Points(XpKind.Deliverable) : 0;
            if (passed && ledger.Award(XpKind.DeliverablePass, deliverableId))
            {
                awarded += XpAwards.Points(XpKind.DeliverablePass);
            }

            return awarded;
        });

        string reference;
        using (var item = sealedStore.Open(template.ReferenceRef, ReleaseContext.WorkSubmitted(deliverableId)))
        {
            reference = SafeText.Sanitize(item.Text(), 200_000);
        }

        var released = Record(deliverableId);
        store.Save(released with { ReferenceReleasedUtc = released.ReferenceReleasedUtc ?? time.GetUtcNow() });
        return new Submission(score, passed, xp, reference);
    }

    /// <summary>The weighted self-score in percent: each criterion's level ÷ its top level × its weight.</summary>
    public static int Score(Rubric rubric, IReadOnlyDictionary<string, int> levels)
    {
        ArgumentNullException.ThrowIfNull(rubric);
        ArgumentNullException.ThrowIfNull(levels);
        var totalWeight = rubric.Criteria.Sum(c => c.Weight);
        var earned = 0.0;
        foreach (var criterion in rubric.Criteria)
        {
            var top = criterion.Levels.Count == 0 ? 0 : criterion.Levels.Max(l => l.Score);
            var chosen = levels.TryGetValue(criterion.Key, out var level) ? level : throw new ArgumentException("Every criterion needs a level.", nameof(levels));
            if (!criterion.Levels.Any(l => l.Score == chosen))
            {
                throw new ArgumentException("A level must be one of the criterion's levels.", nameof(levels));
            }

            earned += top == 0 ? 0 : criterion.Weight * (double)chosen / top;
        }

        return totalWeight == 0 ? 0 : (int)Math.Round(100 * earned / totalWeight, MidpointRounding.AwayFromZero);
    }

    /// <summary>The drill's answer file, created with its prompt the first time. Returns true when it was just created.</summary>
    public bool OpenDrill(string drillId, out string path)
    {
        var drill = catalog.Drill(drillId);
        path = DrillPath(drillId);
        if (File.Exists(path))
        {
            return false;
        }

        Write(path, "# Drill " + drill.Id + " (Objective " + drill.ObjectiveId + ")\n\n" + string.Join('\n', drill.Prompt.ReplaceLineEndings("\n").Split('\n').Select(l => "> " + l)) + "\n\n" + DrillAnswerMarker + "\n\n");
        return true;
    }

    /// <summary>Submits a drill once its answer file has an answer: 15 XP once, then the answer key is released.</summary>
    public Submission SubmitDrill(string drillId)
    {
        var drill = catalog.Drill(drillId);
        var path = DrillPath(drillId);
        var text = File.Exists(path) ? Read(path) : string.Empty;
        var marker = text.IndexOf(DrillAnswerMarker, StringComparison.Ordinal);
        var answer = marker < 0 ? text : text[(marker + DrillAnswerMarker.Length)..];
        if (string.IsNullOrWhiteSpace(answer))
        {
            throw new AscentException("The drill has no answer yet.", "Write it below the marker in " + path + ", then run 'ascent deliver " + drillId + "' again.");
        }

        var xp = ledger.Award(XpKind.Drill, drillId) ? XpAwards.Points(XpKind.Drill) : 0;
        using var item = sealedStore.Open(drill.AnswerRef, ReleaseContext.WorkSubmitted(drillId));
        return new Submission(null, true, xp, SafeText.Sanitize(item.Text(), 200_000));
    }

    private DeliverableRecord Record(string deliverableId) =>
        store.Find(deliverableId) ?? new DeliverableRecord(deliverableId, Path.GetRelativePath(paths.RepoRoot, WorkPath(deliverableId)).Replace('\\', '/'), null, null, null, null, false);

    private static string Read(string path)
    {
        var info = new FileInfo(path);
        return info.Length <= WorkFile.MaxBytes
            ? File.ReadAllText(path)
            : throw new UsageException("'" + path + "' is larger than 256 KB.", "Keep the work file to the template's sections.");
    }

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
