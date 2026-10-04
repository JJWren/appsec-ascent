using Ascent.Assessment.Scheduling;
using Ascent.Assessment.Services;
using Ascent.Core.Curriculum;
using Ascent.Core.Domain;
using Ascent.Core.Profile;
using Ascent.Core.Progress;
using Ascent.Core.Time;
using Ascent.Sealing;
using Ascent.Sealing.Crypto;
using Ascent.Sealing.KeyRelease;
using Ascent.Storage;

namespace Ascent.Cli.Hosting;

/// <summary>
/// The Engine's services for Learner commands, each created on first use (P19). Everything here touches Learner state,
/// so the first access opens (and, if needed, creates) the progress store.
/// </summary>
public sealed class EngineServices : IDisposable
{
    private readonly EngineHost host;
    private readonly EngineOptions options;
    private LearnerProfile? profile;
    private CurriculumCatalog? catalog;
    private SignatureVerifier? verifier;
    private SealedStore? sealedStore;
    private LocalCalendar? calendar;

    internal EngineServices(EngineHost host, EngineOptions options)
    {
        this.host = host;
        this.options = options;
    }

    /// <summary>The progress store.</summary>
    public ProgressDatabase Database => host.Database;

    /// <summary>The Learner profile, created on first use.</summary>
    public LearnerProfile Profile => profile ??= ProfileService.EnsureProfile();

    /// <summary>Profile and settings.</summary>
    public ProfileService ProfileService => new(new ProfileStore(Database), host.Time, host.Random);

    /// <summary>Local days and weeks in the Learner's time zone.</summary>
    public LocalCalendar Calendar => calendar ??= new LocalCalendar(host.Time, LocalCalendar.ResolveZone(Profile.TimeZone));

    /// <summary>The active Curriculum. Loading it carries progress over to a new outline version (E10-06).</summary>
    public CurriculumCatalog Catalog => catalog ??= LoadCatalog();

    /// <summary>The XP ledger.</summary>
    public XpLedger Ledger => new(new XpStore(Database), host.Time);

    /// <summary>Progress facts for releases and Quest completion.</summary>
    public ProgressFactsStore Facts => new(Database);

    /// <summary>The Quest flow.</summary>
    public QuestService Quests => new(Catalog, new QuestProgressStore(Database), Facts, new TeachBackStore(Database), Ledger, host.Time, Database);

    /// <summary>Teach-backs, in the configured folder.</summary>
    public TeachBackService TeachBacks => new(new TeachBackStore(Database), TeachBackService.FolderFor(Profile, host.Paths), host.Time);

    /// <summary>Stand-up days and the weekly goal.</summary>
    public WeekGoal WeekGoal => new(new StandUpStore(Database), Ledger, Calendar, host.Time, Database);

    /// <summary>Ranks reached.</summary>
    public IRankStore Ranks => new RankStore(Database);

    /// <summary>Season 2.</summary>
    public ISeason2Store Season2 => new Season2Store(Database);

    /// <summary>Review cards.</summary>
    public IReviewCardStore Cards => new ReviewCardStore(Database);

    /// <summary>Attempts.</summary>
    public IAttemptStore Attempts => new AttemptStore(Database);

    /// <summary>Records reviews into FSRS.</summary>
    public ReviewRecorder Reviews => new(Cards, new FsrsScheduler(), host.Time, Database);

    /// <summary>Serves questions through the sealing pipeline.</summary>
    public QuestionService Questions => new(Sealed);

    /// <summary>The Diagnostic.</summary>
    public DiagnosticService Diagnostic => new(Catalog, Attempts, host.Random, host.Time);

    /// <summary>Boss Fights.</summary>
    public BossFightService Bosses => new(Catalog, Attempts, Reviews, Ledger, host.Random, host.Time, Database);

    /// <summary>Simulations.</summary>
    public SimulationService Simulations => new(Catalog, Attempts, host.Time);

    /// <summary>Progress export and import (BAK-01).</summary>
    public ProgressExporter Exporter => new(Database, host.Paths, host.Time, host.Files);

    /// <summary>The trusted maintainer key: the embedded one, or a test key from the composition root (P8).</summary>
    public SignatureVerifier Verifier => verifier ??= options.TrustedKeyPem is { } pem ? new SignatureVerifier(pem) : SignatureVerifier.Embedded();

    /// <summary>The verify-then-decrypt pipeline (P1).</summary>
    public SealedStore Sealed => sealedStore ??= new SealedStore(
        itemId => Catalog.BundlePaths.GetValueOrDefault(itemId),
        Verifier,
        new KeyHierarchy(ShieldKeyFile.Load(host.Paths.RepoRoot)),
        new KeyReleasePolicy(Facts),
        new KeyReleaseStore(Database),
        host.Time);

    /// <summary>The current time, never earlier than a time a previous command saw (P17).</summary>
    public DateTimeOffset Now
    {
        get
        {
            _ = Database;
            var now = host.Time.GetUtcNow();
            return host.Clock is { } clock && clock.EffectiveNow > now ? clock.EffectiveNow : now;
        }
    }

    /// <summary>Evaluates the Rank and records any promotion (RNK-01..03).</summary>
    public (Rank Rank, bool Promoted, long Total, long CoreXpMax) EvaluateRank(IReadOnlyDictionary<string, int> bestBossScores)
    {
        ArgumentNullException.ThrowIfNull(bestBossScores);
        var total = Ledger.Total;
        var coreXpMax = Catalog.CoreXpMax;
        var allBossesPassed = Catalog.Outline?.Domains.All(d => bestBossScores.GetValueOrDefault(d.Id) >= BossFightService.PassPercent) == true;
        var gates = new RankGates(allBossesPassed, Quests.CapstoneComplete);
        var reached = Ranks.Highest();
        var rank = RankCalculator.Current(total, coreXpMax, gates, reached);
        var promoted = reached is null ? rank > Rank.Developer : rank > reached;
        if (promoted || reached is null)
        {
            Ranks.Record(rank, host.Time.GetUtcNow());
        }

        return (rank, promoted, total, coreXpMax);
    }

    /// <inheritdoc />
    public void Dispose() => verifier?.Dispose();

    private CurriculumCatalog LoadCatalog()
    {
        var loaded = CurriculumCatalog.From(host.Content);
        var change = new OutlineRefresh(new OutlineVersionStore(Database), Cards, Database).Apply(loaded.Outline?.Version, OutlineMapping.Load(host.Content));
        if (change is { MappingMissing: true })
        {
            host.Renderer.Status(
                Rendering.Outcome.Warn,
                "The exam outline changed from " + change.From + " to " + change.To + ", but there's no mapping between them yet, so your review cards keep their old Objectives. Please report it as a Content Bug.");
        }
        else if (change is not null)
        {
            host.Renderer.Status(
                Rendering.Outcome.Info,
                "The exam outline changed from " + change.From + " to " + change.To + ". " + change.CardsRemapped.ToString(System.Globalization.CultureInfo.InvariantCulture) + " review card(s) moved to their new Objectives.");
        }

        return loaded;
    }
}
