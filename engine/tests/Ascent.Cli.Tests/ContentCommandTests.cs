using System.Text.Json;
using Ascent.Cli.Tests.TestSupport;

namespace Ascent.Cli.Tests;

/// <summary>The U1 commands behave as before on the host (smoke tests against the real repository).</summary>
public sealed class ContentCommandTests
{
    [Fact]
    public async Task Lint_passes_on_the_real_repository()
    {
        using var engine = TestEngine.Empty();

        var (exitCode, output) = await engine.RunAsync("lint", "--root", TestEngine.RealRoot, "--json");

        exitCode.ShouldBe(0);
        using var report = JsonDocument.Parse(output);
        report.RootElement.ValueKind.ShouldBe(JsonValueKind.Object);
    }

    [Fact]
    public async Task Lint_prints_findings_in_plain_text()
    {
        using var engine = TestEngine.Empty();

        var (exitCode, output) = await engine.RunAsync("lint", "--root", TestEngine.RealRoot);

        exitCode.ShouldBe(0);
        output.ShouldContain("0 error(s)");
    }

    [Fact]
    public async Task Coverage_reports_counts_only()
    {
        using var engine = TestEngine.Empty();

        var (exitCode, output) = await engine.RunAsync("coverage", "--root", TestEngine.RealRoot);
        var (jsonExit, json) = await engine.RunAsync("coverage", "--root", TestEngine.RealRoot, "--json", "--gate", "d1");

        exitCode.ShouldBe(0);
        output.ShouldContain("Objectives complete: ");
        jsonExit.ShouldBe(1); // Nothing has been built for D1 yet, so its gate is open.
        using var report = JsonDocument.Parse(json);
        report.RootElement.GetProperty("gaps").GetArrayLength().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Coverage_without_an_outline_fails()
    {
        using var engine = TestEngine.Empty();

        var (exitCode, output) = await engine.RunAsync("coverage");

        exitCode.ShouldBe(1);
        output.ShouldContain("No valid outline");
    }

    [Fact]
    public async Task Exceptions_check_and_emit_work_on_the_real_register()
    {
        using var engine = TestEngine.Empty();

        var (checkExit, checkOutput) = await engine.RunAsync("exceptions", "check", "--root", TestEngine.RealRoot, "--today", "2026-10-03");
        var (jsonExit, json) = await engine.RunAsync("exceptions", "check", "--root", TestEngine.RealRoot, "--json");

        checkExit.ShouldBe(0);
        checkOutput.ShouldContain("0 error(s)");
        jsonExit.ShouldBe(0);
        JsonDocument.Parse(json).Dispose();
    }

    [Fact]
    public async Task Exceptions_emit_writes_inside_the_repository_only()
    {
        using var engine = TestEngine.Empty();
        engine.WriteFile("security/exceptions.yaml", "exceptions: []\n");

        var (exitCode, output) = await engine.RunAsync("exceptions", "emit", "--trivy", ".trivyignore", "--nuget", "security/suppressions.props");
        var (escapeExit, escapeOutput) = await engine.RunAsync("exceptions", "emit", "--trivy", "../outside.txt");
        var (missingExit, _) = await engine.RunAsync("exceptions", "emit");

        exitCode.ShouldBe(0);
        output.ShouldContain("Wrote .trivyignore");
        File.Exists(Path.Join(engine.Root, ".trivyignore")).ShouldBeTrue();
        File.Exists(Path.Join(engine.Root, "security", "suppressions.props")).ShouldBeTrue();

        escapeExit.ShouldBe(1);
        escapeOutput.ShouldContain("Refused the path '../outside.txt'");
        File.Exists(Path.Join(Path.GetDirectoryName(engine.Root)!, "outside.txt")).ShouldBeFalse();

        missingExit.ShouldBe(2);
    }

    [Fact]
    public async Task Verify_bundles_counts_bundles()
    {
        using var engine = TestEngine.Empty();

        var (exitCode, output) = await engine.RunAsync("verify-bundles", "--root", TestEngine.RealRoot);
        var (jsonExit, json) = await engine.RunAsync("verify-bundles", "--root", TestEngine.RealRoot, "--json");

        exitCode.ShouldBe(0);
        output.ShouldContain("Sealed Bundle(s).");
        jsonExit.ShouldBe(0);
        JsonDocument.Parse(json).Dispose();
    }

    [Fact]
    public async Task Help_lists_the_commands()
    {
        using var engine = TestEngine.Empty();

        var (exitCode, output) = await engine.RunAsync("--help");

        exitCode.ShouldBe(0);
        output.ShouldContain("lint");
        output.ShouldContain("verify-bundles");
    }
}
