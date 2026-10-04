using Ascent.Cli.Hosting;
using Ascent.Core;
using Ascent.Core.Platform;
using Ascent.Tests.Shared;
using Microsoft.Extensions.Time.Testing;

namespace Ascent.Cli.Tests.TestSupport;

/// <summary>Runs <c>ascent</c> commands in-process against a throwaway repository, capturing output.</summary>
internal sealed class TestEngine : IDisposable
{
    private TestEngine(string root, bool plain, string? trustedKeyPem = null)
    {
        Root = root;
        Plain = plain;
        TrustedKeyPem = trustedKeyPem;
    }

    public string Root { get; }

    public bool Plain { get; }

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero));

    public Dictionary<string, string?> Environment { get; } = [];

    public string Input { get; set; } = string.Empty;

    /// <summary>The trusted signing key for this repository's Sealed Bundles (P8), if it has any.</summary>
    public string? TrustedKeyPem { get; }

    /// <summary>Seeded randomness, so draws and IDs repeat (P26).</summary>
    public IRandomSource Random { get; set; } = new SeededRandom(7);

    /// <summary>External tools; by default none is installed, so no test starts a real tool unless it opts in.</summary>
    public IProcessRunner Processes { get; set; } = new RecordingProcessRunner();

    /// <summary>The Local Stage, or null for the production adapter.</summary>
    public Ascent.Labs.IOrchestrator? Orchestrator { get; set; }

    /// <summary>The AI reviewer's transport, or null for the real client.</summary>
    public Ascent.Deliverables.IReviewerClient? ReviewerClient { get; set; }

    /// <summary>The HTTP transport, or null for a real one.</summary>
    public HttpMessageHandler? HttpTransport { get; set; }

    /// <summary>Whether Windows-only Labs can run.</summary>
    public bool IsWindows { get; set; } = true;

    /// <summary>URLs the Engine asked to open.</summary>
    public List<Uri> OpenedUrls { get; } = [];

    public EnginePaths Paths => new(Root);

    /// <summary>The real repository root (the folder containing AppSecAscent.slnx).</summary>
    public static string RealRoot { get; } = FindRealRoot();

    public static TestEngine Empty(bool plain = true)
    {
        var root = Path.Join(Path.GetTempPath(), "ascent-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return new TestEngine(root, plain);
    }

    /// <summary>An engine over a fixture's Curriculum, trusting the fixture's test signing key.</summary>
    public static TestEngine For(CurriculumFixture fixture, bool plain = true) => new(fixture.Root, plain, fixture.PublicKeyPem);

    /// <summary>Copies the real JSON schemas, so content in this repository is classified and validated like the real one.</summary>
    public TestEngine WithSchemas()
    {
        foreach (var file in Directory.EnumerateFiles(Path.Join(RealRoot, "schemas"), "*.schema.json"))
        {
            WriteFile("schemas/" + Path.GetFileName(file), File.ReadAllText(file));
        }

        return this;
    }

    public string WriteFile(string relativePath, string content)
    {
        var full = Path.Join(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    public EngineHost CreateHost(TextWriter output, TimeProvider? time = null) => new(new EngineOptions
    {
        RepoRoot = Root,
        Time = time ?? Clock,
        Random = Random,
        TrustedKeyPem = TrustedKeyPem,
        Processes = Processes,
        Orchestrator = Orchestrator,
        ReviewerClient = ReviewerClient,
        HttpTransport = HttpTransport,
        IsWindows = IsWindows,
        OpenUrl = OpenedUrls.Add,
        Output = output,
        Input = new StringReader(Input),
        Environment = name => Environment.TryGetValue(name, out var value) ? value : null,
        OutputRedirected = Plain,
    });

    /// <summary>Runs a command with <paramref name="input"/> as the plain-mode answers, one per line.</summary>
    public Task<(int ExitCode, string Output)> RunWithInputAsync(IEnumerable<string> input, params string[] args)
    {
        Input = string.Join('\n', input) + "\n";
        return RunAsync(args);
    }

    public async Task<(int ExitCode, string Output)> RunAsync(params string[] args)
    {
        using var output = new StringWriter();
        using var host = CreateHost(output);
        var exitCode = await EngineApp.Create(host).RunAsync(args, TestContext.Current.CancellationToken);
        return (exitCode, output.ToString());
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string FindRealRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Join(directory.FullName, EnginePaths.SolutionFile)))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Could not find the repository root.");
    }
}
