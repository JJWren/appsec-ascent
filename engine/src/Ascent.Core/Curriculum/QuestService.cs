using System.Globalization;
using System.Text;
using Ascent.Content.Loading;
using Ascent.Core.Domain;
using Ascent.Core.Errors;
using Ascent.Core.Platform;
using Ascent.Core.Profile;
using Ascent.Core.Progress;

namespace Ascent.Core.Curriculum;

/// <summary>One activity of a Quest and whether it's done.</summary>
/// <param name="Kind">lab, drill or deliverable.</param>
/// <param name="Id">The activity's ID.</param>
/// <param name="Done">True when done.</param>
/// <param name="Bonus">True for bonus activities, which never block completion (LABE-05).</param>
public sealed record ActivityState(string Kind, string Id, bool Done, bool Bonus);

/// <summary>A Quest with its progress.</summary>
/// <param name="Quest">The Quest.</param>
/// <param name="Status">Its status.</param>
/// <param name="LessonDone">True once the lesson and its Teach-back are done.</param>
/// <param name="Activities">Its activities.</param>
public sealed record QuestView(QuestInfo Quest, QuestStatus Status, bool LessonDone, IReadOnlyList<ActivityState> Activities);

/// <summary>Quests in exam order: lesson, Teach-back, activities, completion (E2-01, XP-01).</summary>
public sealed class QuestService
{
    private readonly CurriculumCatalog catalog;
    private readonly IQuestProgressStore progress;
    private readonly IActivityFacts facts;
    private readonly ITeachBackStore teachBacks;
    private readonly XpLedger ledger;
    private readonly TimeProvider time;
    private readonly IProgressTransactions transactions;

    /// <summary>Creates the service.</summary>
    public QuestService(CurriculumCatalog catalog, IQuestProgressStore progress, IActivityFacts facts, ITeachBackStore teachBacks, XpLedger ledger, TimeProvider time, IProgressTransactions transactions)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(teachBacks);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(transactions);
        this.catalog = catalog;
        this.progress = progress;
        this.facts = facts;
        this.teachBacks = teachBacks;
        this.ledger = ledger;
        this.time = time;
        this.transactions = transactions;
    }

    /// <summary>The first Quest in exam order that isn't complete, or null when all are.</summary>
    public QuestInfo? Next() => catalog.Quests.FirstOrDefault(q => Status(q.Id) != QuestStatus.Complete);

    /// <summary>A Quest's status.</summary>
    public QuestStatus Status(string questId) => progress.Find(questId)?.Status ?? QuestStatus.NotStarted;

    /// <summary>Finds a Quest, or throws a usage error naming the ID.</summary>
    public QuestInfo Find(string questId) =>
        catalog.Quests.FirstOrDefault(q => string.Equals(q.Id, questId, StringComparison.Ordinal))
            ?? throw new UsageException("There is no Quest called '" + SafeText.Sanitize(questId, 60) + "'.", "Run 'ascent next' to see the next Quest.");

    /// <summary>Opens a Quest, marking it in progress.</summary>
    public QuestView Open(string questId)
    {
        var quest = Find(questId);
        if (progress.Find(questId) is null)
        {
            progress.Save(new QuestProgressRecord(questId, QuestStatus.InProgress, null, null));
        }

        return View(quest);
    }

    /// <summary>The Quest with its activities' state.</summary>
    public QuestView View(QuestInfo quest)
    {
        ArgumentNullException.ThrowIfNull(quest);
        var record = progress.Find(quest.Id);
        var activities = quest.Labs.Select(lab => new ActivityState("lab", lab, facts.LabStage(lab) >= LabStage.Explained, IsBonusLab(lab)))
            .Concat(quest.Drills.Select(drill => new ActivityState("drill", drill, facts.DrillDone(drill), false)))
            .Concat(quest.Deliverables.Select(dlv => new ActivityState("deliverable", dlv, facts.DeliverableSubmitted(dlv), false)))
            .ToList();
        return new QuestView(quest, record?.Status ?? QuestStatus.NotStarted, record?.LessonCompletedUtc is not null, activities);
    }

    /// <summary>
    /// Completes the lesson once its Teach-back is saved, awarding Lesson XP once (XP-01), then completes the Quest if
    /// every non-bonus activity is done. It's one transaction (REL-U2-01).
    /// </summary>
    public QuestView CompleteLesson(string questId)
    {
        var quest = Find(questId);
        if (!teachBacks.Exists(questId))
        {
            throw new AscentException("The lesson needs its Teach-back first.", "Run 'ascent teachback " + questId + "'.");
        }

        return transactions.Run(() =>
        {
            var record = progress.Find(questId) ?? new QuestProgressRecord(questId, QuestStatus.InProgress, null, null);
            if (record.LessonCompletedUtc is null)
            {
                progress.Save(record with { LessonCompletedUtc = time.GetUtcNow() });
            }

            ledger.Award(XpKind.Lesson, questId);
            return TryComplete(quest);
        });
    }

    /// <summary>Marks the Quest complete when its lesson and every non-bonus activity are done.</summary>
    public QuestView TryComplete(QuestInfo quest)
    {
        var view = View(quest);
        if (view.Status != QuestStatus.Complete && view.LessonDone && view.Activities.Where(a => !a.Bonus).All(a => a.Done))
        {
            var record = progress.Find(quest.Id)!;
            progress.Save(record with { Status = QuestStatus.Complete, CompletedUtc = time.GetUtcNow() });
            return View(quest);
        }

        return view;
    }

    /// <summary>Objectives whose lesson is complete; their questions can enter the Stand-up (SU-01).</summary>
    public IReadOnlySet<string> ObjectivesWithLessonDone() =>
        catalog.Quests.Where(q => progress.Find(q.Id)?.LessonCompletedUtc is not null).Select(q => q.ObjectiveId).ToHashSet(StringComparer.Ordinal);

    /// <summary>Domains whose Quests are all complete.</summary>
    public IReadOnlyList<string> DomainsComplete() =>
        catalog.Quests.GroupBy(q => q.ExamDomain, StringComparer.Ordinal)
            .Where(g => g.All(q => Status(q.Id) == QuestStatus.Complete))
            .Select(g => g.Key)
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <summary>True when the Capstone exists and all its Quests are complete (RNK-02).</summary>
    public bool CapstoneComplete
    {
        get
        {
            var capstone = catalog.Quests.Where(q => q.ExamDomain == "CAP").ToList();
            return capstone.Count > 0 && capstone.All(q => Status(q.Id) == QuestStatus.Complete);
        }
    }

    private bool IsBonusLab(string labId) => catalog.Labs.FirstOrDefault(l => l.Id == labId)?.Bonus == true;
}

