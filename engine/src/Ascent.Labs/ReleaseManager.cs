using System.Globalization;
using Ascent.Core.Curriculum;
using Ascent.Core.Domain;
using Ascent.Core.Errors;
using Ascent.Core.Progress;
using Ascent.Sealing;
using Ascent.Sealing.KeyRelease;

namespace Ascent.Labs;

/// <summary>Whether the next Release can unlock (REL-01).</summary>
/// <param name="Current">The current Release.</param>
/// <param name="Next">The next Release.</param>
/// <param name="FromDomain">The Domain being finished: ORI, D1–D8.</param>
/// <param name="NextDomain">The Domain the next Release belongs to: D1–D8 or CAP.</param>
/// <param name="OpenQuests">The finishing Domain's Quests that aren't complete.</param>
/// <param name="BossRequired">True when the finishing Domain has a Boss Fight.</param>
/// <param name="BossAttempted">True once that Boss Fight has been attempted.</param>
/// <param name="Published">True when the next Release's bundle exists.</param>
public sealed record ReleaseCheck(
    int Current,
    int Next,
    string FromDomain,
    string NextDomain,
    IReadOnlyList<string> OpenQuests,
    bool BossRequired,
    bool BossAttempted,
    bool Published)
{
    /// <summary>True when every Quest is complete and the Boss Fight, if any, has been attempted.</summary>
    public bool Ready => OpenQuests.Count == 0 && (!BossRequired || BossAttempted);
}

/// <summary>What <c>release next</c> did.</summary>
/// <param name="Check">The check it ran.</param>
/// <param name="Skipped">True when the Domain was skipped (REL-02).</param>
/// <param name="Files">Files in the new Release.</param>
/// <param name="Season2">Items added to Season 2.</param>
public sealed record ReleaseOutcome(ReleaseCheck Check, bool Skipped, int Files, IReadOnlyList<string> Season2);

/// <summary>Everything the Release manager works with.</summary>
/// <param name="Catalog">The Curriculum.</param>
/// <param name="Releases">Unlocked Releases.</param>
/// <param name="Quests">Quest progress.</param>
/// <param name="Attempts">Boss Fight attempts.</param>
/// <param name="Labs">Lab progress.</param>
/// <param name="Ledger">XP (Deep Dives done).</param>
/// <param name="Season2">The Season 2 list.</param>
/// <param name="Sealed">The verify-then-decrypt pipeline.</param>
/// <param name="Workspace">The Learner workspace.</param>
/// <param name="Transactions">One transaction for the unlock.</param>
/// <param name="Time">The clock.</param>
public sealed record ReleaseDependencies(
    CurriculumCatalog Catalog,
    IReleaseStore Releases,
    QuestService Quests,
    IAttemptStore Attempts,
    ILabStateStore Labs,
    XpLedger Ledger,
    ISeason2Store Season2,
    SealedStore Sealed,
    Workspace Workspace,
    IProgressTransactions Transactions,
    TimeProvider Time);

/// <summary>
/// Moves the workspace to the next Release (ADR 0006, REL-01..03, P4): the Learner's work is archived to a
/// <c>portfolio/d&lt;n&gt;</c> branch in the workspace repository, the Release replaces <c>throughline/</c>, and the new
/// baseline is committed. Unfinished Deep Dives, and Labs when skipping, go to Season 2 (S2-01).
/// </summary>
public sealed class ReleaseManager
{
    private readonly ReleaseDependencies d;

    /// <summary>Creates the manager.</summary>
    public ReleaseManager(ReleaseDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        d = dependencies;
    }

    /// <summary>The current Release.</summary>
    public int Current => ReleaseLadder.Current(d.Releases.All());

    /// <summary>Checks whether the next Release can unlock (REL-01).</summary>
    public ReleaseCheck Check()
    {
        var current = Current;
        if (current >= ReleaseLadder.Capstone)
        {
            throw new UsageException("You're on the last Release.", "Run 'ascent status' to see where you stand.");
        }

        var from = ReleaseLadder.DomainOf(current);
        var next = current + 1;
        var nextDomain = ReleaseLadder.DomainOf(next);
        var open = d.Catalog.Quests.Where(q => q.ExamDomain == from && d.Quests.Status(q.Id) != QuestStatus.Complete).Select(q => q.Id).ToList();
        var bossRequired = d.Catalog.BossDomains.Contains(from);
        var bossAttempted = d.Attempts.BossAttempts(from).Any(a => a.FinishedUtc is not null);
        var published = d.Catalog.BundlePaths.ContainsKey(KeyReleasePolicy.ReleaseItemFor(nextDomain));
        return new ReleaseCheck(current, next, from, nextDomain, open, bossRequired, bossAttempted, published);
    }

