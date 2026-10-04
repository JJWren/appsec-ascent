using Ascent.Core.Interaction;
using Ascent.Core.Platform;
using Ascent.Maintainer.Authoring;
using Ascent.Maintainer.Hosting;
using Microsoft.Extensions.Time.Testing;

namespace Ascent.Maintainer.Tests.TestSupport;

/// <summary>A throwaway public repository, sealed-repository sources and maintainer folder, plus in-process command runs.</summary>
internal sealed class MaintainerFixture : IDisposable
{
    public MaintainerFixture()
    {
        Root = Path.Join(Path.GetTempPath(), "ascent-maint-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Repo);
        Directory.CreateDirectory(Sources);
        File.WriteAllText(Path.Join(Repo, "AppSecAscent.slnx"), "<Solution />");
    }

    public string Root { get; }

    public string Repo => Path.Join(Root, "public");

    public string Sources => Path.Join(Root, "sealed-sources");

    public string MaintainerDirectory => Path.Join(Root, "maintainer");

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero));

    public MemoryKeyStore KeyStore { get; } = new();

    public ScriptedPrompter Prompter { get; } = new();

    public MaintainerContext Context(TextWriter output) => new(new MaintainerOptions
    {
        RepoRoot = Repo,
        MaintainerDirectory = MaintainerDirectory,
        Time = Clock,
        Output = output,
        Prompter = Prompter,
        KeyStoreFactory = path =>
        {
            KeyStore.Location = path;
            return KeyStore;
        },
    });

    public async Task<(int ExitCode, string Output)> RunAsync(params string[] args)
    {
        using var output = new StringWriter();
        var exitCode = await MaintainerApp.Create(Context(output)).RunAsync(args, TestContext.Current.CancellationToken);
        return (exitCode, output.ToString());
    }

    public string WriteSource(string relativePath, string content)
    {
        var path = Path.Join(Sources, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public string WriteRepo(string relativePath, string content)
    {
        var path = Path.Join(Repo, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
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
}

/// <summary>An in-memory key store (the DPAPI store is Windows-only).</summary>
internal sealed class MemoryKeyStore : IKeyStore
{
    private byte[]? key;

    public string Location { get; set; } = "memory";

    public bool Exists => key is not null;

    public void Save(ReadOnlySpan<byte> pkcs8) => key = pkcs8.ToArray();

    public byte[] Load() => (byte[])(key ?? throw new InvalidOperationException("No key.")).Clone();
}

/// <summary>Answers prompts from a queue; secrets come from <see cref="Secrets"/>.</summary>
internal sealed class ScriptedPrompter : IPrompter
{
    public Queue<string> Secrets { get; } = new();

    public Queue<string> Answers { get; } = new();

    public bool Confirm(string question, bool defaultValue = false) => Answers.Count > 0 ? Answers.Dequeue() == "y" : defaultValue;

    public string Ask(string question, Func<string, string?>? validate = null) => Answers.Dequeue();

    public int Choose(string question, IReadOnlyList<string> options) => int.Parse(Answers.Dequeue(), System.Globalization.CultureInfo.InvariantCulture);

    public string AskSecret(string question) => Secrets.Dequeue();

    public bool ConfirmTyped(string question, string expected) => Answers.Dequeue() == expected;
}

/// <summary>Owner-only file helpers for tests.</summary>
internal static class Files
{
    public static IOwnerOnlyFiles OwnerOnly { get; } = OwnerOnlyFiles.ForCurrentOs();
}
