using System.Globalization;
using System.Text.Json.Nodes;
using Ascent.Content.Loading;
using Ascent.Content.Model;

namespace Ascent.Core.Curriculum;

/// <summary>A Quest's metadata (quest front matter).</summary>
/// <param name="Id">The Quest ID.</param>
/// <param name="ObjectiveId">The Objective.</param>
/// <param name="ExamDomain">ORI, D1–D8 or CAP.</param>
/// <param name="Title">The title.</param>
/// <param name="EstimatedMinutes">The estimated study time.</param>
/// <param name="Labs">Lab IDs.</param>
/// <param name="Drills">Drill IDs.</param>
/// <param name="Deliverables">Deliverable IDs.</param>
/// <param name="DeepDive">The optional Deep Dive.</param>
/// <param name="RelativePath">The Quest file.</param>
public sealed record QuestInfo(
    string Id,
    string ObjectiveId,
    string ExamDomain,
    string Title,
    int EstimatedMinutes,
    IReadOnlyList<string> Labs,
    IReadOnlyList<string> Drills,
    IReadOnlyList<string> Deliverables,
    string? DeepDive,
    string RelativePath);

/// <summary>A Lab's metadata (lab manifest).</summary>
/// <param name="Id">The Lab ID.</param>
/// <param name="ObjectiveId">The Objective.</param>
/// <param name="Release">The Release it runs on.</param>
/// <param name="Bonus">True for bonus Labs (Windows-only or paid), which never block progress (LABE-05).</param>
/// <param name="WindowsOnly">True when the Lab needs Windows.</param>
/// <param name="Red">Red XP.</param>
/// <param name="Blue">Blue XP.</param>
/// <param name="Explain">Explain XP.</param>
public sealed record LabInfo(string Id, string ObjectiveId, int Release, bool Bonus, bool WindowsOnly, int Red, int Blue, int Explain)
{
    /// <summary>The Lab Module's bundle (tier <c>start</c>).</summary>
    public string ModuleRef { get; init; } = Id + ".module";

    /// <summary>The plant spec's bundle (tier <c>start</c>).</summary>
    public string PlantRef { get; init; } = Id + ".plant";

    /// <summary>The security tests' bundle (tier <c>earned</c>).</summary>
    public string TestsRef { get; init; } = Id + ".tests";

    /// <summary>The reference fix's bundle (tier <c>earned</c>).</summary>
    public string FixRef { get; init; } = Id + ".fix";

    /// <summary>The Local Stage's services.</summary>
    public IReadOnlyList<string> Services { get; init; } = [];

    /// <summary>The Cloud Stage, if the Lab has one.</summary>
    public CloudStageInfo? Cloud { get; init; }
}

/// <summary>A Lab's Cloud Stage (lab manifest <c>stages.cloud</c>).</summary>
/// <param name="Bicep">The template, relative to the workspace's <c>throughline/</c> folder.</param>
/// <param name="EstimateUsd">The pre-flight estimate in US dollars (CLD-02).</param>
/// <param name="FreeTier">True when the deployment stays within free grants.</param>
/// <param name="PaidSideQuest">True for a paid side-quest, which needs a typed confirmation.</param>
/// <param name="TeardownWindowHours">Hours until <c>expires-on</c> (CLD-03).</param>
public sealed record CloudStageInfo(string Bicep, decimal EstimateUsd, bool FreeTier, bool PaidSideQuest, int TeardownWindowHours);

/// <summary>A Question Bank item, from its bundle header only (never its content).</summary>
/// <param name="ItemId">The question ID.</param>
/// <param name="ObjectiveId">The Objective.</param>
/// <param name="ExamDomain">The Domain.</param>
/// <param name="Pool">practice, diagnostic or simulation.</param>
/// <param name="RelativePath">The bundle file.</param>
public sealed record QuestionInfo(string ItemId, string ObjectiveId, string ExamDomain, string Pool, string RelativePath);

