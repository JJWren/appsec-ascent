using Ascent.Cli.Hosting;
using Ascent.Core;
using Microsoft.Extensions.Time.Testing;

namespace Ascent.Cli.Tests.TestSupport;

/// <summary>Runs <c>ascent</c> commands in-process against a throwaway repository, capturing output.</summary>
internal sealed class TestEngine : IDisposable
{
    private TestEngine(string root, bool plain)
    {
        Root = root;
        Plain = plain;
    }

    public string Root { get; }

    public bool Plain { get; }

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero));

    public Dictionary<string, string?> Environment { get; } = [];

    public string Input { get; set; } = string.Empty;

    public EnginePaths Paths => new(Root);

    /// <summary>The real repository root (the folder containing AppSecAscent.slnx).</summary>
    public static string RealRoot { get; } = FindRealRoot();

    public static TestEngine Empty(bool plain = true)
    {
        var root = Path.Join(Path.GetTempPath(), "ascent-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return new TestEngine(root, plain);
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
        Output = output,
        Input = new StringReader(Input),
        Environment = name => Environment.TryGetValue(name, out var value) ? value : null,
        OutputRedirected = Plain,
    });

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