/// <summary>Saves Teach-backs as Markdown in the Learner's folder (E2-04, LABE-03, E1-07).</summary>
public sealed class TeachBackService
{
    /// <summary>The most words a Teach-back may have.</summary>
    public const int MaxWords = 150;

    /// <summary>The fewest words a Lab's Teach-back may have (LABE-03).</summary>
    public const int LabMinWords = 30;

    private readonly ITeachBackStore store;
    private readonly string folder;
    private readonly TimeProvider time;

    /// <summary>Creates the service over the Teach-back folder.</summary>
    public TeachBackService(ITeachBackStore store, string folder, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentNullException.ThrowIfNull(time);
        this.store = store;
        this.folder = folder;
        this.time = time;
    }

    /// <summary>The Teach-back folder for a profile: its setting (absolute or repository-relative), else <c>journal/</c>.</summary>
    public static string FolderFor(LearnerProfile profile, EnginePaths paths)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(paths);
        return profile.TeachBackFolder is { Length: > 0 } configured
            ? Path.GetFullPath(Path.IsPathRooted(configured) ? configured : Path.Join(paths.RepoRoot, configured))
            : paths.DefaultJournal;
    }

    /// <summary>Counts words: runs of non-whitespace.</summary>
    public static int CountWords(string text) =>
        string.IsNullOrWhiteSpace(text) ? 0 : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>Saves a Teach-back and returns its file.</summary>
    /// <param name="refId">The Quest or Lab.</param>
    /// <param name="text">The Teach-back, in the Learner's own words.</param>
    /// <param name="forLab">True for a Lab's Explain step, which needs at least 30 words.</param>
    public string Save(string refId, string text, bool forLab)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refId);
        var clean = SafeText.Sanitize(text, 10_000).Trim();
        var words = CountWords(clean);
        var minimum = forLab ? LabMinWords : 1;
        if (words < minimum || words > MaxWords)
        {
            throw new UsageException(
                string.Create(CultureInfo.InvariantCulture, $"A Teach-back needs {minimum}–{MaxWords} words; this one has {words}."),
                "Explain it in your own words, briefly, then try again.");
        }

        Directory.CreateDirectory(folder);
        var path = SafePath.Resolve(folder, refId + ".md");
        var saved = time.GetUtcNow();
        var content = new StringBuilder()
            .Append("# Teach-back: ").Append(refId).Append('\n').Append('\n')
            .Append("_Saved ").Append(saved.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).Append(" UTC, ")
            .Append(words.ToString(CultureInfo.InvariantCulture)).Append(" words._\n\n")
            .Append(clean).Append('\n')
            .ToString();
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        store.Save(refId, path, words, saved);
        return path;
    }
}

/// <summary>When to ask for the exam date (EXM-01).</summary>
public static class ExamPrompt
{
    /// <summary>True when the Season is complete, no exam date is set, and the prompt isn't snoozed for today.</summary>
    public static bool ShouldPrompt(bool seasonComplete, LearnerProfile profile, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return seasonComplete && profile.ExamDate is null && (profile.ExamPromptSnoozedUntil is not { } until || today >= until);
    }
}