/// <summary>
/// The active Curriculum, read from content metadata and bundle headers. It drives Quest order, <c>CoreXpMax</c>
/// (RNK-01) and the question pools.
/// </summary>
public sealed class CurriculumCatalog
{
    private static readonly string[] DomainOrder = ["ORI", "D1", "D2", "D3", "D4", "D5", "D6", "D7", "D8", "CAP"];

    private CurriculumCatalog(
        Outline? outline,
        IReadOnlyList<QuestInfo> quests,
        IReadOnlyList<LabInfo> labs,
        IReadOnlyList<string> drills,
        IReadOnlyList<string> deliverables,
        IReadOnlyList<QuestionInfo> questions,
        IReadOnlyDictionary<string, string> bundlePaths,
        bool seasonComplete)
    {
        Outline = outline;
        Quests = quests;
        Labs = labs;
        Drills = drills;
        Deliverables = deliverables;
        Questions = questions;
        BundlePaths = bundlePaths;
        SeasonComplete = seasonComplete;
    }

    /// <summary>The active outline.</summary>
    public Outline? Outline { get; }

    /// <summary>Quests in exam order (Orientation, D1–D8, Capstone; then Objective; then ID).</summary>
    public IReadOnlyList<QuestInfo> Quests { get; }

    /// <summary>Labs.</summary>
    public IReadOnlyList<LabInfo> Labs { get; }

    /// <summary>Drill IDs.</summary>
    public IReadOnlyList<string> Drills { get; }

    /// <summary>Deliverable IDs.</summary>
    public IReadOnlyList<string> Deliverables { get; }

    /// <summary>Question Bank items from bundle headers.</summary>
    public IReadOnlyList<QuestionInfo> Questions { get; }

    /// <summary>Every bundle's file (absolute), by item ID.</summary>
    public IReadOnlyDictionary<string, string> BundlePaths { get; }

    /// <summary>True when <c>curriculum/season.yaml</c> is marked complete (EXM-01).</summary>
    public bool SeasonComplete { get; }

    /// <summary>The exam Domains that have at least one Quest: one Boss Fight each.</summary>
    public IReadOnlyList<string> BossDomains =>
        Quests.Select(q => q.ExamDomain).Where(d => d.Length == 2 && d[0] == 'D').Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

    /// <summary>
    /// The XP the core path can earn (RNK-01): Quests × 10, drills × 15, each non-bonus Lab's Red + Blue + Explain,
    /// Deliverables × 50 and Boss Fights × 100. Stand-ups and the weekly goal add slack on top; bonus content never counts.
    /// </summary>
    public long CoreXpMax =>
        Quests.Count * 10L
        + Drills.Count * 15L
        + Labs.Where(l => !l.Bonus).Sum(l => (long)l.Red + l.Blue + l.Explain)
        + Deliverables.Count * 50L
        + BossDomains.Count * 100L;

    /// <summary>Builds the catalog from a content index.</summary>
    public static CurriculumCatalog From(ContentIndex index)
    {
        ArgumentNullException.ThrowIfNull(index);
        var quests = index.OfKind(DocumentKind.Quest)
            .Where(d => d.IsSchemaValid && d.Data is not null)
            .Select(d => Quest(d.Data!, d.RelativePath))
            .OrderBy(q => DomainRank(q.ExamDomain))
            .ThenBy(q => ObjectiveKey(q.ObjectiveId))
            .ThenBy(q => q.Id, StringComparer.Ordinal)
            .ToList();

        var labs = index.OfKind(DocumentKind.LabManifest)
            .Where(d => d.IsSchemaValid && d.Data is not null)
            .Select(d => Lab(d.Data!))
            .OrderBy(l => l.Id, StringComparer.Ordinal)
            .ToList();

        var bundles = index.OfKind(DocumentKind.Bundle).Where(d => d.IsSchemaValid && d.Id is not null).ToList();
        var questions = bundles
            .Select(d => (Document: d, Header: JsonRead.Obj(d.Data, "header")))
            .Where(b => JsonRead.Str(b.Header, "itemType") == "question")
            .Select(b => new QuestionInfo(
                b.Document.Id!,
                JsonRead.Str(b.Header, "objectiveId") ?? string.Empty,
                JsonRead.Str(b.Header, "examDomain") ?? string.Empty,
                JsonRead.Str(b.Header, "pool") ?? "practice",
                b.Document.RelativePath))
            .OrderBy(q => q.ItemId, StringComparer.Ordinal)
            .ToList();

        return new CurriculumCatalog(
            index.Outline,
            quests,
            labs,
            Ids(index, DocumentKind.Drill),
            Ids(index, DocumentKind.DeliverableTemplate),
            questions,
            bundles.GroupBy(d => d.Id!, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => Path.Join(index.Root, g.First().RelativePath), StringComparer.Ordinal),
            index.Season.SeasonComplete);
    }

