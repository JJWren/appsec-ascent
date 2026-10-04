using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Ascent.Cli.Commands;
using Ascent.Cli.Rendering;
using Ascent.Content.Loading;
using Ascent.Core;
using Ascent.Core.Errors;
using Ascent.Core.Interaction;
using Ascent.Core.Logging;
using Ascent.Core.Platform;
using Ascent.Storage;
using Spectre.Console;

namespace Ascent.Cli.Hosting;

/// <summary>
/// The composition root (P19): it creates each service on first use, so a command pays only for what it touches.
/// There is no DI container; Spectre resolves commands through <see cref="TypeRegistrar"/>.
/// </summary>
public sealed class EngineHost : IDisposable
{
    private readonly EngineOptions options;
    private EngineSettings? settings;
    private EnginePaths? paths;
    private IOwnerOnlyFiles? files;
    private IProcessRunner? processes;
    private ProgressDatabase? database;
    private ContentIndex? content;
    private IRenderer? renderer;
    private IAnsiConsole? console;
    private IPrompter? prompter;
    private string commandName = "(parse)";
    private long started = Stopwatch.GetTimestamp();

    /// <summary>Creates the host.</summary>
    public EngineHost(EngineOptions? options = null) => this.options = options ?? new EngineOptions();

    /// <summary>The Engine's version, as shown in the User-Agent and logs.</summary>
    public static string Version { get; } =
        typeof(EngineHost).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";

    /// <summary>The clock.</summary>
    public TimeProvider Time => options.Time;

    /// <summary>The randomness source.</summary>
    public IRandomSource Random => options.Random;

    /// <summary>Owner-only file creation.</summary>
    public IOwnerOnlyFiles Files => files ??= options.Files ?? OwnerOnlyFiles.ForCurrentOs();

    /// <summary>The external-tool runner.</summary>
    public IProcessRunner Processes => processes ??= options.Processes ?? new ProcessRunner();

    /// <summary>Repository and state paths.</summary>
    public EnginePaths Paths => paths ??= new EnginePaths(EnginePaths.FindRepoRoot(settings?.Root ?? options.RepoRoot, Directory.GetCurrentDirectory()));

    /// <summary>The clock reading taken when the database was opened.</summary>
    public ClockReading? Clock { get; private set; }

    /// <summary>The progress database, opened (and migrated) on first use.</summary>
    public ProgressDatabase Database => database ??= OpenDatabase();

    /// <summary>The curriculum content index, loaded on first use.</summary>
    public ContentIndex Content => content ??= LoadContent();

    /// <summary>The output renderer for the current command.</summary>
    public IRenderer Renderer => renderer ??= CreateRenderer();

    /// <summary>The prompter for the current command.</summary>
    public IPrompter Prompter => prompter ??= options.Prompter ?? CreatePrompter();

    /// <summary>Phase timings for <c>--timings</c>.</summary>
    public Timings Timings { get; } = new();

    /// <summary>Where command output goes (machine-readable JSON included).</summary>
    public TextWriter Out => options.Output ?? System.Console.Out;

    /// <summary>
    /// A Spectre console that honours the current output mode, for commands that draw their own tables. In plain mode
    /// lines are never wrapped, so logs and screen readers get each finding on one line.
    /// </summary>
    public IAnsiConsole OutputConsole()
    {
        if (!Renderer.IsPlain)
        {
            return SpectreConsole();
        }

        var plain = PlainConsole(Out);
        plain.Profile.Width = short.MaxValue;
        return plain;
    }

    /// <summary>
    /// A Spectre console with no ANSI at all. Spectre's CI enrichers (GitHub Actions and others) would otherwise switch
    /// ANSI back on, so they are disabled and the capability is forced off.
    /// </summary>
    internal static IAnsiConsole PlainConsole(TextWriter writer)
    {
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer),
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.No,
            Enrichment = new ProfileEnrichment { UseDefaultEnrichers = false },
        });
        console.Profile.Capabilities.Ansi = false;
        console.Profile.Capabilities.Links = false;
        return console;
    }

    /// <summary>Called before a command runs: applies its global options.</summary>
    public void BeginCommand(string name, EngineSettings? commandSettings)
    {
        commandName = name;
        settings = commandSettings;
        paths = null;
        renderer = null;
        console = null;
        prompter = null;
        started = Stopwatch.GetTimestamp();
        Timings.Restart();
    }

    /// <summary>Called after a command, successful or not: prints timings and writes the local log.</summary>
    /// <param name="exitCode">The command's exit code.</param>
    /// <param name="exception">The failure, if any.</param>
    /// <param name="unexpected">True for failures that aren't the Learner's to fix; their type and stack trace are logged.</param>
    public void EndCommand(int exitCode, Exception? exception, bool unexpected = false)
    {
        Timings.Mark("command");
        if (settings?.Timings == true)
        {
            foreach (var (phase, elapsed) in Timings.Marks)
            {
                Renderer.Line(string.Create(CultureInfo.InvariantCulture, $"timing {phase}: {elapsed.TotalMilliseconds:F0} ms"));
            }
        }

        if (!unexpected && !Directory.Exists(Paths.StateDirectory))
        {
            // Commands that never touched Learner state (lint, coverage in CI) leave no trace.
            return;
        }

        var log = new LocalLog(Paths.Logs, Time, Files);
        log.Write(new LogEntry
        {
            Command = commandName,
            DurationMs = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            ExitCode = exitCode,
            ErrorType = exception?.GetType().FullName,
            StackTrace = unexpected ? exception?.StackTrace : null,
        });
        log.Prune();
    }

    /// <inheritdoc />
    public void Dispose() => database?.Dispose();

    private ProgressDatabase OpenDatabase()
    {
        var opened = ProgressDatabase.Open(Paths, Time, Files);
        Timings.Mark("database");
        Clock = ClockGuard.Read(opened.Connection, Time);
        if (opened.Migration.BackupPath is { } backup)
        {
            Renderer.Status(Outcome.Info, "Upgraded the progress database; the previous version was backed up to " + Path.GetRelativePath(Paths.RepoRoot, backup) + ".");
        }

        if (Clock.MovedBackwards)
        {
            Renderer.Status(Outcome.Warn, "Your system clock is behind a time the Engine has already seen. Timed attempts keep counting from the later time.");
        }

        return opened;
    }

    private ContentIndex LoadContent()
    {
        var loaded = ContentLoader.Load(Paths.RepoRoot);
        Timings.Mark("content");
        return loaded;
    }

    private bool IsPlain()
    {
        var environment = options.Environment ?? System.Environment.GetEnvironmentVariable;
        var redirected = options.OutputRedirected ?? System.Console.IsOutputRedirected;
        return RenderMode.IsPlain(settings?.Plain == true, environment, redirected, ProfilePeek.PlainMode(Paths));
    }

    private IRenderer CreateRenderer() =>
        IsPlain() ? new PlainRenderer(options.Output ?? System.Console.Out) : new RichRenderer(SpectreConsole());

    private IPrompter CreatePrompter() =>
        Renderer.IsPlain
            ? new PlainPrompter(options.Input ?? System.Console.In, options.Output ?? System.Console.Out)
            : new SpectrePrompter(SpectreConsole());

    /// <summary>The Spectre console for rich output (tests get one over their writer, without ANSI).</summary>
    internal IAnsiConsole SpectreConsole() => console ??= options.Output is null ? AnsiConsole.Console : PlainConsole(options.Output);
}
