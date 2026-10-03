using System.Text.Json;
using Ascent.Core.Logging;
using Ascent.Core.Platform;
using Ascent.Engine.Tests.TestSupport;
using Microsoft.Extensions.Time.Testing;

namespace Ascent.Engine.Tests.Logging;

public sealed class LocalLogTests
{
    [Fact]
    public void Writes_one_allowlisted_json_line_per_entry()
    {
        using var temp = new TempDirectory();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero));
        var log = new LocalLog(temp.Combine("logs"), clock, OwnerOnlyFiles.ForCurrentOs());

        log.Write(new LogEntry { Command = "standup", DurationMs = 42, ExitCode = 0 });
        log.Write(new LogEntry { Command = "flag", ExitCode = 1, ErrorType = "Ascent.Core.Errors.AscentException", RuleIds = ["FLAG-03"] });

        var path = temp.Combine("logs", "ascent-2026-10-03.log");
        var lines = File.ReadAllLines(path);
        lines.Length.ShouldBe(2);

        using var first = JsonDocument.Parse(lines[0]);
        first.RootElement.GetProperty("command").GetString().ShouldBe("standup");
        first.RootElement.GetProperty("durationMs").GetInt64().ShouldBe(42);
        first.RootElement.TryGetProperty("errorType", out _).ShouldBeFalse();
        first.RootElement.GetProperty("time").GetString()!.ShouldStartWith("2026-10-03T12:00:00");

        using var second = JsonDocument.Parse(lines[1]);
        second.RootElement.GetProperty("ruleIds")[0].GetString().ShouldBe("FLAG-03");
    }

    [Fact]
    public void Entries_have_no_field_for_free_text()
    {
        // The allowlist is the record's shape: no Message, Content, Answer or Output property exists.
        var names = typeof(LogEntry).GetProperties().Select(p => p.Name).ToList();
        names.ShouldBe(["Time", "Command", "DurationMs", "ExitCode", "ErrorType", "StackTrace", "RuleIds", "SealedFailure"], ignoreOrder: true);
    }

    [Fact]
    public void Old_files_are_pruned_after_14_days()
    {
        using var temp = new TempDirectory();
        var logs = temp.Combine("logs");
        temp.WriteFile("logs/ascent-2026-09-01.log", "{}");
        temp.WriteFile("logs/ascent-2026-09-20.log", "{}");
        temp.WriteFile("logs/ascent-2026-09-19.log", "{}");
        temp.WriteFile("logs/ascent-2026-09-18.log", "{}");
        temp.WriteFile("logs/ascent-garbage.log", "{}");
        temp.WriteFile("logs/other.txt", "keep");
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero));

        var deleted = new LocalLog(logs, clock, OwnerOnlyFiles.ForCurrentOs()).Prune();

        // Exactly 14 days old (09-19) is kept; older files go.
        deleted.ShouldBe(2);
        File.Exists(Path.Join(logs, "ascent-2026-09-19.log")).ShouldBeTrue();
        File.Exists(Path.Join(logs, "ascent-2026-09-18.log")).ShouldBeFalse();
        File.Exists(Path.Join(logs, "ascent-2026-09-20.log")).ShouldBeTrue();
        File.Exists(Path.Join(logs, "ascent-garbage.log")).ShouldBeTrue();
        File.Exists(Path.Join(logs, "other.txt")).ShouldBeTrue();
    }

    [Fact]
    public void Pruning_a_missing_folder_does_nothing()
    {
        using var temp = new TempDirectory();
        new LocalLog(temp.Combine("absent"), TimeProvider.System, OwnerOnlyFiles.ForCurrentOs()).Prune().ShouldBe(0);
    }

    [Fact]
    public void A_locked_log_file_never_fails_the_command()
    {
        using var temp = new TempDirectory();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero));
        var log = new LocalLog(temp.Combine("logs"), clock, OwnerOnlyFiles.ForCurrentOs());
        log.Write(new LogEntry { Command = "first" });

        using (new FileStream(temp.Combine("logs", "ascent-2026-10-03.log"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Should.NotThrow(() => log.Write(new LogEntry { Command = "while locked" }));
        }
    }
}