    /// <summary>A sort key that orders Objectives numerically (4.9 before 4.10).</summary>
    public static (int Major, int Minor, string Text) ObjectiveKey(string objectiveId)
    {
        ArgumentNullException.ThrowIfNull(objectiveId);
        var parts = objectiveId.Split('.', '-');
        return parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor)
            ? (major, minor, objectiveId)
            : (int.MaxValue, int.MaxValue, objectiveId);
    }

    /// <summary>The position of a Domain in exam order.</summary>
    public static int DomainRank(string examDomain)
    {
        var index = Array.IndexOf(DomainOrder, examDomain);
        return index < 0 ? DomainOrder.Length : index;
    }

    private static QuestInfo Quest(JsonObject data, string relativePath)
    {
        var activities = JsonRead.Obj(data, "activities");
        return new QuestInfo(
            JsonRead.Str(data, "id")!,
            JsonRead.Str(data, "objectiveId") ?? string.Empty,
            JsonRead.Str(data, "examDomain") ?? string.Empty,
            JsonRead.Str(data, "title") ?? string.Empty,
            JsonRead.WholeNumber(data, "estimatedMinutes") ?? 0,
            JsonRead.Strings(JsonRead.Arr(activities, "labs")),
            JsonRead.Strings(JsonRead.Arr(activities, "drills")),
            JsonRead.Strings(JsonRead.Arr(activities, "deliverables")),
            JsonRead.Str(data, "deepDive"),
            relativePath);
    }

    private static LabInfo Lab(JsonObject data)
    {
        var xp = JsonRead.Obj(data, "xp");
        var id = JsonRead.Str(data, "id")!;
        var sealedRefs = JsonRead.Obj(data, "sealed");
        var stages = JsonRead.Obj(data, "stages");
        var cloud = JsonRead.Obj(stages, "cloud");
        return new LabInfo(
            id,
            JsonRead.Str(data, "objectiveId") ?? string.Empty,
            JsonRead.WholeNumber(data, "release") ?? 0,
            JsonRead.Bool(data, "bonus") == true,
            JsonRead.Bool(data, "windowsOnly") == true,
            JsonRead.WholeNumber(xp, "red") ?? 25,
            JsonRead.WholeNumber(xp, "blue") ?? 40,
            JsonRead.WholeNumber(xp, "explain") ?? 10)
        {
            ModuleRef = JsonRead.Str(JsonRead.Obj(data, "module"), "sealedRef") ?? id + ".module",
            PlantRef = JsonRead.Str(sealedRefs, "plant") ?? id + ".plant",
            TestsRef = JsonRead.Str(sealedRefs, "tests") ?? id + ".tests",
            FixRef = JsonRead.Str(sealedRefs, "fix") ?? id + ".fix",
            Services = JsonRead.Strings(JsonRead.Arr(JsonRead.Obj(stages, "local"), "services")),
            Cloud = cloud is null ? null : new CloudStageInfo(
                JsonRead.Str(cloud, "bicep") ?? string.Empty,
                (decimal)(JsonRead.Num(cloud, "estimateUsd") ?? 0),
                JsonRead.Bool(cloud, "freeTier") == true,
                JsonRead.Bool(cloud, "paidSideQuest") == true,
                JsonRead.WholeNumber(cloud, "teardownWindowHours") ?? 4),
        };
    }

    private static List<string> Ids(ContentIndex index, DocumentKind kind) =>
        index.OfKind(kind).Where(d => d.IsSchemaValid && d.Id is not null).Select(d => d.Id!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
}