/// <summary>One step of an outline mapping (E10-06).</summary>
/// <param name="From">The old Objective, or null for a new one.</param>
/// <param name="To">The new Objectives.</param>
/// <param name="Kind">same, split, merged, removed or new.</param>
public sealed record MappingEntry(string? From, IReadOnlyList<string> To, string Kind);

/// <summary>A mapping between two outline versions.</summary>
/// <param name="From">The old outline version.</param>
/// <param name="To">The new outline version.</param>
/// <param name="Entries">The entries.</param>
public sealed record OutlineMapping(string From, string To, IReadOnlyList<MappingEntry> Entries)
{
    /// <summary>Loads every schema-valid mapping.</summary>
    public static IReadOnlyList<OutlineMapping> Load(ContentIndex index)
    {
        ArgumentNullException.ThrowIfNull(index);
        return index.OfKind(DocumentKind.OutlineMapping)
            .Where(d => d.IsSchemaValid && d.Data is not null)
            .Select(d => new OutlineMapping(
                JsonRead.Str(d.Data, "from")!,
                JsonRead.Str(d.Data, "to")!,
                (JsonRead.Arr(d.Data, "entries") ?? []).OfType<System.Text.Json.Nodes.JsonObject>()
                    .Select(e => new MappingEntry(JsonRead.Str(e, "from"), JsonRead.Strings(JsonRead.Arr(e, "to")), JsonRead.Str(e, "kind") ?? "same"))
                    .ToList()))
            .ToList();
    }

    /// <summary>
    /// The chain of mappings that leads from one version to another, or null when there's none. Progress carries over
    /// one mapping at a time (E10-06).
    /// </summary>
    public static IReadOnlyList<OutlineMapping>? Chain(IReadOnlyList<OutlineMapping> mappings, string from, string to)
    {
        ArgumentNullException.ThrowIfNull(mappings);
        var chain = new List<OutlineMapping>();
        var current = from;
        while (!string.Equals(current, to, StringComparison.Ordinal))
        {
            var step = mappings.FirstOrDefault(m => m.From == current);
            if (step is null || chain.Contains(step))
            {
                return null;
            }

            chain.Add(step);
            current = step.To;
        }

        return chain;
    }

    /// <summary>Maps an old Objective to its new one: same, split and merged map to the first target; removed maps to null.</summary>
    public string? Map(string objectiveId)
    {
        var entry = Entries.FirstOrDefault(e => e.From == objectiveId);
        return entry switch
        {
            null => objectiveId,
            { Kind: "removed" } => null,
            { To.Count: > 0 } => entry.To[0],
            _ => objectiveId,
        };
    }
}

/// <summary>What changed when progress was checked against the active outline (E10-06).</summary>
/// <param name="From">The outline version progress was keyed to.</param>
/// <param name="To">The active outline version.</param>
/// <param name="CardsRemapped">Review cards moved to their new Objectives.</param>
/// <param name="MappingMissing">True when no chain of mappings leads from <paramref name="From"/> to <paramref name="To"/>.</param>
public sealed record OutlineChange(string From, string To, int CardsRemapped, bool MappingMissing);

/// <summary>Carries progress over when the active outline changes: review cards follow the mapping chain (E10-06).</summary>
public sealed class OutlineRefresh
{
    private readonly IOutlineVersionStore versions;
    private readonly IReviewCardStore cards;
    private readonly IProgressTransactions transactions;

    /// <summary>Creates the refresh.</summary>
    public OutlineRefresh(IOutlineVersionStore versions, IReviewCardStore cards, IProgressTransactions transactions)
    {
        ArgumentNullException.ThrowIfNull(versions);
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(transactions);
        this.versions = versions;
        this.cards = cards;
        this.transactions = transactions;
    }

    /// <summary>
    /// Records the active version the first time, and remaps progress when it changes. Without a mapping chain nothing
    /// is remapped or recorded, so a mapping added later still applies. Returns null when nothing changed.
    /// </summary>
    public OutlineChange? Apply(string? activeVersion, IReadOnlyList<OutlineMapping> mappings)
    {
        ArgumentNullException.ThrowIfNull(mappings);
        if (activeVersion is null)
        {
            return null;
        }

        var recorded = versions.Recorded;
        if (recorded is null)
        {
            versions.Record(activeVersion);
            return null;
        }

        if (recorded == activeVersion)
        {
            return null;
        }

        var chain = OutlineMapping.Chain(mappings, recorded, activeVersion);
        if (chain is null)
        {
            return new OutlineChange(recorded, activeVersion, 0, MappingMissing: true);
        }

        var remapped = transactions.Run(() =>
        {
            var changed = cards.RemapObjectives(objective => chain.Aggregate<OutlineMapping, string?>(objective, (current, step) => current is null ? null : step.Map(current)));
            versions.Record(activeVersion);
            return changed;
        });
        return new OutlineChange(recorded, activeVersion, remapped, MappingMissing: false);
    }
}
