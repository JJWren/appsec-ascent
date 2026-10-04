using Ascent.Core;
using Ascent.Core.Errors;
using Ascent.Core.Interaction;
using Ascent.Core.Platform;
using Ascent.Maintainer.Authoring;
using Spectre.Console;

namespace Ascent.Maintainer.Hosting;

/// <summary>Everything the maintainer tool's composition root can swap; tests pass fakes.</summary>
public sealed record MaintainerOptions
{
    /// <summary>The public repository root; null means the nearest folder containing <c>AppSecAscent.slnx</c>.</summary>
    public string? RepoRoot { get; init; }

    /// <summary>Where the key, backup and ledger live; null means <c>%APPDATA%/AppSecAscent/maintainer</c>.</summary>
    public string? MaintainerDirectory { get; init; }

    /// <summary>The clock.</summary>
    public TimeProvider Time { get; init; } = TimeProvider.System;

    /// <summary>The randomness source.</summary>
    public IRandomSource Random { get; init; } = CryptoRandomSource.Instance;

    /// <summary>Owner-only file creation; null means the adapter for the running OS.</summary>
    public IOwnerOnlyFiles? Files { get; init; }

    /// <summary>Where output goes; null means the console.</summary>
    public TextWriter? Output { get; init; }

    /// <summary>The prompter; null means interactive Spectre prompts.</summary>
    public IPrompter? Prompter { get; init; }

    /// <summary>Creates the key store for a path; null means DPAPI on Windows (P6).</summary>
    public Func<string, IKeyStore>? KeyStoreFactory { get; init; }
}

/// <summary>The maintainer tool's composition root.</summary>
public sealed class MaintainerContext
{
    private readonly MaintainerOptions options;
    private IOwnerOnlyFiles? files;

    /// <summary>Creates the context.</summary>
    public MaintainerContext(MaintainerOptions? options = null) => this.options = options ?? new MaintainerOptions();

    /// <summary>The clock.</summary>
    public TimeProvider Time => options.Time;

    /// <summary>The randomness source.</summary>
    public IRandomSource Random => options.Random;

    /// <summary>Owner-only file creation.</summary>
    public IOwnerOnlyFiles Files => files ??= options.Files ?? OwnerOnlyFiles.ForCurrentOs();

    /// <summary>Where output goes.</summary>
    public TextWriter Out => options.Output ?? Console.Out;

    /// <summary>The prompter.</summary>
    public IPrompter Prompter => options.Prompter ?? new MaintainerPrompter(AnsiConsole.Console);

    /// <summary>Where the key, backup and ledger live.</summary>
    public string MaintainerDirectory => options.MaintainerDirectory
        ?? Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AppSecAscent", "maintainer");

    /// <summary>The signing ledger.</summary>
    public SigningLedger Ledger => new(Path.Join(MaintainerDirectory, "signing-ledger.jsonl"), Files);

    /// <summary>The path of the trusted public key inside the repository.</summary>
    public static string PublicKeyPath(string repoRoot) => Path.Join(repoRoot, "engine", "src", "Ascent.Sealing", "Keys", "maintainer.pub.pem");

    /// <summary>
    /// The public repository root for a command. It must contain <c>AppSecAscent.slnx</c>, so files such as the
    /// trusted public key are never written into some other folder.
    /// </summary>
    public string RepoRoot(string? explicitRoot)
    {
        var root = EnginePaths.FindRepoRoot(explicitRoot ?? options.RepoRoot, Directory.GetCurrentDirectory());
        return File.Exists(Path.Join(root, EnginePaths.SolutionFile))
            ? root
            : throw new UsageException(
                "This isn't the AppSec Ascent repository: " + root,
                "Run ascent-maint from inside the repository, or pass --public <repository root>.");
    }

    /// <summary>The key store; signing needs Windows unless a test supplies a store.</summary>
    public IKeyStore KeyStore()
    {
        var path = Path.Join(MaintainerDirectory, "signing-key.dpapi");
        if (options.KeyStoreFactory is { } factory)
        {
            return factory(path);
        }

        if (OperatingSystem.IsWindows())
        {
            return new DpapiKeyStore(path, Files);
        }

        throw new AscentException(
            "This command needs Windows: the signing key is protected with DPAPI (ND-U2-1).",
            "Run it on the maintainer's Windows machine. Sealing and verifying work on any OS.");
    }
}
