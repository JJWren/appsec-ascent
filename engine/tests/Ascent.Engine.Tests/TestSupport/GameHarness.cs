using Ascent.Assessment.Scheduling;
using Ascent.Assessment.Services;
using Ascent.Content.Loading;
using Ascent.Core.Curriculum;
using Ascent.Core.Platform;
using Ascent.Core.Progress;
using Ascent.Core.Time;
using Ascent.Deliverables;
using Ascent.Integrations;
using Ascent.Labs;
using Ascent.Labs.Cloud;
using Ascent.Sealing;
using Ascent.Sealing.Crypto;
using Ascent.Sealing.Flags;
using Ascent.Sealing.KeyRelease;
using Ascent.Storage;
using Ascent.Tests.Shared;
using Microsoft.Extensions.Time.Testing;

namespace Ascent.Engine.Tests.TestSupport;

/// <summary>
/// Wires the game services over a <see cref="CurriculumFixture"/> and a real progress database, the way the CLI's
/// composition root does, with a fake clock, seeded randomness and the fixture's test signing key.
/// </summary>
internal sealed class GameHarness : IDisposable
{
    private readonly SignatureVerifier verifier;
    private CurriculumCatalog? catalog;

    public GameHarness(CurriculumFixture fixture, int seed = 7)
    {
        Fixture = fixture;
        Random = new SeededRandom(seed);
        verifier = new SignatureVerifier(fixture.PublicKeyPem);
        Database = ProgressDatabase.Open(fixture.Paths, Clock, Files);
    }

    public CurriculumFixture Fixture { get; }

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.Zero));

    public IRandomSource Random { get; }

    public IOwnerOnlyFiles Files { get; } = OwnerOnlyFiles.ForCurrentOs();

    public ProgressDatabase Database { get; private set; }

    /// <summary>The Curriculum, loaded on first use (add content to the fixture before touching it).</summary>
    public CurriculumCatalog Catalog => catalog ??= CurriculumCatalog.From(ContentLoader.Load(Fixture.Root));

    public LocalCalendar Calendar => new(Clock, TimeZoneInfo.Utc);

    public XpLedger Ledger => new(new XpStore(Database), Clock);

    public ProgressFactsStore Facts => new(Database);

    public QuestProgressStore QuestProgress => new(Database);

    public TeachBackStore TeachBackStore => new(Database);

    public QuestService Quests => new(Catalog, QuestProgress, Facts, TeachBackStore, Ledger, Clock, Database);

    public WeekGoal WeekGoal => new(new StandUpStore(Database), Ledger, Calendar, Clock, Database);

    public ReviewCardStore Cards => new(Database);

    public AttemptStore Attempts => new(Database);

    public ReviewRecorder Reviews => new(Cards, new FsrsScheduler(), Clock, Database);

    public SealedStore Sealed => new(
        itemId => Catalog.BundlePaths.GetValueOrDefault(itemId),
        verifier,
        new KeyHierarchy(ShieldKeyFile.Load(Fixture.Root)),
        new KeyReleasePolicy(Facts),
        new KeyReleaseStore(Database),
        Clock);

    public QuestionService Questions => new(Sealed);

    public DiagnosticService Diagnostic => new(Catalog, Attempts, Random, Clock);

    public BossFightService Bosses => new(Catalog, Attempts, Reviews, Ledger, Random, Clock, Database);

    public SimulationService Simulations => new(Catalog, Attempts, Clock);

    /// <summary>External tools: none is installed until a test adds it.</summary>
    public RecordingProcessRunner Processes { get; } = new();

    public FixtureOrchestrator Orchestrator { get; } = new();

    public bool IsWindows { get; set; } = true;

    public LabStateStore LabStates => new(Database);

    public FlagService Flags => new(Random, Clock, new LabFlagStore(Database));

    public Workspace Workspace => new(Fixture.Paths, Processes);

    public LabService Labs => new(
        new LabDependencies(
            Catalog,
            LabStates,
            Flags,
            Sealed,
            Workspace,
            Orchestrator,
            new Planter(Processes, Files),
            new VerifyRunner(Sealed, Processes, Fixture.Paths, Files, Random),
            Ledger,
            Facts,
            new ReleaseStore(Database),
            Database,
            Clock),
        IsWindows);

    public ReleaseManager Releases => new(new ReleaseDependencies(
        Catalog, new ReleaseStore(Database), Quests, Attempts, LabStates, Ledger, new Season2Store(Database), Sealed, Workspace, Database, Clock));

    public AzureCli Az => new(Processes, Fixture.Root);

    public CloudDeploymentStore Deployments => new(Database);

    public CloudStage Cloud => new(new CloudDependencies(Az, Workspace, Deployments, Ledger, Database, Clock));

    public DeliverableCatalog DeliverableCatalog => DeliverableCatalog.From(ContentLoader.Load(Fixture.Root));

    public DeliverableStore DeliverableStore => new(Database);

    public DeliverableService Deliverables => new(DeliverableCatalog, DeliverableStore, Sealed, Ledger, Fixture.Paths, Database, Clock);

    public ContentBugService ContentBugs => new(new ContentBugStore(Database), Cards, Ledger, Database, Clock);

    /// <summary>Accepts the rules of engagement, which every Lab needs.</summary>
    public void AcceptRules() => new Core.Profile.ProfileService(new ProfileStore(Database), Clock, Random).AcceptRules();

    /// <summary>Closes and reopens the database, as if the Engine had crashed and restarted.</summary>
    public void Restart()
    {
        Database.Dispose();
        Database = ProgressDatabase.Open(Fixture.Paths, Clock, Files);
    }

    /// <summary>Records a finished Boss Fight with a score, without answering it.</summary>
    public void RecordBossScore(string examDomain, int scorePercent)
    {
        var start = Clock.GetUtcNow();
        var id = Attempts.StartBoss(examDomain, Attempts.BossAttempts(examDomain).Count == 0 ? BossKind.First : BossKind.Rematch, start, start + BossFightService.Duration, ["x"]);
        Attempts.FinishBoss(id, start, scorePercent, 100, scorePercent, scorePercent >= BossFightService.PassPercent);
    }

    public void Dispose()
    {
        verifier.Dispose();
        Database.Dispose();
    }
}
