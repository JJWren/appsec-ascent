using Ascent.Cli.Tests.TestSupport;
using Ascent.Core.Domain;
using Ascent.Core.Errors;
using Ascent.Core.Progress;
using Ascent.Storage;
using Ascent.Tests.Shared;

namespace Ascent.Cli.Tests;

/// <summary>progress export and import (BAK-01, REL-U2-03), and output snapshots (UX-U2-01).</summary>
public sealed class ProgressCommandTests
{
    [Fact]
    [Trait("Rule", "BAK-01")]
    public async Task Export_writes_a_private_file_and_import_restores_it_after_confirmation()
    {
        using var fixture = CurriculumFixture.Create();
        using var engine = TestEngine.For(fixture);
        Award(engine, "q-1.1");

        var (exitCode, output) = await engine.RunAsync("progress", "export");

        exitCode.ShouldBe(ExitCodes.Ok);
        var file = Directory.EnumerateFiles(engine.Paths.Exports).ShouldHaveSingleItem();
        Path.GetFileName(file).ShouldBe("progress-20261003T120000Z.json");
        output.ShouldContain("PASS: Exported ");
        output.ShouldContain("WARN: This file is private: it holds your progress and settings. Don't commit or share it.");

        Award(engine, "q-1.2");
        var declined = await engine.RunWithInputAsync(["n"], "progress", "import", file);
        declined.Output.ShouldContain("This replaces all progress on this machine with the export from 2026-10-03T12:00:00.000Z.");
        declined.Output.ShouldContain("Nothing was changed.");
        Total(engine).ShouldBe(20);

        var restored = await engine.RunWithInputAsync(["y"], "progress", "import", file);
        restored.ExitCode.ShouldBe(ExitCodes.Ok);
        restored.Output.ShouldContain("PASS: Imported ");
        restored.Output.ShouldContain("Your previous progress was backed up to .ascent");
        Total(engine).ShouldBe(10);
    }

    [Fact]
    [Trait("Rule", "BAK-01")]
    public async Task A_damaged_database_is_moved_aside_and_the_export_restored()
    {
        using var fixture = CurriculumFixture.Create();
        using var engine = TestEngine.For(fixture);
        Award(engine, "q-1.1");
        var file = Path.Join(fixture.Root, "backup.json");
        (await engine.RunAsync("progress", "export", file)).ExitCode.ShouldBe(ExitCodes.Ok);
        File.WriteAllBytes(engine.Paths.Database, new byte[4096]);
        File.Delete(engine.Paths.Database + "-wal");
        File.Delete(engine.Paths.Database + "-shm");
        (await engine.RunAsync("status")).ExitCode.ShouldBe(ExitCodes.CheckFailed);

        var (exitCode, output) = await engine.RunWithInputAsync(["y"], "progress", "import", file);

        exitCode.ShouldBe(ExitCodes.Ok);
        output.ShouldContain("WARN: The damaged progress database was moved to .ascent");
        Total(engine).ShouldBe(10);
    }

    [Fact]
    public async Task Import_refuses_a_missing_or_invalid_file_without_touching_progress()
    {
        using var fixture = CurriculumFixture.Create();
        using var engine = TestEngine.For(fixture);

        var missing = await engine.RunAsync("progress", "import", Path.Join(fixture.Root, "nope.json"));
        missing.ExitCode.ShouldBe(ExitCodes.Usage);
        missing.Output.ShouldContain("doesn't exist");

        var invalid = await engine.RunAsync("progress", "import", fixture.Write("bad.json", "{\"format\":\"x\"}").Root + "/bad.json");
        invalid.ExitCode.ShouldBe(ExitCodes.CheckFailed);
        invalid.Output.ShouldContain("FAIL: That file can't be imported: its format isn't appsec-ascent/progress.");
        Directory.Exists(engine.Paths.StateDirectory).ShouldBeFalse();
    }

    [Fact]
    public async Task Plain_and_no_color_output_are_identical_and_free_of_escape_codes()
    {
        using var fixture = CurriculumFixture.Create().Quest("q-1.1", "1.1", "D1");
        using var plain = TestEngine.For(fixture);
        Award(plain, "q-1.1");

        var (_, plainOutput) = await plain.RunAsync("status", "--plain");

        plainOutput.ShouldBe(
            """
            Status
            ------
            Rank: Developer
            XP: 10 (next: Security Champion at 11)
            This week (2026-W40): 0/5 Stand-up days. Week streak: 0.
            Badges: none yet

            """.ReplaceLineEndings());

        using var noColor = TestEngine.For(fixture, plain: false);
        noColor.Environment["NO_COLOR"] = "1";
        var (_, noColorOutput) = await noColor.RunAsync("status");
        noColorOutput.ShouldBe(plainOutput);
        noColorOutput.ShouldNotContain("\u001b");
    }

    private static void Award(TestEngine engine, string questId)
    {
        using var database = LearnerCommandTests.Open(engine);
        new XpLedger(new XpStore(database), engine.Clock).Award(XpKind.Lesson, questId);
    }

    private static long Total(TestEngine engine)
    {
        using var database = LearnerCommandTests.Open(engine);
        return new XpLedger(new XpStore(database), engine.Clock).Total;
    }
}