    /// <summary>Throws when the next Release can't unlock: it isn't published, or the Domain isn't finished and isn't being skipped.</summary>
    public static void EnsureCanUnlock(ReleaseCheck check, bool skip)
    {
        ArgumentNullException.ThrowIfNull(check);
        if (!check.Published)
        {
            throw new AscentException(
                string.Create(CultureInfo.InvariantCulture, $"Release {check.Next} hasn't been published yet."),
                "It arrives with its Domain pack; 'git pull' to check for it.");
        }

        if (!check.Ready && !skip)
        {
            var missing = new List<string>();
            if (check.OpenQuests.Count > 0)
            {
                missing.Add("finish " + string.Join(", ", check.OpenQuests));
            }

            if (check.BossRequired && !check.BossAttempted)
            {
                missing.Add("attempt the " + check.FromDomain + " Boss Fight");
            }

            throw new UsageException(
                "Release " + check.Next.ToString(CultureInfo.InvariantCulture) + " unlocks once you " + string.Join(" and ", missing) + ".",
                "Or skip ahead with 'ascent release next --skip', which forfeits XP for unfinished Labs.");
        }
    }

    /// <summary>The git steps that archive the Learner's work before switching (P4).</summary>
    public static IReadOnlyList<GitStep> ArchiveSteps(ReleaseCheck check, bool hasChanges)
    {
        ArgumentNullException.ThrowIfNull(check);
        var branch = "portfolio/" + (check.FromDomain == "ORI" ? "orientation" : "d" + check.FromDomain[1..]);
        var message = "My " + check.FromDomain + " work";
        return hasChanges
            ? [new(["add", "--all"]), new(["commit", "--quiet", "--message", message]), new(["branch", "--force", branch])]
            : [new(["branch", "--force", branch])];
    }

    /// <summary>The git steps that commit the new Release as the baseline (P4).</summary>
    public static IReadOnlyList<GitStep> BaselineSteps(ReleaseCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);
        return
        [
            new(["add", "--all"]),
            new(["commit", "--quiet", "--allow-empty", "--message", string.Create(CultureInfo.InvariantCulture, $"Release {check.Next} baseline")]),
        ];
    }

    /// <summary>
    /// Unlocks the next Release. Without <paramref name="skip"/>, the Domain must be finished (REL-01); with it, the
    /// Domain is left unfinished and its XP is forfeited (REL-02). Either way, the Domain's unfinished Labs and Deep
    /// Dives go to Season 2 (S2-01). When <paramref name="archive"/> is true, the workspace repository's git steps run
    /// around the switch (REL-03).
    /// </summary>
    public async Task<ReleaseOutcome> UnlockAsync(bool skip, bool archive, CancellationToken cancellationToken)
    {
        var check = Check();
        EnsureCanUnlock(check, skip);

        if (archive)
        {
            await d.Workspace.RunAsync(ArchiveSteps(check, await d.Workspace.HasChangesAsync(cancellationToken)), cancellationToken);
        }

        var season2 = new List<string>();
        var files = d.Transactions.Run(() =>
        {
            var now = d.Time.GetUtcNow();
            d.Releases.Unlock(check.NextDomain, now, skip);
            // Labs left unfinished are skipped: after a skip that includes core Labs, otherwise only bonus Labs.
            var quests = d.Catalog.Quests.Where(q => q.ExamDomain == check.FromDomain).ToList();
            foreach (var lab in quests.SelectMany(q => q.Labs).Distinct(StringComparer.Ordinal)
                .Where(lab => (d.Labs.Find(lab)?.Stage ?? LabStage.NotStarted) != LabStage.Explained))
            {
                d.Season2.Add(lab, Season2Reason.SkippedLab, now);
                season2.Add(lab);
            }

            foreach (var deepDive in quests.Select(q => q.DeepDive).OfType<string>().Distinct(StringComparer.Ordinal)
                .Where(dd => !d.Ledger.Has(XpKind.DeepDive, dd)))
            {
                d.Season2.Add(deepDive, Season2Reason.SkippedDeepDive, now);
                season2.Add(deepDive);
            }

            using var release = d.Sealed.Open(KeyReleasePolicy.ReleaseItemFor(check.NextDomain), ReleaseContext.ReleaseUnlocked(check.NextDomain));
            return d.Workspace.ReplaceRelease(release);
        });

        if (archive)
        {
            await d.Workspace.RunAsync(BaselineSteps(check), cancellationToken);
        }

        return new ReleaseOutcome(check, skip && !check.Ready, files, season2);
    }
}
