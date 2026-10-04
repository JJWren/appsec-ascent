using System.Globalization;
using System.Xml;
using Ascent.Core;
using Ascent.Core.Curriculum;
using Ascent.Core.Errors;
using Ascent.Core.Platform;
using Ascent.Sealing;
using Ascent.Sealing.KeyRelease;
using Ascent.Sealing.Unsealing;

namespace Ascent.Labs;

/// <summary>The result of running a Lab's security tests.</summary>
/// <param name="Built">False when the tests didn't build or run, so no results exist.</param>
/// <param name="Total">Tests run.</param>
/// <param name="Passed">Tests passed.</param>
/// <param name="Failed">Tests failed.</param>
/// <param name="FailedTests">Names of failed tests (at most 20).</param>
/// <param name="Problem">When the tests didn't run: the last lines of the build output, sanitized.</param>
public sealed record TestRun(bool Built, int Total, int Passed, int Failed, IReadOnlyList<string> FailedTests, string? Problem)
{
    /// <summary>True when every test ran and passed (LABE-02).</summary>
    public bool AllPassed => Built && Total > 0 && Failed == 0 && Passed == Total;
}

/// <summary>
/// Runs a Lab's released security tests against the Learner's workspace (LABE-02, P3). The tests are unpacked into an
/// unsealed scope whose MSBuild stopper files keep the public repository's build settings out, built and run with
/// <c>ThroughlineRoot</c> pointing at the workspace, and read back from a TRX report. Lab tests are xUnit.net v3
/// projects, so the runner's own <c>-result-trx</c> works whatever <c>dotnet test</c> mode the repository uses.
/// </summary>
public sealed class VerifyRunner
{
    /// <summary>The MSBuild property that points the tests at the Learner's code.</summary>
    public const string ThroughlineRootProperty = "ThroughlineRoot";

    private readonly SealedStore store;
    private readonly IProcessRunner processes;
    private readonly EnginePaths paths;
    private readonly IOwnerOnlyFiles files;
    private readonly IRandomSource random;

    /// <summary>Creates the runner.</summary>
    public VerifyRunner(SealedStore store, IProcessRunner processes, EnginePaths paths, IOwnerOnlyFiles files, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(processes);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(random);
        this.store = store;
        this.processes = processes;
        this.paths = paths;
        this.files = files;
        this.random = random;
    }

    /// <summary>Runs the Lab's tests against <paramref name="throughline"/>.</summary>
    public async Task<TestRun> RunAsync(LabInfo lab, string throughline, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lab);
        using var scope = UnsealedScope.Create(paths, files, random);
        string folder;
        using (var tests = store.Open(lab.TestsRef, ReleaseContext.FlagVerified(lab.Id)))
        {
            folder = scope.Extract(tests, "tests");
        }

        var projects = Directory.EnumerateFiles(folder, "*.csproj", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 2 }).ToList();
        if (projects.Count != 1)
        {
            throw new AscentException("The Lab's tests couldn't be found in their bundle.", "Report it with 'ascent bug " + lab.Id + "'.");
        }

        var trx = Path.Join(scope.Directory, "results.trx");
        var result = await processes.RunAsync(
            new ToolCommand(
                ExternalTool.Dotnet,
                ["run", "--project", projects[0], "--configuration", "Release", "--property:" + ThroughlineRootProperty + "=" + Path.GetFullPath(throughline), "--", "-result-trx", trx, "-noColor", "-noLogo"],
                folder,
                ToolTimeouts.Verify)
            {
                Environment = new Dictionary<string, string>
                {
                    ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
                    ["DOTNET_NOLOGO"] = "1",
                    ["MSBUILDDISABLENODEREUSE"] = "1",
                },
            },
            cancellationToken);

        if (!File.Exists(trx))
        {
            var problem = result.TimedOut
                ? "The tests took longer than " + ToolTimeouts.Verify.TotalMinutes.ToString(CultureInfo.InvariantCulture) + " minutes and were stopped."
                : Tail(result.StandardOutput + "\n" + result.StandardError);
            return new TestRun(false, 0, 0, 0, [], problem);
        }

        return Trx.Read(trx);
    }

    private static string Tail(string output)
    {
        var lines = SafeText.Sanitize(output, 200_000).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join('\n', lines.TakeLast(12));
    }
}

/// <summary>Reads the counts and failed test names from a TRX report, with DTDs and external entities refused.</summary>
public static class Trx
{
    private const int MaxFailedNames = 20;

    /// <summary>Reads a TRX file.</summary>
    public static TestRun Read(string path)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreComments = true };
        int total = 0, passed = 0, failed = 0;
        var failedTests = new List<string>();
        try
        {
            using var reader = XmlReader.Create(path, settings);
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                if (reader.LocalName == "Counters")
                {
                    total = Count(reader, "total");
                    passed = Count(reader, "passed");
                    failed = Count(reader, "failed") + Count(reader, "error") + Count(reader, "timeout") + Count(reader, "aborted");
                }
                else if (reader.LocalName == "UnitTestResult" && reader.GetAttribute("outcome") is "Failed" or "Error" or "Timeout" or "Aborted"
                         && failedTests.Count < MaxFailedNames && reader.GetAttribute("testName") is { } name)
                {
                    failedTests.Add(SafeText.Sanitize(name, 300));
                }
            }
        }
        catch (XmlException ex)
        {
            throw new AscentException("The test results couldn't be read.", "Run 'ascent verify' again; if it keeps failing, report it with 'ascent bug'.", ExitCodes.CheckFailed, ex);
        }

        return new TestRun(true, total, passed, failed, failedTests, null);
    }

    private static int Count(XmlReader reader, string attribute) =>
        int.TryParse(reader.GetAttribute(attribute), NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : 0;
}
