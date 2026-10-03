using Ascent.Core.Interaction;
using Ascent.Core.Platform;

namespace Ascent.Cli.Hosting;

/// <summary>
/// Everything the composition root can swap. Production uses the defaults; tests pass fakes (fixed clocks, seeded
/// randomness, recorded processes, captured output and scripted answers).
/// </summary>
public sealed record EngineOptions
{
    /// <summary>The repository root; null means the nearest folder containing <c>AppSecAscent.slnx</c>.</summary>
    public string? RepoRoot { get; init; }

    /// <summary>The clock.</summary>
    public TimeProvider Time { get; init; } = TimeProvider.System;

    /// <summary>The randomness source.</summary>
    public IRandomSource Random { get; init; } = CryptoRandomSource.Instance;

    /// <summary>Owner-only file creation; null means the adapter for the running OS.</summary>
    public IOwnerOnlyFiles? Files { get; init; }

    /// <summary>The external-tool runner; null means the real <see cref="ProcessRunner"/>.</summary>
    public IProcessRunner? Processes { get; init; }

    /// <summary>The HTTP transport; null means a hardened <see cref="System.Net.Http.SocketsHttpHandler"/>.</summary>
    public HttpMessageHandler? HttpTransport { get; init; }

    /// <summary>Where output goes; null means the console.</summary>
    public TextWriter? Output { get; init; }

    /// <summary>Where plain-mode answers come from; null means standard input.</summary>
    public TextReader? Input { get; init; }

    /// <summary>The prompter; null means one matching the output mode.</summary>
    public IPrompter? Prompter { get; init; }

    /// <summary>Environment variable lookup; null means the real environment.</summary>
    public Func<string, string?>? Environment { get; init; }

    /// <summary>Whether output is redirected; null means ask the console.</summary>
    public bool? OutputRedirected { get; init; }
}
